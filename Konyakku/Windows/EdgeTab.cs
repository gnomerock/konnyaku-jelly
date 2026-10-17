using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace Konyakku.Windows;

/// <summary>
/// A "Jelly" tab tucked into the left edge of the screen. Only a thin strip shows until the mouse
/// touches it, then it slides out; clicking it shows or hides the jelly window.
/// </summary>
internal sealed class EdgeTab(Plugin plugin)
{
    private const ImGuiWindowFlags Flags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.NoBringToFrontOnFocus;

    // 0 = tucked away, 1 = fully out.
    private float reveal;

    public void Draw()
    {
        if (!plugin.Config.ShowEdgeTab) return;

        var scale = ImGuiHelpers.GlobalScale;
        var viewport = ImGui.GetMainViewport();
        var size = new Vector2(92, 34) * scale;
        var peek = 7 * scale;
        var top = viewport.Pos.Y + viewport.Size.Y * 0.42f;

        // Hit zone is the visible strip (plus a little slack) while tucked, the whole tab once it's out.
        var mouse = ImGui.GetIO().MousePos;
        var reach = reveal > 0.5f ? size.X : peek + 6 * scale;
        var near = mouse.X >= viewport.Pos.X && mouse.X <= viewport.Pos.X + reach &&
                   mouse.Y >= top && mouse.Y <= top + size.Y;
        reveal += ((near ? 1f : 0f) - reveal) * Math.Min(1f, ImGui.GetIO().DeltaTime * 16f);

        ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X - (size.X - peek) * (1 - reveal), top));
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
        if (ImGui.Begin("##KonyakkuEdgeTab", Flags))
        {
            var pos = ImGui.GetWindowPos();
            var clicked = ImGui.InvisibleButton("##edge", size);
            var hovered = ImGui.IsItemHovered();
            var open = plugin.IsMainWindowOpen;
            var draw = ImGui.GetWindowDrawList();

            draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(hovered ? Theme.BlueHover : Theme.Blue), 10 * scale, ImDrawFlags.RoundCornersRight);
            // The strip that peeks out: yellow while the window is open, white while it's hidden.
            draw.AddRectFilled(pos + new Vector2(size.X - 4 * scale, 0), pos + size,
                ImGui.GetColorU32(open ? Theme.Yellow : Theme.White), 10 * scale, ImDrawFlags.RoundCornersRight);

            using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            {
                var glyph = FontAwesomeIcon.Language.ToIconString();
                var iconSize = ImGui.CalcTextSize(glyph);
                draw.AddText(pos + new Vector2(12 * scale, (size.Y - iconSize.Y) / 2), ImGui.GetColorU32(Theme.White), glyph);
            }
            const string label = "Jelly";
            var labelSize = ImGui.CalcTextSize(label);
            draw.AddText(pos + new Vector2(38 * scale, (size.Y - labelSize.Y) / 2), ImGui.GetColorU32(Theme.White), label);

            if (hovered) ImGui.SetTooltip(open ? "Hide the Konnyaku Jelly window" : "Show the Konnyaku Jelly window");
            if (clicked) plugin.ToggleMainWindow();
        }
        ImGui.End();
        ImGui.PopStyleVar(3);
    }
}
