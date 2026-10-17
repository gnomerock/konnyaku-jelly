using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Konyakku.Windows;

/// <summary>Shows translations, lets you write outgoing messages, and translates pasted Japanese text.</summary>
internal sealed class LogWindow : Window
{
    private const string PasteTarget = "English";
    private const float CollapsedWidth = 44f;
    private const float ExpandedWidth = 150f;

    private enum Page { Chat, Paste }

    private readonly Plugin plugin;
    private string input = string.Empty;
    private bool busy;
    private int lastScrolledVersion = -1;
    private bool themePushed;

    private Page page = Page.Chat;
    private int seenVersion;
    private float sidebarWidth = -1;

    private string pasteInput = string.Empty;
    private string pasteResult = string.Empty;
    private bool pasteBusy;

    public LogWindow(Plugin plugin) : base("Konnyaku Jelly###KonyakkuLog")
    {
        this.plugin = plugin;
        Size = new Vector2(640, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(400, 260), MaximumSize = new Vector2(4000, 4000) };
    }

    public override void PreDraw() => themePushed = Theme.Push(plugin.Config.UseTheme);

    public override void PostDraw() => Theme.Pop(themePushed);

    public override void Draw()
    {
        var config = plugin.Config;
        DrawSidebar(config);
        ImGui.SameLine();

        if (ImGui.BeginChild("##page", Vector2.Zero, false))
        {
            if (page == Page.Chat) DrawChat(config);
            else DrawPaste();
        }
        ImGui.EndChild();
    }

    /// <summary>Navigation on the left. Collapses to an icon strip; the width eases between the two sizes.</summary>
    private void DrawSidebar(Configuration config)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var collapsed = CollapsedWidth * scale;
        var target = (config.SidebarCollapsed ? CollapsedWidth : ExpandedWidth) * scale;
        sidebarWidth = sidebarWidth < 0 ? target : sidebarWidth + (target - sidebarWidth) * Math.Min(1f, ImGui.GetIO().DeltaTime * 14f);
        if (Math.Abs(target - sidebarWidth) < 0.5f) sidebarWidth = target;
        var showLabels = sidebarWidth > collapsed + 60 * scale;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0.18f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 4 * scale));
        if (ImGui.BeginChild("##sidebar", new Vector2(sidebarWidth, 0), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.Dummy(new Vector2(0, 2 * scale));
            var toggleTip = config.SidebarCollapsed ? "Expand sidebar" : "Collapse sidebar";
            if (NavItem("toggle", config.SidebarCollapsed ? FontAwesomeIcon.AngleDoubleRight : FontAwesomeIcon.AngleDoubleLeft,
                    "Jelly", toggleTip, false, showLabels, labelColor: Theme.Yellow))
            {
                config.SidebarCollapsed = !config.SidebarCollapsed;
                config.Save();
            }

            ImGui.Dummy(new Vector2(0, 6 * scale));
            var unread = page != Page.Chat && plugin.LogVersion != seenVersion;
            if (NavItem("chat", FontAwesomeIcon.Comments, "Chat", "Translated chat and message box", page == Page.Chat, showLabels, badge: unread))
                page = Page.Chat;
            if (NavItem("paste", FontAwesomeIcon.Paste, "Paste", "Translate pasted Japanese text", page == Page.Paste, showLabels))
                page = Page.Paste;

            // Pin the actions to the bottom of the sidebar.
            var row = ImGui.GetFrameHeight() * 1.3f;
            var bottom = row * 3 + ImGui.GetStyle().ItemSpacing.Y * 3 + 4 * scale;
            var y = ImGui.GetWindowHeight() - bottom;
            if (y > ImGui.GetCursorPosY()) ImGui.SetCursorPosY(y);

            var enabled = config.Enabled;
            if (NavItem("pause", enabled ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, enabled ? "Translating" : "Paused",
                    enabled ? "Pause chat translation" : "Resume chat translation", false, showLabels,
                    iconColor: enabled ? Theme.Ok : Theme.Warn, labelColor: enabled ? Theme.Ok : Theme.Warn))
            {
                config.Enabled = !enabled;
                config.Save();
            }
            if (NavItem("clear", FontAwesomeIcon.TrashAlt, "Clear log", "Clear the chat log", false, showLabels))
                plugin.ClearLog();
            if (NavItem("settings", FontAwesomeIcon.Cog, "Settings", "Open settings", false, showLabels))
                plugin.OpenConfig();
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    /// <summary>A full-width sidebar row: icon in the collapsed strip, label beside it when expanded.</summary>
    private static bool NavItem(string id, FontAwesomeIcon icon, string label, string tooltip, bool selected, bool showLabel,
        bool badge = false, Vector4? iconColor = null, Vector4? labelColor = null)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var strip = CollapsedWidth * scale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight() * 1.3f);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##nav-{id}", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var inset = new Vector2(4 * scale, 0);

        if (selected)
        {
            draw.AddRectFilled(pos + inset, pos + size - inset, ImGui.GetColorU32(Theme.Fade(Theme.Blue, 0.55f)), 8 * scale);
            draw.AddRectFilled(pos + new Vector2(0, 7 * scale), pos + new Vector2(3 * scale, size.Y - 7 * scale), ImGui.GetColorU32(Theme.Yellow), 2 * scale);
        }
        else if (hovered)
        {
            draw.AddRectFilled(pos + inset, pos + size - inset, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.08f)), 8 * scale);
        }

        var text = selected ? Theme.White : hovered ? Theme.White : Theme.Soft;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            var glyph = icon.ToIconString();
            var iconSize = ImGui.CalcTextSize(glyph);
            var iconPos = pos + new Vector2((strip - iconSize.X) / 2, (size.Y - iconSize.Y) / 2);
            draw.AddText(iconPos, ImGui.GetColorU32(iconColor ?? text), glyph);
            if (badge)
                draw.AddCircleFilled(iconPos + new Vector2(iconSize.X + 1 * scale, 1 * scale), 4 * scale, ImGui.GetColorU32(Theme.Red));
        }

        if (showLabel)
        {
            var labelSize = ImGui.CalcTextSize(label);
            draw.AddText(pos + new Vector2(strip - 2 * scale, (size.Y - labelSize.Y) / 2), ImGui.GetColorU32(labelColor ?? text), label);
        }

        if (hovered) ImGui.SetTooltip(showLabel ? tooltip : $"{label}: {tooltip}");
        return clicked;
    }

    private void DrawChat(Configuration config)
    {
        seenVersion = plugin.LogVersion;
        DrawHeader(config);
        DrawError();

        var footer = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("##log", new Vector2(0, -footer), true))
        {
            var entries = plugin.SnapshotLog();
            if (entries.Count == 0) DrawEmptyLog(config);

            foreach (var e in entries)
            {
                Theme.Badge(e.Channel, Theme.ChannelColor(e.Channel));
                ImGui.SameLine();
                ImGui.TextColored(e.Outgoing ? Theme.Yellow : Theme.Soft, e.Sender);
                ImGui.SameLine();
                ImGui.TextDisabled($"{e.Time:HH:mm}");

                ImGui.Indent(8);
                ImGui.TextWrapped(e.Translation);
                if (config.ShowOriginal)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Soft);
                    ImGui.TextWrapped(e.Original);
                    ImGui.PopStyleColor();
                }
                ImGui.Unindent(8);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) plugin.CopyToClipboard(e.Translation);
                ImGui.Spacing();
            }

            // Follow new lines only when already at the bottom, so reading older lines isn't interrupted.
            if (plugin.LogVersion != lastScrolledVersion && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 40)
                ImGui.SetScrollHereY(1f);
            lastScrolledVersion = plugin.LogVersion;
        }
        ImGui.EndChild();

        DrawComposer(config);
    }

    private static void DrawHeader(Configuration config)
    {
        ImGui.TextColored(Theme.Yellow, "Chat");
        ImGui.SameLine();
        if (config.Enabled)
            ImGui.TextDisabled($"translated into {config.IncomingTarget}");
        else
            ImGui.TextColored(Theme.Warn, "paused · click Paused in the sidebar to resume");
    }

    private void DrawError()
    {
        if (plugin.Translator.LastError is not { } err) return;
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Error);
        ImGui.TextWrapped($"Something went wrong: {err}");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        if (ImGui.SmallButton("OK##dismiss")) plugin.Translator.ClearError();
    }

    private static void DrawEmptyLog(Configuration config)
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.Yellow, "No translations yet.");
        ImGui.TextWrapped(config.Enabled
            ? "Messages from the channels you picked in Settings will show up here, translated."
            : "Chat translation is paused. Click Paused in the sidebar to turn it back on.");
        ImGui.Spacing();
        ImGui.TextDisabled("Tips:");
        ImGui.BulletText("Type below to send a translated message.");
        ImGui.BulletText("Right-click a line to copy it.");
        ImGui.BulletText("Use Paste in the sidebar for longer Japanese text.");
    }

    private void DrawComposer(Configuration config)
    {
        var channel = ChannelNames.OutgoingName(config.SendPrefix);
        var isTell = config.SendPrefix == ChannelNames.TellPrefix;
        var tellTarget = config.TellTarget.Trim();

        DrawChannelPicker(config, channel, isTell);

        var sendWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.PaperPlane, "Send");
        var copyWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Copy, "Copy");
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - sendWidth - copyWidth - spacing * 2);
        var hint = busy ? Theme.Dots("Translating") : $"Message to {(isTell && tellTarget.Length > 0 ? tellTarget : channel)}…";
        var submit = ImGui.InputTextWithHint("##compose", hint, ref input, 400, ImGuiInputTextFlags.EnterReturnsTrue);
        var hasText = input.Trim().Length > 0;
        var canSend = !isTell || tellTarget.Length > 0;
        ImGui.SameLine();
        ImGui.BeginDisabled(busy || !hasText || !canSend);
        submit |= Theme.PrimaryButton(FontAwesomeIcon.PaperPlane, "Send");
        ImGui.EndDisabled();
        Theme.Tooltip(canSend ? $"Translate and send to {channel} (Enter)" : "Enter who to send the tell to first");
        ImGui.SameLine();
        ImGui.BeginDisabled(busy || !hasText);
        var copy = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, "Copy");
        ImGui.EndDisabled();
        Theme.Tooltip("Translate and copy to the clipboard instead of sending");

        if (submit && !copy && !canSend) return;
        if ((submit || copy) && !busy && hasText)
        {
            var text = input.Trim();
            var sendPrefix = copy ? null : isTell ? $"{ChannelNames.TellPrefix} {tellTarget}" : config.SendPrefix;
            busy = true;
            _ = plugin.TranslateOutgoingAsync(text, sendPrefix).ContinueWith(t =>
            {
                if (t.Result != null) input = string.Empty;
                busy = false;
            });
        }
    }

    /// <summary>"To [Party ▾]" row above the message box, plus a Name@World box for tells.</summary>
    private static void DrawChannelPicker(Configuration config, string channel, bool isTell)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("To");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.ChannelColor(channel));
        var open = ImGui.BeginCombo("##channel", channel);
        ImGui.PopStyleColor();
        if (open)
        {
            foreach (var (prefix, name) in ChannelNames.Outgoing)
            {
                var selected = prefix == config.SendPrefix;
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.ChannelColor(name));
                if (ImGui.Selectable($"{name}##{prefix}", selected))
                {
                    config.SendPrefix = prefix;
                    config.Save();
                }
                ImGui.PopStyleColor();
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        Theme.Tooltip("Chat channel the translated message is sent to");

        if (isTell)
        {
            ImGui.SameLine();
            var target = config.TellTarget;
            ImGui.SetNextItemWidth(170);
            if (ImGui.InputTextWithHint("##tell", "Name@World", ref target, 64))
            {
                config.TellTarget = target;
                config.Save();
            }
            Theme.Tooltip("Who to send the tell to, e.g. Jelly Fish@Tonberry");
        }

        // Right-aligned language label, dropped when the window is too narrow to fit it.
        var language = $"in {config.OutgoingTarget}";
        var x = ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(language).X;
        if (ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X + ImGui.GetStyle().ItemSpacing.X > x) return;
        ImGui.SameLine(x);
        ImGui.TextColored(Theme.Yellow, language);
    }

    private void DrawPaste()
    {
        var buttonRow = ImGui.GetFrameHeightWithSpacing();
        var boxHeight = (ImGui.GetContentRegionAvail().Y - buttonRow - ImGui.GetTextLineHeightWithSpacing() * 2) / 2;

        ImGui.TextColored(Theme.Yellow, "Paste · Japanese");
        ImGui.SameLine();
        ImGui.TextDisabled("paste text here (Ctrl+V)");
        ImGui.InputTextMultiline("##pastein", ref pasteInput, 4000, new Vector2(-1, boxHeight));

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Paste, "Paste"))
            pasteInput = ImGui.GetClipboardText() ?? string.Empty;
        Theme.Tooltip("Paste from the clipboard");
        ImGui.SameLine();
        ImGui.BeginDisabled(pasteBusy || pasteInput.Trim().Length == 0);
        if (Theme.PrimaryButton(FontAwesomeIcon.Language, pasteBusy ? Theme.Dots("Translating") : $"Translate to {PasteTarget}"))
            TranslatePaste();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Eraser, "Clear"))
            pasteInput = pasteResult = string.Empty;

        ImGui.TextColored(Theme.Yellow, PasteTarget);
        if (pasteResult.Length > 0)
        {
            ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Copy, "Copy"));
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, "Copy")) plugin.CopyToClipboard(pasteResult);
            Theme.Tooltip("Copy the translation");
        }
        ImGui.InputTextMultiline("##pasteout", ref pasteResult, 8000, new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly);
    }

    private void TranslatePaste()
    {
        var text = pasteInput.Trim();
        pasteBusy = true;
        _ = plugin.Translator.TranslateAsync(text, PasteTarget).ContinueWith(t =>
        {
            pasteResult = t.IsFaulted
                ? $"Translation failed: {t.Exception?.GetBaseException().Message}"
                : t.Result ?? text;
            pasteBusy = false;
        });
    }
}
