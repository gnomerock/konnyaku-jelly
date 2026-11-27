using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Konyakku.Windows;

internal sealed class ConfigWindow : Window
{
    private static readonly string[] BlackHoleStrategies = ["Standard", "Fae"];

    private const float FieldWidth = 240f;

    private readonly Plugin plugin;
    private string testResult = string.Empty;
    private bool testOk;
    private bool testing;
    private bool themePushed;

    public ConfigWindow(Plugin plugin) : base("Konyaku Jelly Settings###KonyakkuConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(500, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => themePushed = Theme.Push(plugin.Config.UseTheme);

    public override void PostDraw() => Theme.Pop(themePushed);

    public override void OnClose() => plugin.DancingMad.Preview = false;

    public override void Draw()
    {
        var c = plugin.Config;
        var changed = false;

        if (ImGui.BeginTabBar("##settings"))
        {
            if (ImGui.BeginTabItem("  Translator  ")) { DrawProvider(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("  Languages  ")) { changed |= DrawLanguages(c); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("  Chat  ")) { changed |= DrawChat(c); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("  Look  ")) { changed |= DrawLook(c); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("  DMU  ")) { changed |= DrawDmu(c); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        if (changed) c.Save();
    }

    private void DrawProvider()
    {
        Heading("Google Translate");
        ImGui.TextWrapped("Chat is translated with Google Translate. It's free and needs no key or login.");
        ImGui.TextDisabled("Uses the same free endpoint as Google's web translator. Not an official API, so heavy use may be rate limited.");

        ImGui.Spacing();
        ImGui.BeginDisabled(testing);
        if (Theme.PrimaryButton(FontAwesomeIcon.Plug, testing ? Theme.Dots("Testing") : "Test connection")) RunTest();
        ImGui.EndDisabled();
        if (testResult.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, testOk ? Theme.Ok : Theme.Error);
            ImGui.TextWrapped(testResult);
            ImGui.PopStyleColor();
        }
    }

    private static bool DrawLanguages(Configuration c)
    {
        var changed = false;
        Heading("Languages");
        changed |= Combo("Chat I read", Language.Incoming, c.IncomingTarget, v => c.IncomingTarget = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Other players' messages are translated into this language.");
        changed |= Combo("Messages I send", Language.All, c.OutgoingTarget, v => c.OutgoingTarget = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("What you type in the Konyaku Jelly window or with /jelly is translated into this language.");
        return changed;
    }

    private static bool DrawChat(Configuration c)
    {
        var changed = false;
        Heading("Chat");
        changed |= Checkbox("Only translate chat while the Konyaku Jelly window is open", c.TranslateOnlyWhenOpen, v => c.TranslateOnlyWhenOpen = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Fewer requests: messages that arrive while the window is closed are not translated. Open it with /jelly.");
        changed |= Checkbox("Skip my own messages", c.SkipOwnMessages, v => c.SkipOwnMessages = v);
        changed |= Checkbox("Show the original text under each translation", c.ShowOriginal, v => c.ShowOriginal = v);
        changed |= Checkbox("Also echo translations into game chat", c.EchoToGameChat, v => c.EchoToGameChat = v);
        if (c.EchoToGameChat && c.IncomingTarget == "Thai")
            ImGui.TextColored(Theme.Warn, "Thai isn't echoed: the game's chat font has no Thai letters. Read it in the Konyaku Jelly window.");
        changed |= Checkbox("Translate button next to the game's chat box", c.ShowChatBoxButton, v => c.ShowChatBoxButton = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Type in the game's chat box, then click the button: what you typed is replaced with its translation (Write in), ready to send with Enter. A channel command such as /p stays as typed.");

        ImGui.Spacing();
        Heading("Channels to translate");
        ImGui.TextDisabled("Only Party and Alliance are on by default. Shout and Yell in busy cities send a lot of requests.");
        if (ImGui.BeginTable("##channels", 3))
        {
            foreach (var (type, name) in ChannelNames.Selectable)
            {
                ImGui.TableNextColumn();
                var on = c.Channels.Contains(type);
                if (ImGui.Checkbox(name, ref on))
                {
                    if (on) c.Channels.Add(type); else c.Channels.Remove(type);
                    changed = true;
                }
            }
            ImGui.EndTable();
        }
        return changed;
    }

    private static bool DrawLook(Configuration c)
    {
        var changed = false;
        Heading("Look");
        changed |= Checkbox("Blue & white theme", c.UseTheme, v => c.UseTheme = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Turn off to use your normal Dalamud style.");
        changed |= Checkbox("Show the Jelly tab", c.ShowEdgeTab, v => c.ShowEdgeTab = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("A small vertical tab that opens or hides the Konyaku Jelly window. Drag it along the left or right edge of the screen to move it, or drag it out onto the screen to turn it into a normal window there.");
        if (c.ShowEdgeTab && (c.TabEdge != TabEdge.Left || c.Detached))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Back to the left edge"))
            {
                c.TabEdge = TabEdge.Left;
                c.TabY = 0.18f;
                c.Detached = false;
                changed = true;
            }
        }
        changed |= Checkbox("Open the window as a drawer from the Jelly tab's edge", c.DockToEdge, v => c.DockToEdge = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("The window slides out from the Jelly tab's edge, level with the tab. Drag the tab out onto the screen to make it a normal window instead, and drag that window against a screen edge to dock it again. When this is off, the window is always a normal window.");
        var max = c.MaxLogEntries;
        ImGui.SetNextItemWidth(FieldWidth);
        if (ImGui.SliderInt("Lines kept in the log", ref max, 50, 1000)) { c.MaxLogEntries = max; changed = true; }
        return changed;
    }

    private bool DrawDmu(Configuration c)
    {
        var changed = false;
        Heading("Dancing Mad (Ultimate)");
        changed |= Checkbox("Enable DMU helper", c.DmuEnabled, v => c.DmuEnabled = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Turns every Dancing Mad feature below on or off. They only work inside Dancing Mad.");
        using var disabled = ImRaii.Disabled(!c.DmuEnabled);

        ImGui.Spacing();
        Heading("Auto-mark delay");
        if (c.DmuHumanize)
        {
            int min = c.DmuDelayMinMs, max = c.DmuDelayMaxMs;
            ImGui.SetNextItemWidth(FieldWidth);
            if (ImGui.DragIntRange2("Delay", ref min, ref max, 5f, 0, Configuration.MaxDmuDelayMs, "Min %d ms", "Max %d ms"))
            {
                c.DmuDelayMinMs = Math.Clamp(min, 0, Configuration.MaxDmuDelayMs);
                c.DmuDelayMaxMs = Math.Clamp(max, c.DmuDelayMinMs, Configuration.MaxDmuDelayMs);
                changed = true;
            }
        }
        else
        {
            var delay = c.DmuMarkDelayMs;
            ImGui.SetNextItemWidth(FieldWidth);
            if (ImGui.SliderInt("Delay", ref delay, 0, Configuration.MaxDmuDelayMs, "%d ms")) { c.DmuMarkDelayMs = delay; changed = true; }
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("How long to wait before placing a mark.");
        changed |= Checkbox("Random delay", c.DmuHumanize, v => c.DmuHumanize = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Wait a different time between Min and Max for each mark, so it looks like you typed it.");

        ImGui.Spacing();
        Heading("P2 · Path of Light");
        changed |= Checkbox("Enable P2 auto-mark", c.DmuP2Marks, v => c.DmuP2Marks = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("After you soak the 3rd tower, marks you by the marker you get. Test sends the command now so you can check it.");
        using (ImRaii.Disabled(!c.DmuP2Marks))
        {
            changed |= CommandInput("Circle", c.DmuP2Circle, v => c.DmuP2Circle = v);
            changed |= CommandInput("Cone", c.DmuP2Cone, v => c.DmuP2Cone = v);
        }

        ImGui.Spacing();
        Heading("P3 · Black Hole");
        changed |= Checkbox("Enable P3 auto-mark", c.DmuP3Marks, v => c.DmuP3Marks = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Marks you by your First, Second or Third in Line debuff. Test sends the command now so you can check it.");
        using (ImRaii.Disabled(!c.DmuP3Marks))
        {
            changed |= CommandInput("1st in Line", c.DmuP3First, v => c.DmuP3First = v);
            changed |= CommandInput("2nd in Line", c.DmuP3Second, v => c.DmuP3Second = v);
            changed |= CommandInput("3rd in Line", c.DmuP3Third, v => c.DmuP3Third = v);
        }
        changed |= Checkbox("Enable P3 tether helper", c.DmuP3Guide, v => c.DmuP3Guide = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Shows your role and which tether to grab next.");
        using (ImRaii.Disabled(!c.DmuP3Guide))
            changed |= Combo("Strategy", BlackHoleStrategies, c.DmuBlackHoleStrategy, v => c.DmuBlackHoleStrategy = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Fae swaps the DPS and support tethers.");

        ImGui.Spacing();
        Heading("P4 · Debuffs");
        changed |= Checkbox("Enable P4 debuff helper", c.DmuGuide, v => c.DmuGuide = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Shows each of your debuffs with what to do: stack or spread, move or stop, which side to go.");

        ImGui.Spacing();
        ImGui.Separator();
        var preview = plugin.DancingMad.Preview;
        if (ImGui.Checkbox("Show example overlay", ref preview)) plugin.DancingMad.Preview = preview;
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Shows a sample so you can drag the overlay into place. Turns off when you close Settings.");
        return changed;
    }

    private bool CommandInput(string label, string value, Action<string> set)
    {
        ImGui.SetNextItemWidth(FieldWidth);
        var changed = ImGui.InputTextWithHint($"##{label}", "/mk ignore <me>", ref value, 100);
        if (changed) set(value);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Test##{label}")) plugin.DancingMad.QueueMark(value);
        Theme.Tooltip($"Send \"{value}\" now");
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        return changed;
    }

    private void RunTest()
    {
        testing = true;
        testResult = string.Empty;
        Task.Run(async () =>
        {
            try
            {
                var r = await plugin.Translator.TranslateAsync("Thanks for the run, gg! See you next time o/", "Japanese");
                testOk = true;
                testResult = $"It works! → {r}";
            }
            catch (Exception ex)
            {
                testOk = false;
                testResult = $"Couldn't connect: {ex.Message}";
            }
            finally
            {
                testing = false;
            }
        });
    }

    private static void Heading(string text)
    {
        ImGui.TextColored(Theme.Yellow, text);
        ImGui.Spacing();
    }

    private static bool Checkbox(string label, bool value, Action<bool> set)
    {
        if (!ImGui.Checkbox(label, ref value)) return false;
        set(value);
        return true;
    }

    private static bool Combo(string label, string[] items, string current, Action<string> set)
    {
        var changed = false;
        ImGui.SetNextItemWidth(FieldWidth);
        if (ImGui.BeginCombo(label, current))
        {
            foreach (var item in items)
            {
                if (ImGui.Selectable(item, item == current)) { set(item); changed = true; }
            }
            ImGui.EndCombo();
        }
        return changed;
    }
}
