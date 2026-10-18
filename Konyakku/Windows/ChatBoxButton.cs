using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Konyakku.Windows;

/// <summary>
/// A small button drawn just right of the game's chat input. It translates what the player typed there into their
/// outgoing language and writes the result back into the input, so they can check it and press Enter themselves.
/// A leading channel command (/p, /fc, /t First Last@World) is kept as typed.
/// </summary>
internal sealed class ChatBoxButton(Plugin plugin)
{
    private const ImGuiWindowFlags Flags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBackground;

    private volatile bool busy;
    private volatile string? note;
    private DateTime noteUntil;

    public unsafe void Draw()
    {
        var config = plugin.Config;
        if (!config.ShowChatBoxButton) return;
        var input = FindInput(out var addonScale);
        if (input == null) return;

        var node = (AtkResNode*)input->OwnerNode;
        if (node == null || !node->IsVisible()) return;
        var viewport = ImGui.GetMainViewport();
        var height = node->Height * addonScale;
        var size = new Vector2(height, height);
        var pos = viewport.Pos + new Vector2(node->ScreenX + node->Width * addonScale + 4 * ImGuiHelpers.GlobalScale, node->ScreenY);

        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
        if (ImGui.Begin("##KonyakkuChatBoxButton", Flags))
        {
            var clicked = ImGui.InvisibleButton("##translate", size) && !busy;
            var hovered = ImGui.IsItemHovered();
            var draw = ImGui.GetWindowDrawList();
            var color = busy ? Theme.BlueHover with { W = 0.6f } : hovered ? Theme.BlueHover : Theme.Blue;
            draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(color), 6 * ImGuiHelpers.GlobalScale);
            using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            {
                var glyph = (busy ? FontAwesomeIcon.Hourglass : FontAwesomeIcon.Language).ToIconString();
                var iconSize = ImGui.CalcTextSize(glyph);
                draw.AddText(pos + (size - iconSize) / 2, ImGui.GetColorU32(Theme.Yellow), glyph);
            }

            var showNote = note != null && DateTime.Now < noteUntil;
            if (hovered || showNote)
                ImGui.SetTooltip(showNote ? note! : busy ? "Translating…" : $"Translate what you typed into {config.OutgoingTarget}.\nThe translation replaces your text; press Enter to send it.");
            if (clicked) Start(input);
        }
        ImGui.End();
        ImGui.PopStyleVar(3);
    }

    private unsafe void Start(AtkComponentTextInput* input)
    {
        var typed = Read(input);
        var (prefix, body) = SplitCommand(typed);
        if (body.Length == 0)
        {
            Note("Type something in the chat box first, then click here to translate it.");
            return;
        }
        busy = true;
        _ = TranslateAsync(typed, prefix, body);
    }

    private async Task TranslateAsync(string typed, string prefix, string body)
    {
        try
        {
            var target = plugin.Config.OutgoingTarget;
            var result = await plugin.Translator.TranslateAsync(body, target);
            if (result == null)
            {
                Note($"That's already in {target}.");
                return;
            }
            plugin.AddLog(new LogEntry(DateTime.Now, "chat box", "me", body, result, true));
            var replaced = await Plugin.Framework.RunOnFrameworkThread(() => Replace(typed, prefix + result));
            // The player kept typing (or sent it) while we waited: don't overwrite their text.
            if (!replaced)
            {
                plugin.CopyToClipboard(result);
                Note("The chat box changed while translating, so the translation was copied instead. Paste it with Ctrl+V.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Plugin.Log.Warning(ex, "Chat box translation failed");
            Note($"Translation failed: {ex.Message}");
        }
        finally
        {
            busy = false;
        }
    }

    private static unsafe bool Replace(string expected, string text)
    {
        var input = FindInput(out _);
        if (input == null || Read(input) != expected) return false;
        input->SetText(text);
        return true;
    }

    private void Note(string text)
    {
        note = text;
        noteUntil = DateTime.Now.AddSeconds(4);
    }

    /// <summary>The chat input's text. Auto-translate phrases become their plain text.</summary>
    private static unsafe string Read(AtkComponentTextInput* input) => SeString.Parse(input->RawString.AsSpan()).TextValue.Trim();

    /// <summary>Splits a leading channel command off the message: "/p hi" → ("/p ", "hi"); tells keep the name too.</summary>
    internal static (string Prefix, string Body) SplitCommand(string text)
    {
        if (!text.StartsWith('/')) return ("", text);
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        // "/t First Last@World message": the command plus the two halves of the name.
        var take = command is "/t" or "/tell" ? 3 : 1;
        if (parts.Length <= take) return (text + " ", "");
        return (string.Join(' ', parts[..take]) + " ", string.Join(' ', parts[take..]));
    }

    /// <summary>The ChatLog addon's text input, found by component type because the addon has no named field for it.</summary>
    private static unsafe AtkComponentTextInput* FindInput(out float scale)
    {
        scale = 1;
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("ChatLog").Address;
        if (addon == null || !addon->IsVisible) return null;
        scale = addon->Scale;
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var n = addon->UldManager.NodeList[i];
            if (n == null || (ushort)n->Type < 1000) continue;
            var component = ((AtkComponentNode*)n)->Component;
            if (component == null) continue;
            var info = (AtkUldComponentInfo*)component->UldManager.Objects;
            if (info != null && info->ComponentType == ComponentType.TextInput) return (AtkComponentTextInput*)component;
        }
        return null;
    }
}
