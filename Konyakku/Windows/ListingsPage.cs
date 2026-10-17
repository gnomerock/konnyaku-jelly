using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Konyakku.PartyFinder;

namespace Konyakku.Windows;

/// <summary>The Party Finder page of the jelly window: listings that pass the active filter, with translated descriptions.</summary>
internal sealed class ListingsPage(Plugin plugin, Action editFilters)
{
    // Translated descriptions by listing id. An empty string means it was already in the reader's language.
    private readonly ConcurrentDictionary<ulong, string> translations = new();
    private readonly ConcurrentDictionary<ulong, bool> translating = new();

    public void Draw()
    {
        var config = plugin.Config;
        var all = plugin.Listings.Snapshot(TimeSpan.FromMinutes(config.ForgetAfterMinutes));
        var filter = config.CurrentFilter();
        var myJob = Job.Current();
        var shown = all.Where(l => filter.Matches(l, myJob))
            .OrderBy(l => l.DutyName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(l => l.Expires)
            .ToArray();

        DrawHeader(config, shown.Length, all.Length);

        if (ImGui.BeginChild("##listings", Vector2.Zero, true))
        {
            if (all.Length == 0) DrawEmpty();
            else if (shown.Length == 0) DrawNoMatch(filter, all.Length);
            else DrawTable(shown, filter, myJob);
        }
        ImGui.EndChild();
    }

    /// <summary>"Party Finder  [filter ▾]  ✎  12 of 80 shown       🗑".</summary>
    private void DrawHeader(Configuration config, int shown, int total)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Yellow, "Party Finder");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##filter", config.CurrentFilter().Name))
        {
            for (var i = 0; i < config.Filters.Count; i++)
            {
                var selected = i == config.ActiveFilter;
                if (ImGui.Selectable($"{config.Filters[i].Name}##f{i}", selected))
                {
                    config.ActiveFilter = i;
                    config.Save();
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        Theme.Tooltip("Filter to show listings with. Edit filters on the Filters page.");
        ImGui.SameLine();
        if (Theme.IconButton("##edit", FontAwesomeIcon.Pen, "Edit this filter")) editFilters();
        ImGui.SameLine();
        ImGui.TextDisabled($"{shown} of {total} shown");

        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
        if (Theme.IconButton("##clearpf", FontAwesomeIcon.TrashAlt, "Forget every listing collected so far"))
        {
            plugin.Listings.Clear();
            translations.Clear();
        }
    }

    private static void DrawEmpty()
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.Yellow, "No listings yet.");
        ImGui.TextWrapped("Open the Party Finder in game. Konnyaku Jelly sees the listings the game loads, so browse the categories you care about and they'll show up here.");
        ImGui.Spacing();
        ImGui.TextDisabled("Tips:");
        ImGui.BulletText("Listings stay here after you close Party Finder, until they expire.");
        ImGui.BulletText("Hover a listing for its full description. Right-click to translate it or copy the leader's name.");
    }

    private static void DrawNoMatch(ListingFilter filter, int total)
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.Yellow, $"Nothing matches \"{filter.Name}\".");
        ImGui.TextWrapped($"{total} listing{(total == 1 ? " is" : "s are")} hidden by this filter. Pick another filter above or loosen this one on the Filters page.");
    }

    private void DrawTable(Listing[] shown, ListingFilter filter, JobFlags? myJob)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp |
                                      ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("##table", 4, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Duty", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn("Party", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("Min IL", ImGuiTableColumnFlags.WidthFixed, 50f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Ends in", ImGuiTableColumnFlags.WidthFixed, 56f * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        var now = DateTime.Now;
        foreach (var l in shown)
        {
            ImGui.TableNextRow();
            ImGui.PushID(l.Id.ToString());

            ImGui.TableNextColumn();
            // An invisible row-wide selectable carries hover and right-click for the whole row.
            var rowTop = ImGui.GetCursorPosY();
            ImGui.Selectable("##row", false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap,
                new Vector2(0, ImGui.GetTextLineHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y));
            var hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) ImGui.OpenPopup("##menu");
            ImGui.SetCursorPosY(rowTop);

            ImGui.TextColored(Theme.White, l.DutyName);
            DrawTags(l);
            var translated = Translated(l);
            ImGui.PushStyleColor(ImGuiCol.Text, translated != null ? Theme.White : Theme.Soft);
            ImGui.TextUnformatted(translating.ContainsKey(l.Id) ? Theme.Dots("Translating") : OneLine(translated ?? l.Description, 90));
            ImGui.PopStyleColor();

            ImGui.TableNextColumn();
            var open = filter.OpenSlotsFor(l, myJob);
            ImGui.TextColored(open > 0 ? Theme.Ok : Theme.Warn, $"{l.SlotsFilled}/{l.SlotsAvailable}");
            if (filter.Jobs != JobMode.Any)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"{open} for {(filter.Jobs == JobMode.MyJob && myJob is { } j ? Job.Abbreviation(j) : "you")}");
            }
            ImGui.TextDisabled($"{l.Leader} · {l.HomeWorld}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(l.MinItemLevel > 0 ? l.MinItemLevel.ToString() : "-");

            ImGui.TableNextColumn();
            var left = l.Expires - now;
            ImGui.TextColored(left.TotalMinutes < 10 ? Theme.Warn : Theme.White, left.TotalMinutes >= 1 ? $"{(int)left.TotalMinutes}m" : "<1m");

            DrawMenu(l);
            if (hovered && !ImGui.IsPopupOpen("##menu")) DrawDetails(l, translated);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawMenu(Listing l)
    {
        if (!ImGui.BeginPopup("##menu")) return;
        var canTranslate = l.Description.Length > 0 && !translations.ContainsKey(l.Id) && !translating.ContainsKey(l.Id);
        if (ImGui.MenuItem("Translate description", string.Empty, false, canTranslate)) Translate(l);
        if (ImGui.MenuItem("Copy leader's name")) plugin.CopyToClipboard($"{l.Leader}@{l.HomeWorld}");
        if (ImGui.MenuItem("Copy description", string.Empty, false, l.Description.Length > 0))
            plugin.CopyToClipboard(Translated(l) ?? l.Description);
        ImGui.EndPopup();
    }

    /// <summary>The translated description, or null when it hasn't been translated or was already in the reader's language.</summary>
    private string? Translated(Listing l) => translations.TryGetValue(l.Id, out var t) && t.Length > 0 ? t : null;

    private void Translate(Listing l)
    {
        var target = plugin.Config.IncomingTarget;
        if (Language.IsAlreadyIn(l.Description, target))
        {
            translations[l.Id] = string.Empty;
            return;
        }

        translating[l.Id] = true;
        Task.Run(async () =>
        {
            try
            {
                var result = await plugin.Translator.TranslateAsync(l.Description, target);
                translations[l.Id] = result ?? string.Empty;
                if (result != null) plugin.AddLog(new LogEntry(DateTime.Now, "PF", l.Leader, l.Description, result, false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Plugin.Log.Warning(ex, "Party Finder translation failed");
            }
            finally
            {
                translating.TryRemove(l.Id, out _);
            }
        });
    }

    /// <summary>Badges after the duty name: objective, condition, and the listing's flags.</summary>
    private static void DrawTags(Listing l)
    {
        void Tag(string text, Vector4 color)
        {
            if (text.Length == 0) return;
            ImGui.SameLine();
            Theme.Badge(text, color);
        }

        Tag(Names.Objective(l.Objective), Theme.Yellow);
        Tag(Names.Condition(l.Condition), Theme.Soft);
        if (l.OnePlayerPerJob) Tag("1 per job", Theme.Soft);
        if (l.BeginnersWelcome) Tag("Beginners", Theme.Ok);
        if (l.SearchArea.HasFlag(SearchAreaFlags.Private)) Tag("Password", Theme.Warn);
        if (!l.SearchArea.HasFlag(SearchAreaFlags.DataCenter)) Tag("This world", Theme.Soft);
    }

    private static void DrawDetails(Listing l, string? translated)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26);
        ImGui.TextColored(Theme.Yellow, $"{l.DutyName} · {Names.Category(l.Category)}");
        ImGui.TextUnformatted($"{l.Leader} @ {l.HomeWorld}");
        if (translated != null)
        {
            ImGui.Separator();
            ImGui.TextWrapped(translated);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Soft);
            ImGui.TextWrapped(l.Description);
            ImGui.PopStyleColor();
        }
        else if (l.Description.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextWrapped(l.Description);
        }
        ImGui.Separator();
        ImGui.TextDisabled("In party:");
        ImGui.SameLine();
        ImGui.TextUnformatted(l.JobsPresent.Length > 0 ? string.Join(" ", l.JobsPresent) : "-");
        for (var i = 0; i < l.OpenSlots.Length; i++)
        {
            var jobs = Job.All.Where(j => (l.OpenSlots[i] & j) != 0).ToArray();
            var text = jobs.Length >= Job.All.Length - 1 ? "any job" : string.Join(" ", jobs.Select(Job.Abbreviation));
            ImGui.TextDisabled($"Open slot {i + 1}:");
            ImGui.SameLine();
            ImGui.TextUnformatted(text);
        }
        ImGui.TextDisabled("Right-click to translate or copy the leader's name.");
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static string OneLine(string text, int max)
    {
        var line = text.ReplaceLineEndings(" ");
        return line.Length <= max ? line : line[..max].TrimEnd() + "…";
    }
}
