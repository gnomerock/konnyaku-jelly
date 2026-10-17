using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace Konyakku.PartyFinder;

/// <summary>A copy of a Party Finder listing, so nothing holds on to the game's packet data.</summary>
public sealed record Listing(
    ulong Id,
    string Leader,
    string HomeWorld,
    string Description,
    DutyCategory Category,
    DutyType DutyType,
    uint DutyId,
    string DutyName,
    ushort MinItemLevel,
    ObjectiveFlags Objective,
    ConditionFlags Condition,
    SearchAreaFlags SearchArea,
    bool BeginnersWelcome,
    byte SlotsFilled,
    byte SlotsAvailable,
    JobFlags[] OpenSlots,
    string[] JobsPresent,
    JobFlags PresentFlags,
    DateTime Expires,
    DateTime LastSeen)
{
    public bool OnePlayerPerJob => SearchArea.HasFlag(SearchAreaFlags.OnePlayerPerJob);
}

/// <summary>
/// Keeps every listing the game sends while Party Finder is open. The game only sends the listings of the
/// category being browsed, so the store collects them across refreshes and forgets them after a while.
/// </summary>
internal sealed class ListingStore : IDisposable
{
    private readonly IPartyFinderGui partyFinderGui;
    private readonly IDataManager data;
    private readonly object gate = new();
    private readonly Dictionary<ulong, Listing> listings = [];

    public ListingStore(IPartyFinderGui partyFinderGui, IDataManager data)
    {
        this.partyFinderGui = partyFinderGui;
        this.data = data;
        partyFinderGui.ReceiveListing += OnReceiveListing;
    }

    public void Dispose() => partyFinderGui.ReceiveListing -= OnReceiveListing;

    /// <summary>Current listings, after dropping expired ones and ones not seen for <paramref name="forgetAfter"/>.</summary>
    public Listing[] Snapshot(TimeSpan forgetAfter)
    {
        var now = DateTime.Now;
        lock (gate)
        {
            foreach (var id in listings.Where(p => p.Value.Expires < now || now - p.Value.LastSeen > forgetAfter).Select(p => p.Key).ToList())
                listings.Remove(id);
            return [.. listings.Values];
        }
    }

    public void Clear()
    {
        lock (gate) listings.Clear();
    }

    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        var entry = Copy(listing);
        lock (gate) listings[entry.Id] = entry;
    }

    private Listing Copy(IPartyFinderListing l)
    {
        var slots = l.Slots.ToArray();
        var present = l.RawJobsPresent.ToArray();
        var open = new List<JobFlags>();
        JobFlags presentFlags = 0;
        for (var i = 0; i < Math.Min(slots.Length, l.SlotsAvailable); i++)
        {
            var job = i < present.Length ? present[i] : (byte)0;
            if (job == 0) open.Add(slots[i].Accepting.Aggregate((JobFlags)0, (all, j) => all | j));
            else presentFlags |= Job.FromClassJob(job) ?? 0;
        }

        var now = DateTime.Now;
        return new Listing(
            l.Id,
            l.Name.TextValue,
            l.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty,
            l.Description.TextValue.Trim(),
            l.Category,
            l.DutyType,
            l.DutyType == DutyType.Normal ? l.Duty.RowId : 0,
            DutyName(l),
            l.MinimumItemLevel,
            l.Objective,
            l.Conditions,
            l.SearchArea,
            l.BeginnersWelcome,
            l.SlotsFilled,
            l.SlotsAvailable,
            [.. open],
            [.. l.JobsPresent.Where(j => j.RowId != 0).Select(j => j.ValueNullable?.Abbreviation.ExtractText() ?? "?")],
            presentFlags,
            now.AddSeconds(l.SecondsRemaining),
            now);
    }

    private string DutyName(IPartyFinderListing l)
    {
        var name = l.DutyType switch
        {
            DutyType.Normal => l.Duty.ValueNullable?.Name.ExtractText(),
            DutyType.Roulette => data.GetExcelSheet<ContentRoulette>().GetRowOrDefault(l.RawDuty)?.Name.ExtractText(),
            _ => null,
        };
        return string.IsNullOrEmpty(name) ? Names.Category(l.Category) : Names.Capitalize(name);
    }
}
