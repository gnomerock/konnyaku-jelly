using System;
using System.Collections.Generic;
using System.Linq;
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
    private string newJa = string.Empty;
    private string newEn = string.Empty;
    // Collected words the player unticked for sharing.
    private readonly HashSet<string> notShared = [];
    private bool sharing;
    private bool shareOk;
    private string shareResult = string.Empty;
    private const string SharePopup = "Share words with everyone###KonyakkuShare";

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
            if (ImGui.BeginTabItem("  Party Finder  ")) { changed |= DrawPartyFinder(c); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("  Words  ")) { changed |= DrawWords(c); ImGui.EndTabItem(); }
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
        changed |= Combo("Chat I read", Language.Incoming, c.IncomingTarget, v => c.IncomingTarget = v);
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
        if (c.EchoToGameChat && c.IncomingTarget == "Thai")
            ImGui.TextColored(Theme.Warn, "Thai isn't echoed: the game's chat font has no Thai letters. Read it in the Konnyaku Jelly window.");
        changed |= Checkbox("Translate button next to the game's chat box", c.ShowChatBoxButton, v => c.ShowChatBoxButton = v);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Type in the game's chat box, then click the button: what you typed is replaced with its translation (Write in), ready to send with Enter. A channel command such as /p stays as typed.");

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

    private static bool DrawPartyFinder(Configuration c)
    {
        var changed = false;
        Heading("Party Finder");
        ImGui.TextWrapped("The Party Finder page lists the listings the game has loaded. Make filters on the Filters page.");
        var minutes = c.ForgetAfterMinutes;
        ImGui.SetNextItemWidth(FieldWidth);
        if (ImGui.SliderInt("Forget after (minutes)", ref minutes, 1, 120)) { c.ForgetAfterMinutes = minutes; changed = true; }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Listings the game hasn't sent again for this long are dropped. Expired listings are always dropped.");
        return changed;
    }

    private bool DrawWords(Configuration c)
    {
        var changed = false;
        var terms = plugin.Terms;
        var glossary = plugin.Glossary;
        using var font = plugin.ChatFont.Push();

        Heading("FFXIV glossary");
        ImGui.TextWrapped("Japanese messages are sent to the AI with the meanings of the FFXIV words in them, from the shared glossary and your own words below.");
        ImGui.TextDisabled($"Shared glossary: {glossary.Count} words, version {glossary.Version}, {(glossary.Downloaded ? "from GitHub" : "built in")}.");
        if (glossary.LastError != null)
        {
            ImGui.SameLine();
            ImGuiComponents.HelpMarker($"Couldn't download the latest glossary, using the saved copy.\n{glossary.LastError}");
        }

        ImGui.Spacing();
        ImGui.TextColored(Theme.Yellow, "My words");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Your meaning replaces the shared one for the same Japanese word.");
        ImGui.SetNextItemWidth(110);
        ImGui.InputTextWithHint("##myja", "散開", ref newJa, 40);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(FieldWidth);
        ImGui.InputTextWithHint("##myen", "spread", ref newEn, 120);
        ImGui.SameLine();
        ImGui.BeginDisabled(newJa.Trim().Length == 0 || newEn.Trim().Length == 0);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add"))
        {
            glossary.SetMine(newJa, newEn);
            newJa = newEn = string.Empty;
        }
        ImGui.EndDisabled();

        var mine = glossary.Mine();
        if (mine.Count > 0 && ImGui.BeginTable("##mine", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH, new Vector2(-1, Math.Min(140, 26 * (mine.Count + 1)))))
        {
            ImGui.TableSetupColumn("##ja", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("##en", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed, 30);
            foreach (var t in mine)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(t.Ja);
                ImGui.TableNextColumn();
                ImGui.TextWrapped(t.En);
                ImGui.TableNextColumn();
                if (Theme.IconButton($"##mydel{t.Ja}", FontAwesomeIcon.Times, "Delete this word")) glossary.RemoveMine(t.Ja);
            }
            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Heading("Collect FFXIV words");
        ImGui.TextWrapped("Japanese players use lots of game slang (散開, 消化, 野良…). Turn this on to keep a list of the words the AI spots in Japanese chat you receive, to help build a shared FFXIV glossary.");
        changed |= Checkbox("I agree: collect FFXIV words from Japanese chat I receive", c.CollectTerms, v => c.CollectTerms = v);

        ImGui.Spacing();
        ImGui.TextColored(Theme.Ok, "Kept: single words and their meaning, and how often they were seen.");
        ImGui.TextColored(Theme.Ok, "Only from Say, Shout, Yell, Party, Alliance, Novice Network and Party Finder.");
        ImGui.TextColored(Theme.Error, "Never kept: what you type, whole messages, names, worlds, channels or times.");
        ImGui.TextColored(Theme.Error, "Never from Tells, Free Company, Linkshells or PvP teams.");
        ImGui.TextDisabled("The list stays on this PC. Words are only sent when you press Share and confirm.");
        ImGui.TextDisabled("Asking for words makes each Japanese translation reply a little longer.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var list = terms.Snapshot();
        ImGui.TextColored(Theme.Yellow, $"Collected words ({list.Count}/{TermCollector.MaxTerms})");
        ImGui.SameLine();
        ImGui.BeginDisabled(list.Count == 0);
        var toShare = list.Where(t => !t.Shared && !notShared.Contains(t.Term)).ToArray();
        ImGui.BeginDisabled(toShare.Length == 0 || sharing);
        if (Theme.PrimaryButton(FontAwesomeIcon.ShareAlt, sharing ? Theme.Dots("Sharing") : $"Share {toShare.Length}"))
        {
            shareResult = string.Empty;
            ImGui.OpenPopup(SharePopup);
        }
        ImGui.EndDisabled();
        Theme.Tooltip("Send the ticked words to the shared FFXIV glossary, after a preview");
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, "Copy list")) plugin.CopyToClipboard(terms.ToJson());
        Theme.Tooltip("Copy the list as JSON");
        ImGui.SameLine();
        var ctrl = ImGui.GetIO().KeyCtrl;
        ImGui.BeginDisabled(!ctrl);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "Clear all")) terms.Clear();
        ImGui.EndDisabled();
        Theme.Tooltip("Hold Ctrl and click to delete every word");
        ImGui.EndDisabled();
        DrawSharePopup(toShare);
        if (shareResult.Length > 0) ImGui.TextColored(shareOk ? Theme.Ok : Theme.Error, shareResult);

        if (list.Count == 0)
        {
            ImGui.TextDisabled(c.CollectTerms ? "No words yet. They appear as Japanese chat is translated." : "Collecting is off.");
            return changed;
        }

        if (ImGui.BeginTable("##terms", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH, new Vector2(-1, Math.Max(160, ImGui.GetContentRegionAvail().Y))))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthFixed, 44);
            ImGui.TableSetupColumn("Word", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("Meaning", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Seen", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("##mine", ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed, 30);
            ImGui.TableHeadersRow();
            foreach (var t in list.OrderBy(t => t.Shared).ThenByDescending(t => t.Seen))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (t.Shared)
                {
                    ImGui.TextDisabled("sent");
                }
                else
                {
                    var on = !notShared.Contains(t.Term);
                    if (ImGui.Checkbox($"##share{t.Term}", ref on))
                    {
                        if (on) notShared.Remove(t.Term); else notShared.Add(t.Term);
                    }
                    Theme.Tooltip("Include this word when you share");
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(t.Term);
                ImGui.TableNextColumn();
                ImGui.TextWrapped(t.Meaning);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(t.Seen.ToString());
                ImGui.TableNextColumn();
                if (Theme.IconButton($"##keep{t.Term}", FontAwesomeIcon.Plus, "Add to my words (fix the meaning there if it's wrong)")) glossary.SetMine(t.Term, t.Meaning);
                ImGui.TableNextColumn();
                if (Theme.IconButton($"##del{t.Term}", FontAwesomeIcon.Times, "Delete this word")) terms.Remove(t.Term);
            }
            ImGui.EndTable();
        }
        return changed;
    }

    /// <summary>Shows exactly what will be sent before anything leaves the PC.</summary>
    private void DrawSharePopup(CollectedTerm[] words)
    {
        ImGui.SetNextWindowSize(new Vector2(420, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(SharePopup)) return;

        ImGui.TextWrapped($"These {words.Length} words and meanings will be sent to the Konnyaku Jelly glossary server. When several players share the same word, it's reviewed and added to the glossary everyone uses.");
        ImGui.Spacing();
        if (ImGui.BeginChild("##sharelist", new Vector2(-1, Math.Min(200, 24 * words.Length + 8)), true))
        {
            foreach (var w in words) ImGui.TextWrapped($"{w.Term} = {w.Meaning}");
        }
        ImGui.EndChild();
        ImGui.Spacing();
        ImGui.TextColored(Theme.Ok, "Sent: only these words, their meanings, and a random ID for this install.");
        ImGui.TextColored(Theme.Error, "Not sent: messages, names, characters, worlds, channels, times or counts.");
        ImGui.TextDisabled("The server doesn't keep your IP address.");
        ImGui.Spacing();

        if (Theme.PrimaryButton(FontAwesomeIcon.ShareAlt, "Send"))
        {
            Share(words);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    // Called on the draw thread, so ShareAsync creates and saves the install ID here before its first await.
    private void Share(CollectedTerm[] words)
    {
        sharing = true;
        shareResult = string.Empty;
        _ = plugin.Glossary.ShareAsync(words, plugin.Terms.MarkShared).ContinueWith(t =>
        {
            shareOk = !t.IsFaulted;
            shareResult = t.IsFaulted
                ? $"Couldn't share: {t.Exception?.GetBaseException().Message}"
                : $"Thanks! {t.Result} words shared." + (t.Result < words.Length ? $" {words.Length - t.Result} didn't pass the server's checks." : "");
            sharing = false;
        });
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
        ImGuiComponents.HelpMarker("A small vertical tab that opens or hides the Konnyaku Jelly window. Drag it along the left or right edge of the screen to move it, or drag it out onto the screen to turn it into a normal window there.");
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
