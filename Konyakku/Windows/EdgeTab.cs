using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace Konyakku.Windows;

/// <summary>
/// A vertical "Jelly" tab on the left edge of the screen. Clicking it opens the jelly window as a drawer
/// from the same edge (see <see cref="LogWindow"/>); while the docked window is open it covers the tab.
/// </summary>
internal sealed class EdgeTab(Plugin plugin)
{
    private const ImGuiWindowFlags Flags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoBringToFrontOnFocus;

    private const string Label = "Jelly";

    // 0 = resting, 1 = hovered; widens the tab a little under the mouse.
    private float hover;

    /// <summary>Screen Y where the tab starts. The docked window opens at the same height.</summary>
    public static float Top(ImGuiViewportPtr viewport) => viewport.Pos.Y + viewport.Size.Y * 0.18f;

    public void Draw()
    {
        var config = plugin.Config;
        if (!config.ShowEdgeTab) return;
        var open = plugin.IsMainWindowOpen;
        if (open && config.DockToEdge) return;

        var scale = ImGuiHelpers.GlobalScale;
        var viewport = ImGui.GetMainViewport();
        var size = new Vector2((26 + 6 * hover) * scale, 112 * scale);

        ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X, Top(viewport)));
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
        if (ImGui.Begin("##KonyakkuEdgeTab", Flags))
        {
            var pos = ImGui.GetWindowPos();
            var clicked = ImGui.InvisibleButton("##edge", size);
            var hovered = ImGui.IsItemHovered();
            hover += ((hovered ? 1f : 0f) - hover) * Math.Min(1f, ImGui.GetIO().DeltaTime * 16f);
            var draw = ImGui.GetWindowDrawList();
            var rounding = 8 * scale;

            draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(hovered ? Theme.BlueHover : Theme.Blue), rounding, ImDrawFlags.RoundCornersRight);
            // Yellow edge while a floating (undocked) window is open, so the tab shows it will hide it.
            if (open)
                draw.AddRectFilled(pos + new Vector2(size.X - 3 * scale, 0), pos + size, ImGui.GetColorU32(Theme.Yellow), rounding, ImDrawFlags.RoundCornersRight);

            var y = pos.Y + 8 * scale;
            using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            {
                var glyph = FontAwesomeIcon.Language.ToIconString();
                var iconSize = ImGui.CalcTextSize(glyph);
                draw.AddText(new Vector2(pos.X + (size.X - iconSize.X) / 2, y), ImGui.GetColorU32(Theme.Yellow), glyph);
                y += iconSize.Y + 6 * scale;
            }

            // ImGui can't rotate text, so the label is stacked one letter per line.
            var step = ImGui.GetTextLineHeight() * 0.82f;
            foreach (var ch in Label)
            {
                var letter = ch.ToString();
                var letterSize = ImGui.CalcTextSize(letter);
                draw.AddText(new Vector2(pos.X + (size.X - letterSize.X) / 2, y), ImGui.GetColorU32(Theme.White), letter);
                y += step;
            }

            if (hovered) ImGui.SetTooltip(open ? "Hide Konnyaku Jelly" : "Open Konnyaku Jelly");
            if (clicked) plugin.ToggleMainWindow();
        }
        ImGui.End();
        ImGui.PopStyleVar(3);
    }
}
