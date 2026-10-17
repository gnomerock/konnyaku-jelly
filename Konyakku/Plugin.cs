using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Konyakku.PartyFinder;
using Konyakku.Translation;
using Konyakku.Windows;

namespace Konyakku;

public sealed record LogEntry(DateTime Time, string Channel, string Sender, string Original, string Translation, bool Outgoing);

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IPartyFinderGui PartyFinderGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

    private const string MainCommand = "/konyakku";
    private const string TranslateCommand = "/jelly";
    private const string PartyFinderCommand = "/tazu";

    internal Configuration Config { get; }
    internal TranslationService Translator { get; }
    internal ListingStore Listings { get; }

    private readonly WindowSystem windows = new("Konyakku");
    private readonly ConfigWindow configWindow;
    private readonly LogWindow logWindow;
    private readonly EdgeTab edgeTab;
    private readonly PartyFinderTranslator partyFinder;
    private readonly object logLock = new();
    private readonly List<LogEntry> log = [];
    private string? pendingClipboard;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Normalize();
        Translator = new TranslationService(Config);
        Listings = new ListingStore(PartyFinderGui, DataManager);

        configWindow = new ConfigWindow(this);
        logWindow = new LogWindow(this);
        windows.AddWindow(configWindow);
        windows.AddWindow(logWindow);
        edgeTab = new EdgeTab(this);

        CommandManager.AddHandler(MainCommand, new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Toggle the translation window. \"/konyakku config\" opens settings.",
        });
        CommandManager.AddHandler(PartyFinderCommand, new CommandInfo(OnPartyFinderCommand)
        {
            HelpMessage = "Open the Party Finder page of the Konnyaku Jelly window. \"/tazu filters\" edits filters.",
        });
        CommandManager.AddHandler(TranslateCommand, new CommandInfo(OnTranslateCommand)
        {
            HelpMessage = "<text> — translate to your outgoing language and copy it. \"/jelly /p <text>\" translates and sends to party.",
        });

        partyFinder = new PartyFinderTranslator(this, ContextMenu, PartyFinderGui);
        ChatGui.ChatMessage += OnChatMessage;
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += logWindow.Toggle;
    }

    public void Dispose()
    {
        partyFinder.Dispose();
        Listings.Dispose();
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= logWindow.Toggle;
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(TranslateCommand);
        CommandManager.RemoveHandler(PartyFinderCommand);
        windows.RemoveAllWindows();
        Translator.Dispose();
    }

    /// <summary>Goes up on every new log entry, even once the log is full and its length stops changing.</summary>
    internal int LogVersion { get; private set; }

    internal IReadOnlyList<LogEntry> SnapshotLog()
    {
        lock (logLock) return log.ToArray();
    }

    internal void ClearLog()
    {
        lock (logLock) log.Clear();
    }

    internal void CopyToClipboard(string text) => pendingClipboard = text;

    internal void OpenConfig() => configWindow.IsOpen = true;

    internal bool IsMainWindowOpen => logWindow.IsOpen;

    internal void ToggleMainWindow() => logWindow.Toggle();

    private void DrawUi()
    {
        if (pendingClipboard is { } text)
        {
            ImGui.SetClipboardText(text);
            pendingClipboard = null;
        }
        windows.Draw();
        edgeTab.Draw();
    }

    private void OnMainCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config":
                configWindow.Toggle();
                break;
            case "pf":
                logWindow.Show(LogWindow.Page.Listings);
                break;
            case "filters":
                logWindow.Show(LogWindow.Page.Filters);
                break;
            default:
                logWindow.Toggle();
                break;
        }
    }

    private void OnPartyFinderCommand(string command, string args) =>
        logWindow.Show(args.Trim().Equals("filters", StringComparison.OrdinalIgnoreCase) ? LogWindow.Page.Filters : LogWindow.Page.Listings);

    private void OnTranslateCommand(string command, string args)
    {
        var text = args.Trim();
        if (text.Length == 0)
        {
            logWindow.IsOpen = true;
            return;
        }

        string? prefix = null;
        if (text.StartsWith('/'))
        {
            var space = text.IndexOf(' ');
            if (space < 0) return;
            prefix = text[..space];
            text = text[(space + 1)..].Trim();
        }
        _ = TranslateOutgoingAsync(text, prefix);
    }

    /// <summary>Translate my own text. With a channel prefix (e.g. "/p") the result is sent, otherwise copied.</summary>
    internal async Task<string?> TranslateOutgoingAsync(string text, string? sendPrefix)
    {
        try
        {
            var result = await Translator.TranslateAsync(text, Config.OutgoingTarget) ?? text;
            var channel = sendPrefix == null ? "me" : ChannelNames.OutgoingName(sendPrefix.Split(' ')[0]);
            AddLog(new LogEntry(DateTime.Now, channel, "me", text, result, true));
            if (sendPrefix != null) await Framework.RunOnFrameworkThread(() => ChatSender.Send($"{sendPrefix} {result}"));
            else CopyToClipboard(result);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Outgoing translation failed");
            ChatGui.PrintError($"[Konnyaku Jelly] {ex.Message}");
            return null;
        }
    }

    private void OnChatMessage(IHandleableChatMessage msg)
    {
        var type = msg.LogKind;
        if (!Config.Enabled || msg.IsHandled || !Config.Channels.Contains(type)) return;
        if (Config.TranslateOnlyWhenOpen && !logWindow.IsOpen) return;

        var text = msg.Message.TextValue.Trim();
        if (text.Length < 2 || text.All(c => !char.IsLetter(c))) return;

        var senderName = msg.Sender.TextValue;
        if (Config.SkipOwnMessages && ObjectTable.LocalPlayer?.Name.TextValue is { Length: > 0 } me && senderName.Contains(me)) return;
        if (Language.IsAlreadyIn(text, Config.IncomingTarget)) return;

        _ = TranslateIncomingAsync(type, senderName, text);
    }

    private async Task TranslateIncomingAsync(XivChatType type, string sender, string text)
    {
        try
        {
            var result = await Translator.TranslateAsync(text, Config.IncomingTarget);
            if (result == null) return;

            AddLog(new LogEntry(DateTime.Now, ChannelNames.Short(type), sender, text, result, false));
            if (Config.EchoToGameChat)
            {
                var se = new SeStringBuilder()
                    .AddUiForeground("[KY] ", 45)
                    .AddText($"{sender}: {result}")
                    .Build();
                await Framework.RunOnFrameworkThread(() => ChatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = se }));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Incoming translation failed");
        }
    }

    internal void AddLog(LogEntry entry)
    {
        lock (logLock)
        {
            log.Add(entry);
            var overflow = log.Count - Math.Max(20, Config.MaxLogEntries);
            if (overflow > 0) log.RemoveRange(0, overflow);
            LogVersion++;
        }
    }
}
