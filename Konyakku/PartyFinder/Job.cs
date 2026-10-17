using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Gui.PartyFinder.Types;

namespace Konyakku.PartyFinder;

/// <summary>Maps between the game's ClassJob rows and Party Finder's <see cref="JobFlags"/>.</summary>
internal static class Job
{
    public static readonly JobFlags[] All = Enum.GetValues<JobFlags>();
    public static readonly JobFlags AllFlags = All.Aggregate((JobFlags)0, (all, j) => all | j);

    private static Dictionary<uint, JobFlags>? byClassJob;
    private static readonly Dictionary<JobFlags, string> Abbreviations = [];

    public static JobFlags? FromClassJob(uint classJob)
    {
        byClassJob ??= All
            .Select(f => (Flag: f, Row: f.ClassJob(Plugin.DataManager)))
            .Where(x => x.Row != null)
            .ToDictionary(x => x.Row!.Value.RowId, x => x.Flag);
        return byClassJob.TryGetValue(classJob, out var flag) ? flag : null;
    }

    /// <summary>The Party Finder flag of the job you're on, or null when not logged in.</summary>
    public static JobFlags? Current() =>
        Plugin.ObjectTable.LocalPlayer is { } me ? FromClassJob(me.ClassJob.RowId) : null;

    public static string Abbreviation(JobFlags flag)
    {
        if (!Abbreviations.TryGetValue(flag, out var name))
            Abbreviations[flag] = name = flag.ClassJob(Plugin.DataManager)?.Abbreviation.ExtractText() ?? flag.ToString();
        return name;
    }

    /// <summary>Jobs before base classes, so the picker shows PLD, WAR… first.</summary>
    public static IEnumerable<JobFlags> PickerOrder() =>
        All.OrderBy(f => f.ClassJob(Plugin.DataManager)?.JobIndex is > 0 ? 0 : 1);
}
