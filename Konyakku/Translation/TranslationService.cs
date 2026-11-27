using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Konyakku.Translation;

/// <summary>Translates through Google Translate's free web endpoint (no key, no login).</summary>
internal sealed class TranslationService : IDisposable
{
    // shortcut: unofficial endpoint used by browser extensions, may be rate limited; move to Cloud Translation API (key) if it gets blocked.
    private const string Endpoint = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&sl=auto";

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim slots = new(3);
    private readonly ConcurrentDictionary<string, string> cache = new();
    private readonly CancellationTokenSource shutdown = new();

    public string? LastError { get; private set; }

    public void ClearError() => LastError = null;

    /// <summary>Returns the translation, or null when the text is already in the target language.</summary>
    public async Task<string?> TranslateAsync(string text, string target, CancellationToken ct = default)
    {
        var code = Language.Code(target);
        var key = $"{code}\u0001{text}";
        if (cache.TryGetValue(key, out var hit)) return hit.Length == 0 ? null : hit;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, shutdown.Token);
        await slots.WaitAsync(linked.Token);
        try
        {
            // POST keeps long Party Finder descriptions out of the URL.
            using var form = new FormUrlEncodedContent([new("q", text)]);
            using var resp = await http.PostAsync($"{Endpoint}&tl={code}", form, linked.Token);
            var body = await resp.Content.ReadAsStringAsync(linked.Token);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Google Translate {(int)resp.StatusCode}: {(body.Length > 300 ? body[..300] + "…" : body)}");

            // Reply: [[["translated", "original", ...], ...], null, "detected source", ...]
            var json = JsonNode.Parse(body);
            var detected = (string?)json?[2];
            var result = detected == code
                ? string.Empty
                : string.Concat(json?[0]?.AsArray().Select(s => (string?)s?[0]) ?? []).Trim();

            if (cache.Count > 2000) cache.Clear();
            cache[key] = result;
            LastError = null;
            return result.Length == 0 ? null : result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            throw;
        }
        finally
        {
            slots.Release();
        }
    }

    public void Dispose()
    {
        shutdown.Cancel();
        http.Dispose();
        shutdown.Dispose();
    }
}
