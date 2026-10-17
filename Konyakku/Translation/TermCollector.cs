using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dalamud.Game.Text;

namespace Konyakku.Translation;

/// <summary>An FFXIV word the AI spotted in received Japanese chat, with its guess at the meaning.</summary>
public sealed class CollectedTerm
{
    public string Term { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
    public int Seen { get; set; }
    /// <summary>Already sent to the glossary server with this meaning.</summary>
    public bool Shared { get; set; }
}

/// <summary>Where a received message came from, so the collector can skip private channels and the sender's name.</summary>
internal readonly record struct Received(bool PublicChannel, string Sender);

/// <summary>
/// Opt-in list of FFXIV words from received Japanese chat. Only single terms are kept: never the message, sender,
/// channel or time. The list is saved to its own file next to the plugin config, and only leaves this PC when the
/// player shares words (<see cref="Glossary.ShareAsync"/>).
/// </summary>
internal sealed class TermCollector
{
    public const int MaxTerms = 500;
    private const int MaxTermLength = 12;
    private const int MaxMeaningLength = 60;

    /// <summary>Channels anyone nearby or in the duty can read. Tells, Free Company, linkshells and PvP teams are never collected.</summary>
    private static readonly HashSet<XivChatType> PublicChannels =
    [
        XivChatType.Say, XivChatType.Shout, XivChatType.Yell,
        XivChatType.Party, XivChatType.CrossParty, XivChatType.Alliance, XivChatType.NoviceNetwork,
    ];

    // Keeps Japanese readable in the file instead of \uXXXX escapes.
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Configuration config;
    private readonly string path;
    private readonly object gate = new();
    private readonly List<CollectedTerm> terms;

    public TermCollector(Configuration config, string directory)
    {
        this.config = config;
        path = Path.Combine(directory, "collected-terms.json");
        terms = Load(path);
    }

    public static bool IsPublic(XivChatType type) => PublicChannels.Contains(type);

    /// <summary>Whether to ask the AI for FFXIV words in this message.</summary>
    public bool Wants(Received? from, string text) =>
        config.CollectTerms && from is { PublicChannel: true } && text.Any(IsJapanese);

    public int Count
    {
        get { lock (gate) return terms.Count; }
    }

    public IReadOnlyList<CollectedTerm> Snapshot()
    {
        lock (gate) return terms.Select(t => new CollectedTerm { Term = t.Term, Meaning = t.Meaning, Seen = t.Seen, Shared = t.Shared }).ToArray();
    }

    /// <summary>Adds the terms that pass the filters. <paramref name="original"/> is only used to check the term is really in it.</summary>
    public void Add(Received from, string original, IEnumerable<(string Term, string Meaning)> found)
    {
        var nameParts = from.Sender.Split([' ', '@'], StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length >= 3).ToArray();
        var changed = false;
        lock (gate)
        {
            foreach (var (rawTerm, rawMeaning) in found)
            {
                var term = rawTerm.Trim();
                var meaning = rawMeaning.Trim();
                if (!Acceptable(term, meaning, original, nameParts)) continue;

                var existing = terms.Find(t => t.Term == term);
                if (existing != null) existing.Seen++;
                else if (terms.Count < MaxTerms) terms.Add(new CollectedTerm { Term = term, Meaning = meaning, Seen = 1 });
                else continue;
                changed = true;
            }
            if (changed) Save();
        }
    }

    /// <summary>Marks words as shared, unless their meaning changed since the snapshot that was sent.</summary>
    public void MarkShared(IEnumerable<CollectedTerm> sent)
    {
        lock (gate)
        {
            foreach (var s in sent)
                if (terms.Find(t => t.Term == s.Term && t.Meaning == s.Meaning) is { } t) t.Shared = true;
            Save();
        }
    }

    public void Remove(string term)
    {
        lock (gate)
        {
            if (terms.RemoveAll(t => t.Term == term) > 0) Save();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            terms.Clear();
            Save();
        }
    }

    /// <summary>
    /// Character names in FFXIV only use Latin letters, so a term made of Japanese characters with no letters or digits
    /// can't be anyone's name, a Name@World, a time or coordinates.
    /// </summary>
    private static bool Acceptable(string term, string meaning, string original, string[] nameParts)
    {
        if (term.Length == 0 || term.Length > MaxTermLength || meaning.Length == 0 || meaning.Length > MaxMeaningLength) return false;
        if (!original.Contains(term, StringComparison.Ordinal)) return false;
        if (!term.Any(IsJapanese) || term.Any(c => char.IsAsciiLetterOrDigit(c) || IsFullWidthLetterOrDigit(c) || "@[]<>".Contains(c))) return false;
        return !nameParts.Any(p => meaning.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The list as JSON (term, meaning, times seen), ready to paste and share.</summary>
    public string ToJson()
    {
        lock (gate) return JsonSerializer.Serialize(terms, JsonOptions);
    }

    private static bool IsJapanese(char c) => c is (>= '぀' and <= 'ヿ') or (>= '一' and <= '鿿') or (>= 'ｦ' and <= 'ﾟ');

    private static bool IsFullWidthLetterOrDigit(char c) => c is (>= '０' and <= '９') or (>= 'Ａ' and <= 'Ｚ') or (>= 'ａ' and <= 'ｚ');

    private static List<CollectedTerm> Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<CollectedTerm>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Couldn't read the collected words file");
            return [];
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(terms, JsonOptions));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Couldn't save the collected words file");
        }
    }
}
