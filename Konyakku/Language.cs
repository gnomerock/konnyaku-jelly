using System.Linq;

namespace Konyakku;

/// <summary>Languages Konyakku translates between.</summary>
internal static class Language
{
    /// <summary>Languages your own messages can be translated into.</summary>
    public static readonly string[] All = ["English", "Japanese"];

    /// <summary>Languages other players' chat can be translated into. Thai is only for reading: the game can't show it in chat.</summary>
    public static readonly string[] Incoming = [.. All, "Thai"];

    /// <summary>Cheap check that skips the API call when a message is obviously in the target language already.</summary>
    public static bool IsAlreadyIn(string text, string target) => target switch
    {
        "Japanese" => text.Any(IsKana),
        "English" => text.All(c => c < 0x80),
        "Thai" => text.Any(IsThai),
        _ => false,
    };

    private static bool IsKana(char c) => c is >= '\u3040' and <= '\u30FF';

    private static bool IsThai(char c) => c is >= '\u0E00' and <= '\u0E7F';
}
