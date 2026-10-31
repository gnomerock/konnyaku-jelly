using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Game.Text;
using Konyakku.Translation;

namespace Konyakku;

public enum ProviderKind
{
    /// <summary>ChatGPT Plus/Pro subscription, using the login created by `codex login`.</summary>
    ChatGptSubscription,
    /// <summary>Pay-as-you-go OpenAI API key.</summary>
    OpenAiApiKey,
    /// <summary>Anthropic Claude API key.</summary>
    Claude,
    /// <summary>Google Gemini API key.</summary>
    Gemini,
}

/// <summary>Where the Jelly tab sits: stuck to a screen edge, or floating where it was dropped.</summary>
public enum TabEdge { Left, Right }

/// <summary>Models offered in settings for each provider. The first entry is the cheapest and is the default.</summary>
public static class ProviderModels
{
    public static readonly string[] Subscription = ["gpt-6-luna", "gpt-6-sol", "gpt-6.1-sol", "gpt-6-astra"];
    public static readonly string[] OpenAi = ["gpt-5-nano", "gpt-6-luna", "gpt-5.6-luna", "gpt-5-mini", "gpt-6-sol", "gpt-6.1-sol"];
    public static readonly string[] Claude = ["claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5"];
    public static readonly string[] Gemini = ["gemini-2.5-flash-lite", "gemini-3.1-flash-lite", "gemini-3.5-flash-lite", "gemini-2.5-flash", "gemini-3.8-flash"];

    public static string Allowed(string[] models, string current) => Array.IndexOf(models, current) >= 0 ? current : models[0];
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 6;

    public const int MaxDmuDelayMs = 2000;

    public const string DefaultCustomPrompt = "Used in FFXIV online communication and raiding contexts.";

    public bool Enabled { get; set; } = true;
    public ProviderKind Provider { get; set; } = ProviderKind.ChatGptSubscription;

    // ChatGPT subscription (Codex backend)
    public string SubscriptionModel { get; set; } = ProviderModels.Subscription[0];
    public string ReasoningEffort { get; set; } = "low";

    // OpenAI API key
    public string ApiKey { get; set; } = string.Empty;
    public string ApiModel { get; set; } = ProviderModels.OpenAi[0];

    // Anthropic Claude API key
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string ClaudeModel { get; set; } = ProviderModels.Claude[0];

    // Google Gemini API key
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = ProviderModels.Gemini[0];

    /// <summary>Extra instructions added to the translation prompt (slang, names, tone…).</summary>
    public string CustomPrompt { get; set; } = DefaultCustomPrompt;

    public string IncomingTarget { get; set; } = "English";
    public string OutgoingTarget { get; set; } = "Japanese";

    /// <summary>Party (incl. cross-world) and alliance raid chat by default; players opt in to the rest.</summary>
    public HashSet<XivChatType> Channels { get; set; } =
    [
        XivChatType.Party, XivChatType.CrossParty, XivChatType.Alliance,
    ];

    public bool SkipOwnMessages { get; set; } = true;
    public bool ShowOriginal { get; set; } = true;
    public bool UseTheme { get; set; } = true;
    public bool SidebarCollapsed { get; set; } = false;
    /// <summary>The vertical "Jelly" tab that opens and hides the window. It can be dragged along either screen edge.</summary>
    public bool ShowEdgeTab { get; set; } = true;
    /// <summary>Which screen edge the Jelly tab sticks to.</summary>
    public TabEdge TabEdge { get; set; } = TabEdge.Left;
    /// <summary>The tab's top as a fraction of the screen height.</summary>
    public float TabY { get; set; } = 0.18f;
    /// <summary>The tab was dragged off its edge, so the window is a normal window rather than a drawer. Dragging the
    /// window (or the tab) back to an edge docks it again.</summary>
    public bool Detached { get; set; } = false;
    /// <summary>Open the window as a drawer pinned to the Jelly tab's edge, level with the tab.</summary>
    public bool DockToEdge { get; set; } = true;

    public bool EchoToGameChat { get; set; } = false;

    /// <summary>A translate button next to the game's chat input that rewrites what you typed in your outgoing language.</summary>
    public bool ShowChatBoxButton { get; set; } = true;

    /// <summary>Only translate incoming chat while the Konyaku Jelly window is open, to save tokens.</summary>
    public bool TranslateOnlyWhenOpen { get; set; } = true;

    /// <summary>Opt-in: keep FFXIV words the AI spots in received Japanese chat (public channels only) in a local list.</summary>
    public bool CollectTerms { get; set; } = false;

    /// <summary>Random ID sent with shared words, so the server counts each player once. Not linked to the character or account.</summary>
    public string InstallId { get; set; } = string.Empty;

    /// <summary>The player's own glossary words. They replace shared glossary entries with the same Japanese.</summary>
    public List<GlossaryTerm> MyTerms { get; set; } = [];

    /// <summary>Master switch for every Dancing Mad (Ultimate) feature: marks and overlay.</summary>
    public bool DmuEnabled { get; set; } = true;
    /// <summary>Legacy (0.7.6): one switch for both DMU marks. Read once by <see cref="Normalize"/>.</summary>
    public bool DmuMarks { get; set; } = true;
    /// <summary>Dancing Mad (Ultimate): mark yourself with these commands. A command that doesn't start with "/" is skipped.</summary>
    public bool DmuP2Marks { get; set; } = true;
    public bool DmuP3Marks { get; set; } = true;
    /// <summary>Milliseconds to wait before sending a DMU mark.</summary>
    public int DmuMarkDelayMs { get; set; } = 500;
    /// <summary>Wait a random time between <see cref="DmuDelayMinMs"/> and <see cref="DmuDelayMaxMs"/> instead.</summary>
    public bool DmuHumanize { get; set; } = false;
    public int DmuDelayMinMs { get; set; } = 300;
    public int DmuDelayMaxMs { get; set; } = 800;
    /// <summary>P2 Path of Light: the headmarker you get for soaking the 3rd tower.</summary>
    public string DmuP2Circle { get; set; } = "/mk ignore <me>";
    public string DmuP2Cone { get; set; } = "/mk bind <me>";
    /// <summary>P3 Black Hole: First, Second and Third in Line (3, 3 and 2 players).</summary>
    public string DmuP3First { get; set; } = "/mk attack <me>";
    public string DmuP3Second { get; set; } = "/mk bind <me>";
    public string DmuP3Third { get; set; } = "/mk ignore <me>";
    /// <summary>Show which Black Hole tether to grab and when, in the overlay.</summary>
    public bool DmuP3Guide { get; set; } = true;
    /// <summary>"Standard" or "Fae" (Fae swaps the DPS and support tethers).</summary>
    public string DmuBlackHoleStrategy { get; set; } = "Standard";
    /// <summary>Show what your P4 debuffs ask of you, in a small overlay.</summary>
    public bool DmuGuide { get; set; } = true;

    public int MaxLogEntries { get; set; } = 300;
    public string SendPrefix { get; set; } = "/p";
    /// <summary>Name@World used when <see cref="SendPrefix"/> is a tell.</summary>
    public string TellTarget { get; set; } = string.Empty;

    /// <summary>Runs on every load: resets models no longer offered, migrates old settings.</summary>
    public void Normalize()
    {
        SubscriptionModel = ProviderModels.Allowed(ProviderModels.Subscription, SubscriptionModel);
        ApiModel = ProviderModels.Allowed(ProviderModels.OpenAi, ApiModel);
        ClaudeModel = ProviderModels.Allowed(ProviderModels.Claude, ClaudeModel);
        GeminiModel = ProviderModels.Allowed(ProviderModels.Gemini, GeminiModel);
        if (Array.IndexOf(Language.Incoming, IncomingTarget) < 0) IncomingTarget = "English";
        if (Array.IndexOf(Language.All, OutgoingTarget) < 0) OutgoingTarget = "Japanese";
        NormalizeSendPrefix();
        // Version 3 added a default custom prompt; give it to existing users who never wrote their own.
        if (Version < 3 && string.IsNullOrWhiteSpace(CustomPrompt)) CustomPrompt = DefaultCustomPrompt;
        // Version 4 had a floating tab (TabEdge 2); now the tab stays on an edge and the window detaches instead.
        if (!Enum.IsDefined(TabEdge))
        {
            TabEdge = TabEdge.Left;
            Detached = true;
        }
        TabY = float.IsFinite(TabY) ? Math.Clamp(TabY, 0f, 1f) : 0.18f;
        // Version 6 split the DMU marks into P2 and P3 switches.
        if (Version < 6 && !DmuMarks) DmuP2Marks = DmuP3Marks = false;
        if (DmuBlackHoleStrategy != "Fae") DmuBlackHoleStrategy = "Standard";
        // 0.7.6–0.7.9 defaulted to "/mk stop", which isn't a sign: the stop-sign marker is "ignore".
        DmuP2Circle = FixStopSign(DmuP2Circle);
        DmuP2Cone = FixStopSign(DmuP2Cone);
        DmuP3First = FixStopSign(DmuP3First);
        DmuP3Second = FixStopSign(DmuP3Second);
        DmuP3Third = FixStopSign(DmuP3Third);
        DmuMarkDelayMs = Math.Clamp(DmuMarkDelayMs, 0, MaxDmuDelayMs);
        DmuDelayMinMs = Math.Clamp(DmuDelayMinMs, 0, MaxDmuDelayMs);
        DmuDelayMaxMs = Math.Clamp(DmuDelayMaxMs, DmuDelayMinMs, MaxDmuDelayMs);
        Version = 6;
    }

    private static string FixStopSign(string command) => command.Replace("/mk stop", "/mk ignore", StringComparison.OrdinalIgnoreCase);

    /// <summary>Older versions stored free text such as "/t Name@World"; split it into a dropdown channel and a tell target.</summary>
    private void NormalizeSendPrefix()
    {
        var text = SendPrefix.Trim();
        var space = text.IndexOf(' ');
        var prefix = (space < 0 ? text : text[..space]).ToLowerInvariant();
        prefix = prefix switch
        {
            "/tell" => ChannelNames.TellPrefix,
            "/party" => "/p",
            "/say" => "/s",
            "/alliance" => "/a",
            "/freecompany" => "/fc",
            "/shout" => "/sh",
            "/yell" => "/y",
            _ => prefix,
        };
        if (prefix == ChannelNames.TellPrefix && space >= 0 && TellTarget.Length == 0) TellTarget = text[(space + 1)..].Trim();
        SendPrefix = Array.Exists(ChannelNames.Outgoing, c => c.Prefix == prefix) ? prefix : "/p";
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
