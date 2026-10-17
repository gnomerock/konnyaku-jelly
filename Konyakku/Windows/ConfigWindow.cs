using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Konyakku.Translation;

namespace Konyakku.Windows;

internal sealed class ConfigWindow : Window
{
    private static readonly string[] Efforts = ["none", "minimal", "low", "medium", "high"];

    private static readonly (ProviderKind Kind, string Name, string About)[] Providers =
    [
        (ProviderKind.ChatGptSubscription, "ChatGPT subscription", "Uses your ChatGPT Plus / Pro plan. No extra cost, needs Codex CLI login."),
        (ProviderKind.OpenAiApiKey, "OpenAI API key", "Pay per use with an OpenAI API key."),
        (ProviderKind.Claude, "Claude API key", "Pay per use with an Anthropic API key."),
        (ProviderKind.Gemini, "Gemini API key", "Pay per use with a Google AI Studio API key."),
    ];

    private const float FieldWidth = 240f;

    private readonly Plugin plugin;
    private string testResult = string.Empty;
    private bool testOk;
    private bool testing;
    private bool themePushed;

    public ConfigWindow(Plugin plugin) : base("Konnyaku Jelly Settings###KonyakkuConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(500, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => themePushed = Theme.Push(plugin.Config.UseTheme);

    public override void PostDraw() => Theme.Pop(themePushed);

    public override void Draw()
    {
        var c = plugin.Config;
        var changed = false;

        if (ImGui.BeginTabBar("##settings"))
        {
            if (ImGui.BeginTabItem("  Translator  ")) { changed |= DrawProvider(c); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("  Languages  ")) { changed |= DrawLanguages(c); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("  Chat  ")) { changed |= DrawChat(c); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("  Look  ")) { changed |= DrawLook(c); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        if (changed) c.Save();
    }

    private bool DrawProvider(Configuration c)
    {
        var changed = false;
        Heading("Which AI should translate?");
        foreach (var (kind, name, about) in Providers)
        {
            if (ImGui.RadioButton(name, c.Provider == kind)) { c.Provider = kind; changed = true; }
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(about);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        switch (c.Provider)
        {
            case ProviderKind.ChatGptSubscription:
                if (CodexAuth.Exists)
                    ImGui.TextColored(Theme.Ok, "ChatGPT login found. You're good to go!");
                else
                {
                    ImGui.TextColored(Theme.Warn, "Not logged in yet.");
                    ImGui.TextWrapped("Install Codex CLI (npm i -g @openai/codex), then run: codex login");
                }
                ImGui.TextDisabled($"Login file: {CodexAuth.AuthPath}");
                changed |= Combo("Model##sub", ProviderModels.Subscription, c.SubscriptionModel, v => c.SubscriptionModel = v);
                changed |= Combo("Thinking effort", Efforts, c.ReasoningEffort, v => c.ReasoningEffort = v);
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Lower is faster and uses less of your plan. \"low\" is plenty for chat.");
                ImGui.TextDisabled("Uses the same backend as Codex. Not an official API for other apps.");
                break;
            case ProviderKind.OpenAiApiKey:
                changed |= ApiKeyInput("API key##openai", c.ApiKey, v => c.ApiKey = v);
                changed |= Combo("Model##api", ProviderModels.OpenAi, c.ApiModel, v => c.ApiModel = v);
                break;
            case ProviderKind.Claude:
                changed |= ApiKeyInput("API key##claude", c.ClaudeApiKey, v => c.ClaudeApiKey = v);
                changed |= Combo("Model##claude", ProviderModels.Claude, c.ClaudeModel, v => c.ClaudeModel = v);
                break;
            case ProviderKind.Gemini:
                changed |= ApiKeyInput("API key##gemini", c.GeminiApiKey, v => c.GeminiApiKey = v);
                changed |= Combo("Model##gemini", ProviderModels.Gemini, c.GeminiModel, v => c.GeminiModel = v);
                break;
        }
        if (c.Provider != ProviderKind.ChatGptSubscription)
        {
            ImGui.TextDisabled("Models are listed cheapest first.");
            ImGui.TextDisabled("Your key is saved in plain text in the Dalamud plugin config.");
        }

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
        return changed;
    }

    private static bool DrawLanguages(Configuration c)
    {
        var changed = false;
        Heading("Languages");
        changed |= Combo("Chat I read", Language.All, c.IncomingTarget, v => c.IncomingTarget = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Other players' messages are translated into this language.");
        changed |= Combo("Messages I send", Language.All, c.OutgoingTarget, v => c.OutgoingTarget = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("What you type in the Konnyaku Jelly window or with /jelly is translated into this language.");

        ImGui.Spacing();
        Heading("Custom prompt");
        ImGui.TextWrapped("Extra instructions added to the built-in FFXIV translation rules.");
        var prompt = c.CustomPrompt;
        if (ImGui.InputTextMultiline("##customprompt", ref prompt, 2000, new Vector2(-1, 90)))
        {
            c.CustomPrompt = prompt;
            changed = true;
        }
        ImGui.TextDisabled("e.g. \"Keep 'gg', 'o/' and party-finder shorthand as-is. Use casual Japanese.\"");
        return changed;
    }

    private static bool DrawChat(Configuration c)
    {
        var changed = false;
        Heading("Chat");
        changed |= Checkbox("Only translate chat while the Konnyaku Jelly window is open", c.TranslateOnlyWhenOpen, v => c.TranslateOnlyWhenOpen = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Saves tokens: messages that arrive while the window is closed are not translated. Open it with /jelly.");
        changed |= Checkbox("Skip my own messages", c.SkipOwnMessages, v => c.SkipOwnMessages = v);
        changed |= Checkbox("Show the original text under each translation", c.ShowOriginal, v => c.ShowOriginal = v);
        changed |= Checkbox("Also echo translations into game chat", c.EchoToGameChat, v => c.EchoToGameChat = v);

        ImGui.Spacing();
        Heading("Channels to translate");
        ImGui.TextDisabled("Only Party and Alliance are on by default. Shout and Yell in busy cities burn through your quota.");
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
        changed |= Checkbox("Jelly tab on the left edge of the screen", c.ShowEdgeTab, v => c.ShowEdgeTab = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("A small vertical tab on the left edge of the screen. Click it to open or hide the Konnyaku Jelly window.");
        changed |= Checkbox("Open the window as a drawer from the left edge", c.DockToEdge, v => c.DockToEdge = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("The window slides out from the left edge, level with the Jelly tab. Turn off to move the window anywhere.");
        var max = c.MaxLogEntries;
        ImGui.SetNextItemWidth(FieldWidth);
        if (ImGui.SliderInt("Lines kept in the log", ref max, 50, 1000)) { c.MaxLogEntries = max; changed = true; }
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

    private static bool ApiKeyInput(string label, string value, Action<string> set)
    {
        ImGui.SetNextItemWidth(FieldWidth);
        if (!ImGui.InputTextWithHint(label, "paste your key here", ref value, 256, ImGuiInputTextFlags.Password)) return false;
        set(value);
        return true;
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
