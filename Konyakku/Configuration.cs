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
    public int Version { get; set; } = 2;

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
    public string CustomPrompt { get; set; } = string.Empty;

    public string IncomingTarget { get; set; } = "English";
    public string OutgoingTarget { get; set; } = "Japanese";

    public HashSet<XivChatType> Channels { get; set; } =
    [
        XivChatType.Say, XivChatType.Party, XivChatType.CrossParty, XivChatType.Alliance,
        XivChatType.FreeCompany, XivChatType.TellIncoming, XivChatType.NoviceNetwork,
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
        XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3,
        XivChatType.CrossLinkShell4, XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6,
        XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];

    public bool SkipOwnMessages { get; set; } = true;
    public bool ShowOriginal { get; set; } = true;

    public bool EchoToGameChat { get; set; } = false;

    public int MaxLogEntries { get; set; } = 300;
    public string SendPrefix { get; set; } = "/p";

    /// <summary>Moves saved model names that are no longer offered back to the cheapest allowed model.</summary>
    public void Normalize()
    {
        SubscriptionModel = ProviderModels.Allowed(ProviderModels.Subscription, SubscriptionModel);
        ApiModel = ProviderModels.Allowed(ProviderModels.OpenAi, ApiModel);
        ClaudeModel = ProviderModels.Allowed(ProviderModels.Claude, ClaudeModel);
        GeminiModel = ProviderModels.Allowed(ProviderModels.Gemini, GeminiModel);
        if (Array.IndexOf(Language.All, IncomingTarget) < 0) IncomingTarget = "English";
        if (Array.IndexOf(Language.All, OutgoingTarget) < 0) OutgoingTarget = "Japanese";
        Version = 2;
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
