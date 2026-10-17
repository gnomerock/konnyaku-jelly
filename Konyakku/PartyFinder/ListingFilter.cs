using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Gui.PartyFinder.Types;

namespace Konyakku.PartyFinder;

public enum TriState { Any, Yes, No }

public enum JobMode
{
    /// <summary>Any open slot counts.</summary>
    Any,
    /// <summary>Only slots the job you're on right now can take.</summary>
    MyJob,
    /// <summary>Only slots one of <see cref="ListingFilter.SelectedJobs"/> can take.</summary>
    Selected,
}

/// <summary>A saved filter. Empty sets and <see cref="TriState.Any"/> mean "don't filter on this".</summary>
[Serializable]
public sealed class ListingFilter
{
    public string Name { get; set; } = "New filter";

    public HashSet<DutyCategory> Categories { get; set; } = [];
    /// <summary>ContentFinderCondition rows. Only normal duties have one, so roulettes and "other" listings never match.</summary>
    public HashSet<uint> Duties { get; set; } = [];

    public JobMode Jobs { get; set; } = JobMode.Any;
    public HashSet<JobFlags> SelectedJobs { get; set; } = [];
    public int MinOpenSlots { get; set; } = 0;

    /// <summary>Hide listings that ask for a higher minimum item level than this. 0 = any.</summary>
    public int MaxItemLevel { get; set; } = 0;
    public HashSet<ObjectiveFlags> Objectives { get; set; } = [];
    public HashSet<ConditionFlags> Conditions { get; set; } = [];
    public TriState DataCenter { get; set; } = TriState.Any;
    public TriState OnePlayerPerJob { get; set; } = TriState.Any;
    public TriState BeginnersWelcome { get; set; } = TriState.Any;
    public bool HidePrivate { get; set; } = false;

    public bool Matches(Listing l, JobFlags? myJob)
    {
        if (Categories.Count > 0 && !Categories.Contains(l.Category)) return false;
        if (Duties.Count > 0 && (l.DutyType != DutyType.Normal || !Duties.Contains(l.DutyId))) return false;
        if (MaxItemLevel > 0 && l.MinItemLevel > MaxItemLevel) return false;
        if (Objectives.Count > 0 && !Objectives.Any(o => l.Objective.HasFlag(o))) return false;
        if (Conditions.Count > 0 && !Conditions.Any(c => l.Condition.HasFlag(c))) return false;
        if (HidePrivate && l.SearchArea.HasFlag(SearchAreaFlags.Private)) return false;
        if (!Check(DataCenter, l.SearchArea.HasFlag(SearchAreaFlags.DataCenter))) return false;
        if (!Check(OnePlayerPerJob, l.OnePlayerPerJob)) return false;
        if (!Check(BeginnersWelcome, l.BeginnersWelcome)) return false;
        return OpenSlotsFor(l, myJob) >= Math.Max(Jobs == JobMode.Any ? 0 : 1, MinOpenSlots);
    }

    /// <summary>Open slots the wanted jobs could take, leaving out jobs a one-player-per-job party already has.</summary>
    public int OpenSlotsFor(Listing l, JobFlags? myJob)
    {
        var wanted = Jobs switch
        {
            // Not logged in (or a job Party Finder doesn't know): don't filter rather than hide everything.
            JobMode.MyJob => myJob ?? Job.AllFlags,
            JobMode.Selected => SelectedJobs.Aggregate((JobFlags)0, (all, j) => all | j),
            _ => Job.AllFlags,
        };
        if (l.OnePlayerPerJob) wanted &= ~l.PresentFlags;
        return l.OpenSlots.Count(s => (s & wanted) != 0);
    }

    public ListingFilter Clone() => new()
    {
        Name = $"{Name} copy",
        Categories = [.. Categories],
        Duties = [.. Duties],
        Jobs = Jobs,
        SelectedJobs = [.. SelectedJobs],
        MinOpenSlots = MinOpenSlots,
        MaxItemLevel = MaxItemLevel,
        Objectives = [.. Objectives],
        Conditions = [.. Conditions],
        DataCenter = DataCenter,
        OnePlayerPerJob = OnePlayerPerJob,
        BeginnersWelcome = BeginnersWelcome,
        HidePrivate = HidePrivate,
    };

    private static bool Check(TriState want, bool value) => want == TriState.Any || value == (want == TriState.Yes);
}
