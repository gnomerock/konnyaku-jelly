using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Konyakku.Translation;

namespace Konyakku.Windows;

internal sealed class ConfigWindow : Window
{
    private static readonly string[] Languages =
        ["Thai", "English", "Japanese", "Korean", "Chinese (Simplified)", "Chinese (Traditional)", "French", "German", "Spanish", "Vietnamese", "Indonesian"];
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
        using var font = plugin.ThaiFont.Push();
        var c = plugin.Config;
        var changed = false;

        if (ImGui.CollapsingHeader("Provider", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var sub = c.Provider == ProviderKind.ChatGptSubscription;
            if (ImGui.RadioButton("ChatGPT subscription (Plus / Pro)", sub)) { c.Provider = ProviderKind.ChatGptSubscription; changed = true; }
            if (ImGui.RadioButton("OpenAI API key (pay per use)", !sub)) { c.Provider = ProviderKind.OpenAiApiKey; changed = true; }
            ImGui.Spacing();

            if (c.Provider == ProviderKind.ChatGptSubscription)
            {
                if (CodexAuth.Exists)
                    ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), "ChatGPT login found (Codex CLI).");
                else
                    ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "Not logged in. Install Codex CLI and run: codex login");
                ImGui.TextWrapped($"Login file: {CodexAuth.AuthPath}");
                ImGui.TextWrapped("Uses the Codex backend with your ChatGPT plan's limits. This is not an official OpenAI API for third-party apps.");
                changed |= InputString("Model##sub", c.SubscriptionModel, v => c.SubscriptionModel = v);
                changed |= Combo("Reasoning effort", Efforts, c.ReasoningEffort, v => c.ReasoningEffort = v);
            }
            else
            {
                var key = c.ApiKey;
                if (ImGui.InputText("API key", ref key, 256, ImGuiInputTextFlags.Password)) { c.ApiKey = key; changed = true; }
                changed |= InputString("Model##api", c.ApiModel, v => c.ApiModel = v);
                ImGui.TextWrapped("The key is stored in plain text in the Dalamud plugin config.");
            }

            ImGui.BeginDisabled(testing);
            if (ImGui.Button("Test connection")) RunTest();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextWrapped(testResult);
        }

        if (ImGui.CollapsingHeader("Languages", ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= Combo("Translate chat into", Languages, c.IncomingTarget, v => c.IncomingTarget = v);
            changed |= Combo("My messages into", Languages, c.OutgoingTarget, v => c.OutgoingTarget = v);
        }

        if (ImGui.CollapsingHeader("Chat", ImGuiTreeNodeFlags.DefaultOpen))
        {
            changed |= Checkbox("Skip my own messages", c.SkipOwnMessages, v => c.SkipOwnMessages = v);
            changed |= Checkbox("Show original text", c.ShowOriginal, v => c.ShowOriginal = v);
            changed |= Checkbox("Also echo into game chat (cannot display Thai)", c.EchoToGameChat, v => c.EchoToGameChat = v);
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
            changed |= InputString("Thai font file", c.FontPath, v => c.FontPath = v);
            ImGui.TextWrapped("Font changes apply after reloading the plugin.");
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
