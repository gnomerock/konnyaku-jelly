using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace Konyakku.Windows;

/// <summary>
/// The vertical "Jelly" tab. It sticks to the left or right edge of the screen, or floats where it was dropped.
/// Click to open or hide the jelly window; drag to move it. Dropped near an edge it snaps to that edge, and the
/// window opens as a drawer from it (see <see cref="LogWindow"/>), covering the tab while open.
/// </summary>
internal sealed class EdgeTab(Plugin plugin)
{
    private const ImGuiWindowFlags Flags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoBringToFrontOnFocus;

    private const string Label = "Jelly";
    private const float Width = 26f;
    private const float Height = 112f;
    /// <summary>Dropping the tab with the mouse this close to a screen edge sticks it to that edge.</summary>
    private const float SnapDistance = 60f;

    // 0 = resting, 1 = hovered; widens the tab a little under the mouse.
    private float hover;
    private bool dragging;
    private Vector2 grab;
    private Vector2 dragPos;

    /// <summary>Whether the window should open as a drawer: only while the tab sticks to an edge.</summary>
    public static bool Docked(Configuration config) => config.DockToEdge && config.TabEdge != TabEdge.Free;

    /// <summary>Screen Y where the tab starts. The docked window opens at the same height.</summary>
    public static float Top(ImGuiViewportPtr viewport, Configuration config)
    {
        var max = Math.Max(0, viewport.Size.Y - Height * ImGuiHelpers.GlobalScale);
        return viewport.Pos.Y + Math.Clamp(config.TabY * viewport.Size.Y, 0, max);
    }

    public void Draw()
    {
        var config = plugin.Config;
        if (!config.ShowEdgeTab) return;
        var open = plugin.IsMainWindowOpen;
        if (open && Docked(config) && !dragging) return;

        var scale = ImGuiHelpers.GlobalScale;
        var viewport = ImGui.GetMainViewport();
        var mouse = ImGui.GetIO().MousePos;
        var snap = SnapDistance * scale;

        // While dragging, the shape previews where the tab will land.
        var edge = !dragging ? config.TabEdge
            : mouse.X - viewport.Pos.X < snap ? TabEdge.Left
            : viewport.Pos.X + viewport.Size.X - mouse.X < snap ? TabEdge.Right
            : TabEdge.Free;
        var size = new Vector2((edge == TabEdge.Free ? Width + 4 : Width + 6 * hover) * scale, Height * scale);

        float top;
        if (dragging) top = Math.Clamp(dragPos.Y, viewport.Pos.Y, viewport.Pos.Y + viewport.Size.Y - size.Y);
        else top = Top(viewport, config);
        var left = edge switch
        {
            TabEdge.Left => viewport.Pos.X,
            TabEdge.Right => viewport.Pos.X + viewport.Size.X - size.X,
            _ when dragging => Math.Clamp(dragPos.X, viewport.Pos.X, viewport.Pos.X + viewport.Size.X - size.X),
            _ => viewport.Pos.X + Math.Clamp(config.TabX * viewport.Size.X, 0, Math.Max(0, viewport.Size.X - size.X)),
        };

        ImGui.SetNextWindowPos(new Vector2(left, top));
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
        if (ImGui.Begin("##KonyakkuEdgeTab", Flags))
        {
            var pos = ImGui.GetWindowPos();
            var clicked = ImGui.InvisibleButton("##edge", size);
            var hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemActivated()) grab = mouse - pos;
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4 * scale))
            {
                dragging = true;
                dragPos = mouse - grab;
            }
            else if (dragging && !ImGui.IsItemActive())
            {
                dragging = false;
                clicked = false;
                Drop(config, viewport, edge, pos, size);
            }

            hover += ((hovered || dragging ? 1f : 0f) - hover) * Math.Min(1f, ImGui.GetIO().DeltaTime * 16f);
            DrawTab(edge, pos, size, hovered || dragging, open, scale);

            if (hovered && !dragging) ImGui.SetTooltip((open ? "Hide Konnyaku Jelly" : "Open Konnyaku Jelly") + "\nDrag to move it. Drop it at the left or right edge to stick it there.");
            if (clicked && !dragging) plugin.ToggleMainWindow();
        }
        ImGui.End();
        ImGui.PopStyleVar(3);
    }

    private static void Drop(Configuration config, ImGuiViewportPtr viewport, TabEdge edge, Vector2 pos, Vector2 size)
    {
        config.TabEdge = edge;
        config.TabY = (pos.Y - viewport.Pos.Y) / viewport.Size.Y;
        if (edge == TabEdge.Free) config.TabX = (pos.X - viewport.Pos.X) / viewport.Size.X;
        config.Save();
    }

    private static void DrawTab(TabEdge edge, Vector2 pos, Vector2 size, bool hovered, bool open, float scale)
    {
        var draw = ImGui.GetWindowDrawList();
        var rounding = 8 * scale;
        // Square on the side that touches the screen edge; rounded all round when floating.
        var corners = edge switch
        {
            TabEdge.Left => ImDrawFlags.RoundCornersRight,
            TabEdge.Right => ImDrawFlags.RoundCornersLeft,
            _ => ImDrawFlags.RoundCornersAll,
        };

        draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(hovered ? Theme.BlueHover : Theme.Blue), rounding, corners);
        // Yellow strip on the inner side while a floating (undocked) window is open, so the tab shows it will hide it.
        if (open)
        {
            var strip = 3 * scale;
            if (edge == TabEdge.Right) draw.AddRectFilled(pos, pos + new Vector2(strip, size.Y), ImGui.GetColorU32(Theme.Yellow), rounding, corners);
            else draw.AddRectFilled(pos + new Vector2(size.X - strip, 0), pos + size, ImGui.GetColorU32(Theme.Yellow), rounding, corners);
        }
        if (edge == TabEdge.Free) draw.AddRect(pos, pos + size, ImGui.GetColorU32(Theme.White with { W = 0.35f }), rounding, corners, scale);

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
    }
}
