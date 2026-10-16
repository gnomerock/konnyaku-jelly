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
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;
    public ProviderKind Provider { get; set; } = ProviderKind.ChatGptSubscription;

    // ChatGPT subscription (Codex backend)
    public string SubscriptionModel { get; set; } = "gpt-5.1";
    public string ReasoningEffort { get; set; } = "low";

    // OpenAI API key
    public string ApiKey { get; set; } = string.Empty;
    public string ApiModel { get; set; } = "gpt-5-mini";

    public string IncomingTarget { get; set; } = "Thai";
    public string OutgoingTarget { get; set; } = "English";

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

    /// <summary>The game font cannot render Thai, so this is only useful for Latin/Japanese targets.</summary>
    public bool EchoToGameChat { get; set; } = false;

    public string FontPath { get; set; } = @"C:\Windows\Fonts\LeelawUI.ttf";
    public int MaxLogEntries { get; set; } = 300;
    public string SendPrefix { get; set; } = "/p";

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
