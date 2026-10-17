using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Gui.PartyFinder.Types;
using Lumina.Excel.Sheets;

namespace Konyakku.PartyFinder;

/// <summary>Display names for Party Finder enums, and the duty list the filter editor searches.</summary>
internal static class Names
{
    /// <summary>Categories as the Party Finder window lists them. <see cref="DutyCategory.None"/> is left out.</summary>
    public static readonly DutyCategory[] Categories =
    [
        DutyCategory.Roulette, DutyCategory.Dungeons, DutyCategory.GuildQuests, DutyCategory.Trials,
        DutyCategory.Raids, DutyCategory.HighEndDuty, DutyCategory.PvP, DutyCategory.GoldSaucer,
        DutyCategory.FATEs, DutyCategory.TreasureHunts, DutyCategory.TheHunt, DutyCategory.GatheringForays,
        DutyCategory.DeepDungeons, DutyCategory.FieldOperations, DutyCategory.VCDungeonFinder,
    ];

    public static readonly (ObjectiveFlags Flag, string Name)[] Objectives =
    [
        (ObjectiveFlags.Practice, "Practice"),
        (ObjectiveFlags.DutyCompletion, "Duty completion"),
        (ObjectiveFlags.Loot, "Loot"),
        (ObjectiveFlags.None, "No objective"),
    ];

    public static readonly (ConditionFlags Flag, string Name)[] Conditions =
    [
        (ConditionFlags.DutyComplete, "Duty complete"),
        (ConditionFlags.DutyIncomplete, "Duty incomplete"),
        (ConditionFlags.DutyCompleteWeeklyUnclaimed, "Weekly reward unclaimed"),
        (ConditionFlags.None, "No condition"),
    ];

    public static string Category(DutyCategory c) => c switch
    {
        DutyCategory.Roulette => "Duty Roulette",
        DutyCategory.GuildQuests => "Guildhests",
        DutyCategory.HighEndDuty => "High-end Duty",
        DutyCategory.PvP => "PvP",
        DutyCategory.GoldSaucer => "Gold Saucer",
        DutyCategory.FATEs => "FATEs",
        DutyCategory.TreasureHunts => "Treasure Hunt",
        DutyCategory.TheHunt => "The Hunt",
        DutyCategory.GatheringForays => "Gathering Forays",
        DutyCategory.DeepDungeons => "Deep Dungeons",
        DutyCategory.FieldOperations => "Field Operations",
        DutyCategory.VCDungeonFinder => "V&C Dungeon Finder",
        DutyCategory.None => "Other",
        _ => c.ToString(),
    };

    public static string Objective(ObjectiveFlags o) =>
        Objectives.FirstOrDefault(x => x.Flag != ObjectiveFlags.None && o.HasFlag(x.Flag)).Name ?? string.Empty;

    public static string Condition(ConditionFlags c) =>
        Conditions.FirstOrDefault(x => x.Flag != ConditionFlags.None && c.HasFlag(x.Flag)).Name ?? string.Empty;

    /// <summary>Duty names in the game data are often lower-case ("the Aetherfont").</summary>
    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static List<(uint Id, string Name)>? duties;

    /// <summary>Every named duty that can be listed in Party Finder, sorted by name.</summary>
    public static List<(uint Id, string Name)> Duties() => duties ??=
    [
        .. Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()
            .Select(d => (d.RowId, Name: Capitalize(d.Name.ExtractText())))
            .Where(d => d.Name.Length > 0)
            .DistinctBy(d => d.RowId)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
    ];

    public static string Duty(uint id) =>
        Duties().FirstOrDefault(d => d.Id == id).Name ?? $"Duty #{id}";
}
