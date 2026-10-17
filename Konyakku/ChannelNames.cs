using System.Collections.Generic;
using Dalamud.Game.Text;

namespace Konyakku;

internal static class ChannelNames
{
    public static readonly (XivChatType Type, string Name)[] Selectable =
    [
        (XivChatType.Say, "Say"),
        (XivChatType.Shout, "Shout"),
        (XivChatType.Yell, "Yell"),
        (XivChatType.Party, "Party"),
        (XivChatType.CrossParty, "Cross-world Party"),
        (XivChatType.Alliance, "Alliance"),
        (XivChatType.FreeCompany, "Free Company"),
        (XivChatType.TellIncoming, "Tell"),
        (XivChatType.NoviceNetwork, "Novice Network"),
        (XivChatType.PvPTeam, "PvP Team"),
        (XivChatType.Ls1, "LS1"), (XivChatType.Ls2, "LS2"), (XivChatType.Ls3, "LS3"), (XivChatType.Ls4, "LS4"),
        (XivChatType.Ls5, "LS5"), (XivChatType.Ls6, "LS6"), (XivChatType.Ls7, "LS7"), (XivChatType.Ls8, "LS8"),
        (XivChatType.CrossLinkShell1, "CWLS1"), (XivChatType.CrossLinkShell2, "CWLS2"),
        (XivChatType.CrossLinkShell3, "CWLS3"), (XivChatType.CrossLinkShell4, "CWLS4"),
        (XivChatType.CrossLinkShell5, "CWLS5"), (XivChatType.CrossLinkShell6, "CWLS6"),
        (XivChatType.CrossLinkShell7, "CWLS7"), (XivChatType.CrossLinkShell8, "CWLS8"),
    ];

    /// <summary>Channels you can send a translated message to, in the order shown in the dropdown.</summary>
    public static readonly (string Prefix, string Name)[] Outgoing =
    [
        ("/p", "Party"), ("/s", "Say"), ("/a", "Alliance"), ("/fc", "Free Company"), ("/t", "Tell"),
        ("/beginner", "Novice Network"), ("/sh", "Shout"), ("/y", "Yell"),
        ("/l1", "LS1"), ("/l2", "LS2"), ("/l3", "LS3"), ("/l4", "LS4"),
        ("/l5", "LS5"), ("/l6", "LS6"), ("/l7", "LS7"), ("/l8", "LS8"),
        ("/cwl1", "CWLS1"), ("/cwl2", "CWLS2"), ("/cwl3", "CWLS3"), ("/cwl4", "CWLS4"),
        ("/cwl5", "CWLS5"), ("/cwl6", "CWLS6"), ("/cwl7", "CWLS7"), ("/cwl8", "CWLS8"),
    ];

    public const string TellPrefix = "/t";

    public static string OutgoingName(string prefix)
    {
        foreach (var (p, n) in Outgoing)
            if (p == prefix) return n;
        return prefix;
    }

    private static readonly Dictionary<XivChatType, string> Lookup = BuildLookup();

    public static string Short(XivChatType type) => Lookup.TryGetValue(type, out var n) ? n : type.ToString();

    private static Dictionary<XivChatType, string> BuildLookup()
    {
        var d = new Dictionary<XivChatType, string>();
        foreach (var (t, n) in Selectable) d[t] = n;
        return d;
    }
}
