using System.Linq;

namespace Konyakku;

/// <summary>Languages Konyakku translates between. Only English and Japanese for now.</summary>
internal static class Language
{
    public static readonly string[] All = ["English", "Japanese"];

    /// <summary>Cheap check that skips the API call when a message is obviously in the target language already.</summary>
    public static bool IsAlreadyIn(string text, string target) => target switch
    {
        "Japanese" => text.Any(IsKana),
        "English" => text.All(c => c < 0x80),
        _ => false,
    };

    private static bool IsKana(char c) => c is >= '\u3040' and <= '\u30FF';
}
