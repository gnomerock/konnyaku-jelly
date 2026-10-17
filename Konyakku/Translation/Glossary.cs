using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Konyakku.Translation;

/// <summary>A Japanese FFXIV term and the meaning players use, from the shared glossary or the player's own list.</summary>
public sealed class GlossaryTerm
{
    public string Ja { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed class GlossaryFile
{
    public int Version { get; set; }
    public List<GlossaryTerm> Terms { get; set; } = [];
}

/// <summary>
/// The shared FFXIV glossary, served by the glossary Worker (worker/ in the repo), plus the player's own words.
/// Each message only gets the terms that appear in it, so the glossary can grow without making every prompt longer.
/// </summary>
internal sealed class Glossary : IDisposable
{
    public const string Server = "https://konnyaku-glossary.nuttakorn-buajarern.workers.dev";
    /// <summary>The server takes at most this many words per share.</summary>
    private const int ShareBatch = 50;
    private const int MaxMatches = 15;
    private const int MaxTerms = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Configuration config;
    private readonly string cachePath;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly CancellationTokenSource shutdown = new();
    private volatile GlossaryFile shared;

    public Glossary(Configuration config, string directory)
    {
        this.config = config;
        cachePath = Path.Combine(directory, "glossary-cache.json");
        shared = Newer(LoadBuiltIn(), LoadCache());
        _ = Task.Run(RefreshAsync);
    }

    public int Version => shared.Version;
    public int Count => shared.Terms.Count;
    /// <summary>Whether the shared glossary was downloaded (now or on an earlier start), not the built-in copy.</summary>
    public bool Downloaded { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// The &lt;glossary&gt; block for this message, or "" when no term appears in it. Longer terms win, so 反時計回り
    /// doesn't also match 時計回り. The player's own words replace shared ones with the same Japanese.
    /// </summary>
    public string BlockFor(string text)
    {
        List<GlossaryTerm> mine;
        lock (config.MyTerms) mine = config.MyTerms.Where(t => t.Ja.Length > 0 && t.En.Length > 0).ToList();
        var mineJa = mine.Select(t => t.Ja).ToHashSet();
        var candidates = mine.Concat(shared.Terms.Where(t => !mineJa.Contains(t.Ja)))
            .Where(t => text.Contains(t.Ja, StringComparison.Ordinal))
            .OrderByDescending(t => t.Ja.Length);

        var covered = new bool[text.Length];
        var matched = new List<GlossaryTerm>();
        foreach (var term in candidates)
        {
            var uncovered = false;
            for (var at = text.IndexOf(term.Ja, StringComparison.Ordinal); at >= 0; at = text.IndexOf(term.Ja, at + 1, StringComparison.Ordinal))
            {
                if (!Array.TrueForAll(covered[at..(at + term.Ja.Length)], c => c)) uncovered = true;
                Array.Fill(covered, true, at, term.Ja.Length);
            }
            if (uncovered) matched.Add(term);
            if (matched.Count == MaxMatches) break;
        }
        if (matched.Count == 0) return string.Empty;

        var block = new StringBuilder("<glossary>\n");
        foreach (var t in matched)
            block.Append($"{t.Ja} = {t.En}").Append(string.IsNullOrWhiteSpace(t.Note) ? "" : $" ({t.Note})").Append('\n');
        return block.Append("</glossary>\n").ToString();
    }

    /// <summary>Adds or replaces one of the player's own words. Translation reads the list from background threads, hence the lock.</summary>
    public void SetMine(string ja, string en)
    {
        ja = ja.Trim();
        en = en.Trim();
        if (ja.Length == 0 || en.Length == 0) return;
        lock (config.MyTerms)
        {
            config.MyTerms.RemoveAll(t => t.Ja == ja);
            config.MyTerms.Add(new GlossaryTerm { Ja = ja, En = en });
        }
        config.Save();
    }

    public void RemoveMine(string ja)
    {
        lock (config.MyTerms) config.MyTerms.RemoveAll(t => t.Ja == ja);
        config.Save();
    }

    public IReadOnlyList<GlossaryTerm> Mine()
    {
        lock (config.MyTerms) return config.MyTerms.ToArray();
    }

    /// <summary>
    /// Sends words to the glossary server: only each word, its meaning and the random install ID. Returns how many the
    /// server accepted. Throws with a readable message on failure; words sent before a failure stay sent.
    /// </summary>
    public async Task<int> ShareAsync(IReadOnlyList<CollectedTerm> words, Action<IReadOnlyList<CollectedTerm>> sent)
    {
        if (!Guid.TryParse(config.InstallId, out _))
        {
            config.InstallId = Guid.NewGuid().ToString("N");
            config.Save();
        }

        var accepted = 0;
        foreach (var batch in words.Chunk(ShareBatch))
        {
            var body = JsonSerializer.Serialize(new
            {
                installId = config.InstallId,
                terms = batch.Select(w => new { term = w.Term, meaning = w.Meaning }),
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var res = await http.PostAsync($"{Server}/v1/terms", content, shutdown.Token);
            if ((int)res.StatusCode == 429) throw new InvalidOperationException("Too many shares at once. Try again in a minute.");
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The glossary server said {(int)res.StatusCode} {res.ReasonPhrase}.");

            using var reply = JsonDocument.Parse(await res.Content.ReadAsStringAsync(shutdown.Token));
            accepted += reply.RootElement.GetProperty("accepted").GetInt32();
            sent(batch);
        }
        return accepted;
    }

    private async Task RefreshAsync()
    {
        try
        {
            var json = await http.GetStringAsync($"{Server}/v1/glossary", shutdown.Token);
            var file = Clean(JsonSerializer.Deserialize<GlossaryFile>(json, JsonOptions));
            Downloaded = true;
            LastError = null;
            if (file == null || file.Version <= shared.Version) return;
            shared = file;
            await File.WriteAllTextAsync(cachePath, json, shutdown.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            Plugin.Log.Warning(ex, "Couldn't download the shared glossary; using the saved copy");
        }
    }

    private static GlossaryFile LoadBuiltIn()
    {
        using var stream = typeof(Glossary).Assembly.GetManifestResourceStream("Konyakku.glossary.json");
        return stream == null ? new GlossaryFile() : Clean(JsonSerializer.Deserialize<GlossaryFile>(stream, JsonOptions)) ?? new GlossaryFile();
    }

    private GlossaryFile? LoadCache()
    {
        try
        {
            if (!File.Exists(cachePath)) return null;
            var file = Clean(JsonSerializer.Deserialize<GlossaryFile>(File.ReadAllText(cachePath), JsonOptions));
            Downloaded = file != null;
            return file;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Couldn't read the saved glossary");
            return null;
        }
    }

    private static GlossaryFile Newer(GlossaryFile builtIn, GlossaryFile? cached) =>
        cached != null && cached.Version > builtIn.Version ? cached : builtIn;

    /// <summary>The glossary goes into prompts, so drop empty or oversized entries from a downloaded file.</summary>
    private static GlossaryFile? Clean(GlossaryFile? file)
    {
        if (file == null) return null;
        file.Terms = file.Terms
            .Select(t => new GlossaryTerm { Ja = t.Ja?.Trim() ?? "", En = t.En?.Trim() ?? "", Note = t.Note?.Trim() })
            .Where(t => t.Ja.Length is > 0 and <= 20 && t.En.Length is > 0 and <= 80 && (t.Note?.Length ?? 0) <= 80)
            .Where(t => !$"{t.Ja}{t.En}{t.Note}".Any(c => c is '\n' or '<' or '>'))
            .DistinctBy(t => t.Ja)
            .Take(MaxTerms)
            .ToList();
        return file;
    }

    public void Dispose()
    {
        shutdown.Cancel();
        http.Dispose();
        shutdown.Dispose();
    }
}
