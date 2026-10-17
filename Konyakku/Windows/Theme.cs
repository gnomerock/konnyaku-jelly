using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace Konyakku.Windows;

/// <summary>Blue / white / red / bell-yellow palette and small widgets shared by the windows.</summary>
internal static class Theme
{
    public static readonly Vector4 Blue = Rgb(0, 150, 224);
    public static readonly Vector4 BlueHover = Rgb(40, 175, 240);
    public static readonly Vector4 BlueActive = Rgb(0, 120, 190);
    public static readonly Vector4 DeepBlue = Rgb(8, 70, 120);
    public static readonly Vector4 Night = Rgb(10, 42, 72);
    public static readonly Vector4 White = Rgb(255, 255, 255);
    public static readonly Vector4 Red = Rgb(226, 30, 40);
    public static readonly Vector4 RedHover = Rgb(245, 70, 75);
    public static readonly Vector4 RedActive = Rgb(190, 15, 25);
    public static readonly Vector4 Yellow = Rgb(255, 210, 0);
    public static readonly Vector4 Soft = Rgb(170, 205, 235);
    public static readonly Vector4 Ok = Rgb(120, 230, 140);
    public static readonly Vector4 Warn = Rgb(255, 180, 80);
    public static readonly Vector4 Error = Rgb(255, 120, 120);
    public static readonly Vector4 Ink = Rgb(10, 35, 60);

    private static readonly (ImGuiCol Col, Vector4 Value)[] Colors =
    [
        (ImGuiCol.Text, White),
        (ImGuiCol.TextDisabled, Soft),
        (ImGuiCol.WindowBg, Rgb(10, 42, 72, 0.97f)),
        (ImGuiCol.ChildBg, Rgb(255, 255, 255, 0.05f)),
        (ImGuiCol.PopupBg, Rgb(12, 50, 86, 0.98f)),
        (ImGuiCol.Border, Rgb(255, 255, 255, 0.18f)),
        (ImGuiCol.FrameBg, Rgb(255, 255, 255, 0.10f)),
        (ImGuiCol.FrameBgHovered, Rgb(255, 255, 255, 0.16f)),
        (ImGuiCol.FrameBgActive, Rgb(255, 255, 255, 0.22f)),
        (ImGuiCol.TitleBg, DeepBlue),
        (ImGuiCol.TitleBgActive, Blue),
        (ImGuiCol.TitleBgCollapsed, DeepBlue),
        (ImGuiCol.ScrollbarBg, Rgb(0, 0, 0, 0.10f)),
        (ImGuiCol.ScrollbarGrab, Rgb(255, 255, 255, 0.25f)),
        (ImGuiCol.ScrollbarGrabHovered, Rgb(255, 255, 255, 0.35f)),
        (ImGuiCol.ScrollbarGrabActive, Rgb(255, 255, 255, 0.45f)),
        (ImGuiCol.CheckMark, Yellow),
        (ImGuiCol.SliderGrab, Yellow),
        (ImGuiCol.SliderGrabActive, Rgb(255, 230, 90)),
        (ImGuiCol.Button, Blue),
        (ImGuiCol.ButtonHovered, BlueHover),
        (ImGuiCol.ButtonActive, BlueActive),
        (ImGuiCol.Header, Rgb(0, 150, 224, 0.55f)),
        (ImGuiCol.HeaderHovered, Rgb(0, 150, 224, 0.75f)),
        (ImGuiCol.HeaderActive, Blue),
        (ImGuiCol.Separator, Rgb(255, 255, 255, 0.15f)),
        (ImGuiCol.ResizeGrip, Rgb(255, 210, 0, 0.30f)),
        (ImGuiCol.ResizeGripHovered, Rgb(255, 210, 0, 0.60f)),
        (ImGuiCol.ResizeGripActive, Yellow),
        (ImGuiCol.Tab, DeepBlue),
        (ImGuiCol.TabHovered, BlueHover),
        (ImGuiCol.TabActive, Blue),
        (ImGuiCol.TabUnfocused, DeepBlue),
        (ImGuiCol.TabUnfocusedActive, Blue),
        (ImGuiCol.TextSelectedBg, Rgb(255, 210, 0, 0.35f)),
    ];

    private static readonly (ImGuiStyleVar Var, float Value)[] Floats =
    [
        (ImGuiStyleVar.WindowRounding, 12f),
        (ImGuiStyleVar.ChildRounding, 10f),
        (ImGuiStyleVar.FrameRounding, 8f),
        (ImGuiStyleVar.PopupRounding, 8f),
        (ImGuiStyleVar.GrabRounding, 8f),
        (ImGuiStyleVar.TabRounding, 8f),
        (ImGuiStyleVar.ScrollbarRounding, 8f),
    ];

    private static readonly (ImGuiStyleVar Var, Vector2 Value)[] Vectors =
    [
        (ImGuiStyleVar.WindowPadding, new Vector2(12, 10)),
        (ImGuiStyleVar.FramePadding, new Vector2(8, 5)),
        (ImGuiStyleVar.ItemSpacing, new Vector2(8, 6)),
    ];

    /// <summary>Pushes the theme when it is turned on. Pass the result to <see cref="Pop"/>.</summary>
    public static bool Push(bool enabled)
    {
        if (!enabled) return false;
        foreach (var (col, value) in Colors) ImGui.PushStyleColor(col, value);
        foreach (var (v, value) in Floats) ImGui.PushStyleVar(v, value);
        foreach (var (v, value) in Vectors) ImGui.PushStyleVar(v, value);
        return true;
    }

    public static void Pop(bool pushed)
    {
        if (!pushed) return;
        ImGui.PopStyleVar(Floats.Length + Vectors.Length);
        ImGui.PopStyleColor(Colors.Length);
    }

    /// <summary>A red call-to-action button with an icon.</summary>
    public static bool PrimaryButton(FontAwesomeIcon icon, string label) =>
        ImGuiComponents.IconButtonWithText(icon, label, Red, RedActive, RedHover);

    public static bool IconButton(string id, FontAwesomeIcon icon, string tooltip)
    {
        var clicked = ImGuiComponents.IconButton(id, icon);
        Tooltip(tooltip);
        return clicked;
    }

    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(text);
    }

    /// <summary>A small rounded label, e.g. the chat channel of a log line.</summary>
    public static void Badge(string text, Vector4 color)
    {
        var pad = new Vector2(6, 1);
        var size = ImGui.CalcTextSize(text) + pad * 2;
        var pos = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(color), 6f);
        draw.AddText(pos + pad, ImGui.GetColorU32(Ink), text);
        ImGui.Dummy(size);
    }

    public static Vector4 ChannelColor(string channel) => channel switch
    {
        "me" or "Send" => Rgb(255, 150, 150),
        "PF" => Yellow,
        "Say" => Rgb(230, 235, 240),
        "Shout" or "Yell" => Rgb(255, 190, 120),
        "Party" or "Cross-world Party" => Rgb(120, 205, 255),
        "Alliance" => Rgb(255, 170, 90),
        "Free Company" => Rgb(150, 230, 160),
        "Tell" => Rgb(255, 170, 225),
        "Novice Network" => Rgb(200, 240, 140),
        _ when channel.StartsWith('/') => Rgb(255, 150, 150),
        _ => Rgb(255, 230, 140),
    };

    public static Vector4 Fade(Vector4 color, float alpha) => color with { W = alpha };

    /// <summary>"Translating", "Translating.", … for busy labels.</summary>
    public static string Dots(string text) => text + new string('.', (int)(ImGui.GetTime() * 3) % 4);

    private static Vector4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
}
