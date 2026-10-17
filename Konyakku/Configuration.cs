using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Game.Text;

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
    public int Version { get; set; } = 3;

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
    /// <summary>The slide-out "Jelly" tab on the left edge of the screen that toggles the window.</summary>
    public bool ShowEdgeTab { get; set; } = true;

    public bool EchoToGameChat { get; set; } = false;

    /// <summary>Only translate incoming chat while the Konnyaku Jelly window is open, to save tokens.</summary>
    public bool TranslateOnlyWhenOpen { get; set; } = true;

    public int MaxLogEntries { get; set; } = 300;
    public string SendPrefix { get; set; } = "/p";
    /// <summary>Name@World used when <see cref="SendPrefix"/> is a tell.</summary>
    public string TellTarget { get; set; } = string.Empty;

    /// <summary>Moves saved model names that are no longer offered back to the cheapest allowed model.</summary>
    public void Normalize()
    {
        SubscriptionModel = ProviderModels.Allowed(ProviderModels.Subscription, SubscriptionModel);
        ApiModel = ProviderModels.Allowed(ProviderModels.OpenAi, ApiModel);
        ClaudeModel = ProviderModels.Allowed(ProviderModels.Claude, ClaudeModel);
        GeminiModel = ProviderModels.Allowed(ProviderModels.Gemini, GeminiModel);
        if (Array.IndexOf(Language.All, IncomingTarget) < 0) IncomingTarget = "English";
        if (Array.IndexOf(Language.All, OutgoingTarget) < 0) OutgoingTarget = "Japanese";
        NormalizeSendPrefix();
        // Version 3 added a default custom prompt; give it to existing users who never wrote their own.
        if (Version < 3 && string.IsNullOrWhiteSpace(CustomPrompt)) CustomPrompt = DefaultCustomPrompt;
        Version = 3;
    }

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

    public void Save() =>Plugin.PluginInterface.SavePluginConfig(this);
}
