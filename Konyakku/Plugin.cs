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

    private const string MainCommand = "/konyakku";
    private const string TranslateCommand = "/jelly";

    internal Configuration Config { get; }
    internal TranslationService Translator { get; }

    private readonly WindowSystem windows = new("Konyakku");
    private readonly ConfigWindow configWindow;
    private readonly LogWindow logWindow;
    private readonly PartyFinderTranslator partyFinder;
    private readonly object logLock = new();
    private readonly List<LogEntry> log = [];
    private string? pendingClipboard;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Normalize();
        Translator = new TranslationService(Config);

        configWindow = new ConfigWindow(this);
        logWindow = new LogWindow(this);
        windows.AddWindow(configWindow);
        windows.AddWindow(logWindow);

        CommandManager.AddHandler(MainCommand, new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Toggle the translation window. \"/konyakku config\" opens settings.",
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
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= logWindow.Toggle;
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(TranslateCommand);
        windows.RemoveAllWindows();
        Translator.Dispose();
    }

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

    private void DrawUi()
    {
        if (pendingClipboard is { } text)
        {
            ImGui.SetClipboardText(text);
            pendingClipboard = null;
        }
        windows.Draw();
    }

    private void OnMainCommand(string command, string args)
    {
        if (args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase)) configWindow.Toggle();
        else logWindow.Toggle();
    }

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
            AddLog(new LogEntry(DateTime.Now, sendPrefix ?? "me", "me", text, result, true));
            if (sendPrefix != null) await Framework.RunOnFrameworkThread(() => ChatSender.Send($"{sendPrefix} {result}"));
            else CopyToClipboard(result);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Outgoing translation failed");
            ChatGui.PrintError($"[Konyakku] {ex.Message}");
            return null;
        }
    }

    private void OnChatMessage(IHandleableChatMessage msg)
    {
        var type = msg.LogKind;
        if (!Config.Enabled || msg.IsHandled || !Config.Channels.Contains(type)) return;

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
        }
    }
}
