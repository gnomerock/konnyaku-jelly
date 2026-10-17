using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Konyakku.PartyFinder;

namespace Konyakku.Windows;

/// <summary>The Filters page of the jelly window: pick a saved filter, then edit what it lets through.</summary>
internal sealed class FilterPage(Plugin plugin)
{
    private static readonly string[] TriNames = ["Any", "Yes", "No"];

    private int editing = -1;
    private string dutySearch = string.Empty;

    public void Draw()
    {
        var config = plugin.Config;
        if (editing < 0 || editing >= config.Filters.Count) editing = config.ActiveFilter;
        var filter = config.Filters[editing];

        var changed = DrawPicker(config, ref filter);

        if (ImGui.BeginChild("##filter", Vector2.Zero, true))
        {
            changed |= DrawDuties(filter);
            changed |= DrawJobs(filter);
            changed |= DrawConditions(filter);
        }
        ImGui.EndChild();

        if (changed) config.Save();
    }

    /// <summary>Filter dropdown, name box and New / Copy / Delete / Use buttons.</summary>
    private bool DrawPicker(Configuration config, ref ListingFilter filter)
    {
        var changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Yellow, "Filters");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("##pick", filter.Name))
        {
            for (var i = 0; i < config.Filters.Count; i++)
            {
                var label = i == config.ActiveFilter ? $"{config.Filters[i].Name}  (in use)" : config.Filters[i].Name;
                if (ImGui.Selectable($"{label}##f{i}", i == editing)) editing = i;
            }
            ImGui.EndCombo();
        }
        Theme.Tooltip("Filter to edit");

        ImGui.SameLine();
        if (Theme.IconButton("##new", FontAwesomeIcon.Plus, "New filter"))
        {
            config.Filters.Add(new ListingFilter());
            editing = config.Filters.Count - 1;
            changed = true;
        }
        ImGui.SameLine();
        if (Theme.IconButton("##copy", FontAwesomeIcon.Copy, "Copy this filter"))
        {
            config.Filters.Add(filter.Clone());
            editing = config.Filters.Count - 1;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(config.Filters.Count <= 1);
        if (Theme.IconButton("##delete", FontAwesomeIcon.TrashAlt, "Delete this filter (hold Ctrl)") && ImGui.GetIO().KeyCtrl)
        {
            config.Filters.RemoveAt(editing);
            if (config.ActiveFilter >= editing && config.ActiveFilter > 0) config.ActiveFilter--;
            editing = Math.Min(editing, config.Filters.Count - 1);
            changed = true;
        }
        ImGui.EndDisabled();

        filter = config.Filters[editing];
        ImGui.SameLine();
        var inUse = editing == config.ActiveFilter;
        ImGui.BeginDisabled(inUse);
        if (Theme.PrimaryButton(FontAwesomeIcon.Check, inUse ? "In use" : "Use"))
        {
            config.ActiveFilter = editing;
            changed = true;
        }
        ImGui.EndDisabled();
        Theme.Tooltip(inUse ? "The Party Finder page uses this filter" : "Show listings with this filter");

        var name = filter.Name;
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Name");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputText("##name", ref name, 40) && name.Trim().Length > 0)
        {
            filter.Name = name;
            changed = true;
        }
        return changed;
    }

    private bool DrawDuties(ListingFilter filter)
    {
        if (!ImGui.CollapsingHeader("Duty", ImGuiTreeNodeFlags.DefaultOpen)) return false;
        var changed = false;

        ImGui.TextDisabled("Categories (none ticked = all)");
        if (ImGui.BeginTable("##cats", 3))
        {
            foreach (var cat in Names.Categories)
            {
                ImGui.TableNextColumn();
                changed |= Toggle(Names.Category(cat), filter.Categories, cat);
            }
            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Duties (none picked = all)");
        // Picked duties as removable buttons, wrapping to a new line when the next one doesn't fit.
        var right = ImGui.GetWindowPos().X + ImGui.GetContentRegionMax().X;
        var first = true;
        foreach (var id in filter.Duties.ToArray())
        {
            var label = Names.Duty(id);
            var width = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Times, label);
            if (!first && ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width < right) ImGui.SameLine();
            first = false;
            ImGui.PushID($"duty{id}");
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, label))
            {
                filter.Duties.Remove(id);
                changed = true;
            }
            ImGui.PopID();
            Theme.Tooltip("Remove");
        }

        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##dutysearch", "Search duties to add, e.g. savage", ref dutySearch, 60);
        var query = dutySearch.Trim();
        if (query.Length >= 2)
        {
            var matches = Names.Duties().Where(d => d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
            var height = Math.Min(matches.Count, 8) * ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y * 2;
            if (matches.Count == 0) ImGui.TextDisabled("No duty with that name.");
            else if (ImGui.BeginChild("##dutyresults", new Vector2(-1, height), true))
            {
                foreach (var (id, dutyName) in matches)
                {
                    var picked = filter.Duties.Contains(id);
                    if (ImGui.Checkbox($"{dutyName}##r{id}", ref picked))
                    {
                        if (picked) filter.Duties.Add(id);
                        else filter.Duties.Remove(id);
                        changed = true;
                    }
                }
            }
            if (matches.Count > 0) ImGui.EndChild();
        }
        if (filter.Duties.Count > 0)
            ImGui.TextDisabled("Picking duties hides roulettes and listings without a duty.");
        return changed;
    }

    private static bool DrawJobs(ListingFilter filter)
    {
        if (!ImGui.CollapsingHeader("Open slots", ImGuiTreeNodeFlags.DefaultOpen)) return false;
        var changed = false;

        ImGui.TextDisabled("Only show parties with a slot for");
        foreach (var (mode, label) in new[] { (JobMode.Any, "Anyone"), (JobMode.MyJob, "My current job"), (JobMode.Selected, "These jobs") })
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(label, filter.Jobs == mode))
            {
                filter.Jobs = mode;
                changed = true;
            }
        }

        if (filter.Jobs == JobMode.Selected && ImGui.BeginTable("##jobs", 8))
        {
            foreach (var job in Job.PickerOrder())
            {
                ImGui.TableNextColumn();
                changed |= Toggle(Job.Abbreviation(job), filter.SelectedJobs, job);
            }
            ImGui.EndTable();
        }

        var min = filter.MinOpenSlots;
        ImGui.SetNextItemWidth(160);
        if (ImGui.SliderInt("At least this many open slots", ref min, 0, 7))
        {
            filter.MinOpenSlots = min;
            changed = true;
        }
        Theme.Tooltip("Counts only slots the jobs above can take. One-player-per-job parties don't count jobs they already have.");
        return changed;
    }

    private static bool DrawConditions(ListingFilter filter)
    {
        if (!ImGui.CollapsingHeader("Listing conditions", ImGuiTreeNodeFlags.DefaultOpen)) return false;
        var changed = false;

        var ilvl = filter.MaxItemLevel;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputInt("My item level", ref ilvl, 5, 10))
        {
            filter.MaxItemLevel = Math.Clamp(ilvl, 0, 9999);
            changed = true;
        }
        Theme.Tooltip("Hide listings that need a higher item level than this. 0 = don't check.");

        ImGui.TextDisabled("Objective (none ticked = any)");
        foreach (var (flag, name) in Names.Objectives)
        {
            ImGui.SameLine();
            changed |= Toggle(name, filter.Objectives, flag);
        }
        ImGui.TextDisabled("Completion (none ticked = any)");
        foreach (var (flag, name) in Names.Conditions)
        {
            ImGui.SameLine();
            changed |= Toggle(name, filter.Conditions, flag);
        }

        changed |= Tri("Data center wide", filter.DataCenter, v => filter.DataCenter = v,
            "Yes: only listings open to the whole data center. No: only your world's listings.");
        changed |= Tri("One player per job", filter.OnePlayerPerJob, v => filter.OnePlayerPerJob = v, null);
        changed |= Tri("Beginners welcome", filter.BeginnersWelcome, v => filter.BeginnersWelcome = v, null);

        var hidePrivate = filter.HidePrivate;
        if (ImGui.Checkbox("Hide password-protected listings", ref hidePrivate))
        {
            filter.HidePrivate = hidePrivate;
            changed = true;
        }
        return changed;
    }

    private static bool Toggle<T>(string label, HashSet<T> set, T value)
    {
        var on = set.Contains(value);
        if (!ImGui.Checkbox($"{label}##{value}", ref on)) return false;
        if (on) set.Add(value);
        else set.Remove(value);
        return true;
    }

    /// <summary>"Label  (•) Any  ( ) Yes  ( ) No".</summary>
    private static bool Tri(string label, TriState value, Action<TriState> set, string? tooltip)
    {
        ImGui.TextDisabled(label);
        if (tooltip != null) Theme.Tooltip(tooltip);
        var changed = false;
        for (var i = 0; i < TriNames.Length; i++)
        {
            ImGui.SameLine(i == 0 ? 170 * ImGuiHelpers.GlobalScale : 0);
            if (ImGui.RadioButton($"{TriNames[i]}##{label}", value == (TriState)i))
            {
                set((TriState)i);
                changed = true;
            }
        }
        return changed;
    }
}
