using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Konyakku.Translation;

namespace Konyakku.Windows;

internal sealed class ConfigWindow : Window
{
    private static readonly string[] Efforts = ["none", "minimal", "low", "medium", "high"];

    private readonly Plugin plugin;
    private string testResult = string.Empty;
    private bool testing;

    public ConfigWindow(Plugin plugin) : base("Konyakku Settings###KonyakkuConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(460, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = plugin.Config;
        var changed = false;

        if (ImGui.CollapsingHeader("Provider", ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= Radio("ChatGPT subscription (Plus / Pro)", ProviderKind.ChatGptSubscription, c);
            changed |= Radio("OpenAI API key (pay per use)", ProviderKind.OpenAiApiKey, c);
            changed |= Radio("Claude API key (pay per use)", ProviderKind.Claude, c);
            changed |= Radio("Gemini API key (pay per use)", ProviderKind.Gemini, c);
            ImGui.Spacing();

            switch (c.Provider)
            {
                case ProviderKind.ChatGptSubscription:
                    if (CodexAuth.Exists)
                        ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), "ChatGPT login found (Codex CLI).");
                    else
                        ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "Not logged in. Install Codex CLI and run: codex login");
                    ImGui.TextWrapped($"Login file: {CodexAuth.AuthPath}");
                    ImGui.TextWrapped("Uses the Codex backend with your ChatGPT plan's limits. This is not an official OpenAI API for third-party apps.");
                    changed |= Combo("Model##sub", ProviderModels.Subscription, c.SubscriptionModel, v => c.SubscriptionModel = v);
                    changed |= Combo("Reasoning effort", Efforts, c.ReasoningEffort, v => c.ReasoningEffort = v);
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
                ImGui.TextWrapped("Models are listed cheapest first. The key is stored in plain text in the Dalamud plugin config.");

            ImGui.BeginDisabled(testing);
            if (ImGui.Button("Test connection")) RunTest();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextWrapped(testResult);
        }

        if (ImGui.CollapsingHeader("Languages", ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= Combo("Translate chat into", Language.All, c.IncomingTarget, v => c.IncomingTarget = v);
            changed |= Combo("My messages into", Language.All, c.OutgoingTarget, v => c.OutgoingTarget = v);

            ImGui.Text("Custom prompt (added to the FFXIV translation instructions):");
            var prompt = c.CustomPrompt;
            if (ImGui.InputTextMultiline("##customprompt", ref prompt, 2000, new Vector2(-1, 80)))
            {
                c.CustomPrompt = prompt;
                changed = true;
            }
            ImGui.TextWrapped("e.g. \"Keep 'gg', 'o/' and party-finder shorthand as-is. Use casual Japanese.\"");
        }

        if (ImGui.CollapsingHeader("Chat", ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= Checkbox("Skip my own messages", c.SkipOwnMessages, v => c.SkipOwnMessages = v);
            changed |= Checkbox("Show original text", c.ShowOriginal, v => c.ShowOriginal = v);
            changed |= Checkbox("Also echo into game chat", c.EchoToGameChat, v => c.EchoToGameChat = v);
            ImGui.Text("Channels to translate:");
            var i = 0;
            foreach (var (type, name) in ChannelNames.Selectable)
            {
                var on = c.Channels.Contains(type);
                if (ImGui.Checkbox(name, ref on))
                {
                    if (on) c.Channels.Add(type); else c.Channels.Remove(type);
                    changed = true;
                }
                if (++i % 4 != 0) ImGui.SameLine(i % 4 * 105);
            }
            ImGui.NewLine();
        }

        if (ImGui.CollapsingHeader("Display"))
        {
            var max = c.MaxLogEntries;
            if (ImGui.SliderInt("Log size", ref max, 50, 1000)) { c.MaxLogEntries = max; changed = true; }
        }

        if (changed) c.Save();
    }

    private void RunTest()
    {
        testing = true;
        testResult = "Testing…";
        Task.Run(async () =>
        {
            try
            {
                var r = await plugin.Translator.TranslateAsync("Thanks for the run, gg! See you next time o/", plugin.Config.IncomingTarget);
                testResult = $"OK: {r}";
            }
            catch (Exception ex)
            {
                testResult = $"Failed: {ex.Message}";
            }
            finally
            {
                testing = false;
            }
        });
    }

    private static bool InputString(string label, string value, Action<string> set)
    {
        if (!ImGui.InputText(label, ref value, 512)) return false;
        set(value.Trim());
        return true;
    }

    private static bool Radio(string label, ProviderKind kind, Configuration c)
    {
        if (!ImGui.RadioButton(label, c.Provider == kind)) return false;
        c.Provider = kind;
        return true;
    }

    private static bool ApiKeyInput(string label, string value, Action<string> set)
    {
        if (!ImGui.InputText(label, ref value, 256, ImGuiInputTextFlags.Password)) return false;
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
