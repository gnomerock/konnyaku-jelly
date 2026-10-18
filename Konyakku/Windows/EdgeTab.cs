using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace Konyakku.Windows;

/// <summary>
/// The vertical "Jelly" tab. It sticks to the left or right edge of the screen. Click to open or hide the jelly
/// window; drag it along an edge to move it. The window opens as a drawer from that edge (see <see cref="LogWindow"/>),
/// covering the tab while open. Dragged out away from the edges, the tab turns into a normal window where it's dropped
/// (<see cref="Configuration.Detached"/>). While dragged it wobbles like jelly.
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
    /// <summary>Dropping the tab (or dragging the window) with the mouse this close to a screen edge sticks it to that edge.</summary>
    public const float SnapDistance = 60f;

    // The jelly: the shape's far side lags behind the grab point on a damped spring and wobbles back.
    private const float Stiffness = 240f;
    private const float Damping = 11f;
    private const float MaxLag = 26f;

    // 0 = resting, 1 = hovered; widens the tab a little under the mouse.
    private float hover;
    private bool wasHovered;
    private bool dragging;
    private Vector2 grab;
    private Vector2 dragPos;
    // 0 = tab, 1 = previewing the window it turns into when dropped away from the edges.
    private float morph;

    private bool haveLast;
    private Vector2 lastPos;
    private Vector2 lag;
    private Vector2 lagVelocity;
    // Where the jelly is held, relative to the tab: the grab point while dragging, easing back to the edge side.
    private Vector2 anchor;
    private Vector2 anchorPos;
    private float reach = 1;
    private readonly List<Vector2> points = [];

    /// <summary>Whether the window should open as a drawer: while the tab sticks to an edge and wasn't dragged out.</summary>
    public static bool Docked(Configuration config) => config.DockToEdge && !config.Detached;

    /// <summary>Screen Y where the tab starts. The docked window opens at the same height.</summary>
    public static float Top(ImGuiViewportPtr viewport, Configuration config)
    {
        var max = Math.Max(0, viewport.Size.Y - Height * ImGuiHelpers.GlobalScale);
        return viewport.Pos.Y + Math.Clamp(config.TabY * viewport.Size.Y, 0, max);
    }

    /// <summary>The screen edge the mouse is close enough to snap to, if any.</summary>
    public static TabEdge? EdgeAt(Vector2 mouse, ImGuiViewportPtr viewport)
    {
        var snap = SnapDistance * ImGuiHelpers.GlobalScale;
        if (mouse.X - viewport.Pos.X < snap) return TabEdge.Left;
        if (viewport.Pos.X + viewport.Size.X - mouse.X < snap) return TabEdge.Right;
        return null;
    }

    public void Draw()
    {
        var config = plugin.Config;
        var open = plugin.IsMainWindowOpen;
        // A docked or detached window takes the tab's place while it's open.
        if (!config.ShowEdgeTab || (open && (Docked(config) || config.Detached) && !dragging))
        {
            haveLast = false;
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var io = ImGui.GetIO();
        var dt = Math.Min(io.DeltaTime, 1f / 30);
        var viewport = ImGui.GetMainViewport();
        var mouse = io.MousePos;

        // While dragging, the shape previews where the tab will land; null means "turn into a window here".
        TabEdge? edge = dragging ? EdgeAt(mouse, viewport) : config.TabEdge;
        morph += ((edge == null ? 1f : 0f) - morph) * Math.Min(1f, dt * 10f);
        if (morph < 0.01f) morph = 0;
        var size = new Vector2((edge == null ? Width + 4 : Width + 6 * hover) * scale, Height * scale);

        var top = dragging ? Math.Clamp(dragPos.Y, viewport.Pos.Y, viewport.Pos.Y + viewport.Size.Y - size.Y) : Top(viewport, config);
        var left = edge switch
        {
            TabEdge.Left => viewport.Pos.X,
            TabEdge.Right => viewport.Pos.X + viewport.Size.X - size.X,
            _ => Math.Clamp(dragPos.X, viewport.Pos.X, viewport.Pos.X + viewport.Size.X - size.X),
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
                Drop(config, viewport, edge, pos, scale);
            }

            // A little poke when the mouse arrives, pushing the jelly away from the edge.
            if (hovered && !wasHovered && !dragging) lagVelocity.X += (config.TabEdge == TabEdge.Right ? -1 : 1) * 120 * scale;
            wasHovered = hovered;
            hover += ((hovered || dragging ? 1f : 0f) - hover) * Math.Min(1f, dt * 16f);

            Wobble(pos, size, edge, dt, scale);
            // Draw outside the tab's own rectangle: the jelly bulges past it and the window preview is much bigger.
            var draw = ImGui.GetWindowDrawList();
            draw.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, false);
            if (morph > 0) DrawPreview(draw, pos, scale);
            DrawTab(draw, edge, pos, size, hovered || dragging, open, scale, 1 - morph * 0.7f);
            draw.PopClipRect();

            if (hovered && !dragging) ImGui.SetTooltip((open ? "Hide Konnyaku Jelly" : "Open Konnyaku Jelly") + "\nDrag it along the edge to move it, or out onto the screen to make it a normal window.");
            if (clicked && !dragging) plugin.ToggleMainWindow();
        }
        ImGui.End();
        ImGui.PopStyleVar(3);
    }

    private void Drop(Configuration config, ImGuiViewportPtr viewport, TabEdge? edge, Vector2 pos, float scale)
    {
        if (edge is { } e)
        {
            config.TabEdge = e;
            config.TabY = (pos.Y - viewport.Pos.Y) / viewport.Size.Y;
            config.Detached = false;
        }
        else
        {
            // Turn into the window where the tab was dropped; the tab goes back to its edge until the window closes.
            var window = plugin.MainWindowSize;
            var at = new Vector2(
                Math.Clamp(pos.X, viewport.Pos.X, Math.Max(viewport.Pos.X, viewport.Pos.X + viewport.Size.X - window.X)),
                Math.Clamp(pos.Y, viewport.Pos.Y, Math.Max(viewport.Pos.Y, viewport.Pos.Y + viewport.Size.Y - window.Y)));
            config.Detached = true;
            plugin.OpenMainWindowAt(at);
            morph = 0;
            haveLast = false;
        }
        config.Save();
    }

    /// <summary>Moves the spring: whatever the tab moved this frame, its far side is left behind and springs back.</summary>
    private void Wobble(Vector2 pos, Vector2 size, TabEdge? edge, float dt, float scale)
    {
        var rest = edge switch
        {
            TabEdge.Left => new Vector2(0, size.Y / 2),
            TabEdge.Right => new Vector2(size.X, size.Y / 2),
            _ => size / 2,
        };
        if (!haveLast)
        {
            lastPos = pos;
            lag = lagVelocity = Vector2.Zero;
            anchor = rest;
            haveLast = true;
        }
        anchor = dragging ? grab : anchor + (rest - anchor) * Math.Min(1f, dt * 6f);

        lag -= pos - lastPos;
        lastPos = pos;
        // Two half steps keep the spring stable at low frame rates.
        for (var i = 0; i < 2; i++)
        {
            var step = dt / 2;
            lagVelocity += (-Stiffness * lag - Damping * lagVelocity) * step;
            lag += lagVelocity * step;
        }
        var max = MaxLag * scale;
        if (lag.Length() > max) lag = Vector2.Normalize(lag) * max;
        if (!dragging && lag.LengthSquared() < 0.01f && lagVelocity.LengthSquared() < 0.01f) lag = lagVelocity = Vector2.Zero;

        anchorPos = pos + anchor;
        reach = Height * scale;
    }

    /// <summary>A point of the shape after the jelly's lag: the further from where it's held, the more it trails.</summary>
    private Vector2 Jelly(Vector2 p)
    {
        var weight = Math.Min(1f, Vector2.Distance(p, anchorPos) / reach);
        return p + lag * weight;
    }

    /// <summary>A translucent window growing out of the tab: what dropping it here will open.</summary>
    private void DrawPreview(ImDrawListPtr draw, Vector2 pos, float scale)
    {
        var eased = 1 - MathF.Pow(1 - morph, 3);
        var tab = new Vector2(Width * scale, Height * scale);
        var size = Vector2.Lerp(tab, plugin.MainWindowSize, eased);
        var rounding = 8 * scale;

        Shape(draw, pos, pos + size, rounding, ImDrawFlags.RoundCornersAll);
        draw.PathFillConvex(ImGui.GetColorU32(Theme.Blue with { W = 0.28f * morph }));
        var title = Math.Min(size.Y, ImGui.GetFrameHeight());
        Shape(draw, pos, pos + new Vector2(size.X, title), rounding, ImDrawFlags.RoundCornersTop);
        draw.PathFillConvex(ImGui.GetColorU32(Theme.Blue with { W = 0.8f * morph }));
        Shape(draw, pos, pos + size, rounding, ImDrawFlags.RoundCornersAll);
        draw.PathStroke(ImGui.GetColorU32(Theme.White with { W = 0.6f * morph }), ImDrawFlags.Closed, 1.5f * scale);

        if (size.X > 160 * scale)
        {
            var text = "Konnyaku Jelly";
            var at = Jelly(pos + new Vector2(10 * scale, (title - ImGui.GetTextLineHeight()) / 2));
            draw.AddText(at, ImGui.GetColorU32(Theme.White with { W = morph }), text);
        }
    }

    private void DrawTab(ImDrawListPtr draw, TabEdge? edge, Vector2 pos, Vector2 size, bool hovered, bool open, float scale, float alpha)
    {
        var rounding = 8 * scale;
        // Square on the side that touches the screen edge; rounded all round while dragged out.
        var corners = edge switch
        {
            TabEdge.Left => ImDrawFlags.RoundCornersRight,
            TabEdge.Right => ImDrawFlags.RoundCornersLeft,
            _ => ImDrawFlags.RoundCornersAll,
        };

        Shape(draw, pos, pos + size, rounding, corners);
        draw.PathFillConvex(ImGui.GetColorU32((hovered ? Theme.BlueHover : Theme.Blue) with { W = alpha }));
        // Yellow strip on the inner side while a normal window is open, so the tab shows it will hide it.
        if (open && edge != null)
        {
            var strip = 3 * scale;
            if (edge == TabEdge.Right) Shape(draw, pos, pos + new Vector2(strip, size.Y), rounding, corners);
            else Shape(draw, pos + new Vector2(size.X - strip, 0), pos + size, rounding, corners);
            draw.PathFillConvex(ImGui.GetColorU32(Theme.Yellow with { W = alpha }));
        }
        if (edge == null)
        {
            Shape(draw, pos, pos + size, rounding, corners);
            draw.PathStroke(ImGui.GetColorU32(Theme.White with { W = 0.35f * alpha }), ImDrawFlags.Closed, scale);
        }

        var y = pos.Y + 8 * scale;
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            var glyph = FontAwesomeIcon.Language.ToIconString();
            var iconSize = ImGui.CalcTextSize(glyph);
            draw.AddText(Jelly(new Vector2(pos.X + size.X / 2, y + iconSize.Y / 2)) - iconSize / 2, ImGui.GetColorU32(Theme.Yellow with { W = alpha }), glyph);
            y += iconSize.Y + 6 * scale;
        }

        // ImGui can't rotate text, so the label is stacked one letter per line.
        var step = ImGui.GetTextLineHeight() * 0.82f;
        foreach (var ch in Label)
        {
            var letter = ch.ToString();
            var letterSize = ImGui.CalcTextSize(letter);
            draw.AddText(Jelly(new Vector2(pos.X + size.X / 2, y + letterSize.Y / 2)) - letterSize / 2, ImGui.GetColorU32(Theme.White with { W = alpha }), letter);
            y += step;
        }
    }

    /// <summary>Puts a rounded rectangle, bent by the jelly, into the draw list's path for a fill or a stroke.</summary>
    private void Shape(ImDrawListPtr draw, Vector2 min, Vector2 max, float rounding, ImDrawFlags corners)
    {
        rounding = Math.Min(rounding, Math.Min(max.X - min.X, max.Y - min.Y) / 2);
        points.Clear();
        Corner(new Vector2(min.X, min.Y), new Vector2(1, 1), MathF.PI, corners.HasFlag(ImDrawFlags.RoundCornersTopLeft), rounding);
        Corner(new Vector2(max.X, min.Y), new Vector2(-1, 1), MathF.PI * 1.5f, corners.HasFlag(ImDrawFlags.RoundCornersTopRight), rounding);
        Corner(new Vector2(max.X, max.Y), new Vector2(-1, -1), 0, corners.HasFlag(ImDrawFlags.RoundCornersBottomRight), rounding);
        Corner(new Vector2(min.X, max.Y), new Vector2(1, -1), MathF.PI * 0.5f, corners.HasFlag(ImDrawFlags.RoundCornersBottomLeft), rounding);

        // Long straight sides get extra points so they can bend too.
        draw.PathClear();
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var pieces = Math.Max(1, (int)(Vector2.Distance(a, b) / (12 * ImGuiHelpers.GlobalScale)));
            for (var j = 0; j < pieces; j++) draw.PathLineTo(Jelly(Vector2.Lerp(a, b, (float)j / pieces)));
        }
    }

    private void Corner(Vector2 corner, Vector2 inward, float start, bool round, float rounding)
    {
        if (!round || rounding <= 0)
        {
            points.Add(corner);
            return;
        }
        var centre = corner + inward * rounding;
        const int segments = 5;
        for (var i = 0; i <= segments; i++)
        {
            var angle = start + MathF.PI / 2 * i / segments;
            points.Add(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * rounding);
        }
    }
}
