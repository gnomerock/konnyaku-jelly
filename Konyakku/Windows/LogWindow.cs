using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;

namespace Konyakku.Windows;

/// <summary>Shows translations, lets you write outgoing messages, and translates pasted Japanese text.</summary>
internal sealed class LogWindow : Window
{
    private const string PasteTarget = "English";

    private readonly Plugin plugin;
    private string input = string.Empty;
    private bool busy;
    private int lastCount;
    private bool themePushed;

    private string pasteInput = string.Empty;
    private string pasteResult = string.Empty;
    private bool pasteBusy;

    public LogWindow(Plugin plugin) : base("Konnyaku Jelly###KonyakkuLog")
    {
        this.plugin = plugin;
        Size = new Vector2(520, 380);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 240), MaximumSize = new Vector2(4000, 4000) };
    }

    public override void PreDraw() => themePushed = Theme.Push(plugin.Config.UseTheme);

    public override void PostDraw() => Theme.Pop(themePushed);

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##tabs")) return;
        if (ImGui.BeginTabItem("  Chat  "))
        {
            DrawChat(plugin.Config);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("  Paste  "))
        {
            DrawPaste();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawChat(Configuration config)
    {
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

            if (entries.Count != lastCount && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 40)
                ImGui.SetScrollHereY(1f);
            lastCount = entries.Count;
        }
        ImGui.EndChild();

        DrawComposer(config);
    }

    private void DrawHeader(Configuration config)
    {
        var enabled = config.Enabled;
        if (ImGui.Checkbox("##enabled", ref enabled))
        {
            config.Enabled = enabled;
            config.Save();
        }
        Theme.Tooltip(enabled ? "Click to pause chat translation" : "Click to resume chat translation");
        ImGui.SameLine();
        if (enabled)
            ImGui.TextColored(Theme.Ok, $"Translating chat → {config.IncomingTarget}");
        else
            ImGui.TextColored(Theme.Warn, "Paused");

        var buttons = ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine(ImGui.GetContentRegionMax().X - buttons);
        if (Theme.IconButton("clear", FontAwesomeIcon.TrashAlt, "Clear the log")) plugin.ClearLog();
        ImGui.SameLine();
        if (Theme.IconButton("settings", FontAwesomeIcon.Cog, "Settings")) plugin.OpenConfig();
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
            : "Chat translation is paused. Tick the box above to turn it back on.");
        ImGui.Spacing();
        ImGui.TextDisabled("Tips:");
        ImGui.BulletText("Type below to send a translated message.");
        ImGui.BulletText("Right-click a line to copy it.");
        ImGui.BulletText("Use the Paste tab for longer Japanese text.");
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

        ImGui.TextColored(Theme.Yellow, "Japanese");
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
