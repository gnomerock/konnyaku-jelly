using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Konyakku;

/// <summary>Adds "Translate" to the Party Finder right-click menu and echoes the description in English.</summary>
internal sealed class PartyFinderTranslator : IDisposable
{
    private const string Target = "English";

    private readonly Plugin plugin;
    private readonly IContextMenu contextMenu;
    private readonly IPartyFinderGui partyFinderGui;

    // Listing descriptions by leader content id, filled as the Party Finder list loads.
    private readonly ConcurrentDictionary<ulong, string> descriptions = new();

    public PartyFinderTranslator(Plugin plugin, IContextMenu contextMenu, IPartyFinderGui partyFinderGui)
    {
        this.plugin = plugin;
        this.contextMenu = contextMenu;
        this.partyFinderGui = partyFinderGui;
        partyFinderGui.ReceiveListing += OnReceiveListing;
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose()
    {
        contextMenu.OnMenuOpened -= OnMenuOpened;
        partyFinderGui.ReceiveListing -= OnReceiveListing;
    }

    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        if (descriptions.Count > 2000) descriptions.Clear();
        descriptions[listing.ContentId] = listing.Description.TextValue;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.AddonName is not ("LookingForGroup" or "LookingForGroupDetail")) return;

        var description = FindDescription(args);
        if (string.IsNullOrWhiteSpace(description)) return;

        args.AddMenuItem(new MenuItem
        {
            Name = "Translate",
            PrefixChar = 'K',
            PrefixColor = 45,
            OnClicked = _ => Task.Run(() => TranslateAsync(description.Trim())),
        });
    }

    private unsafe string? FindDescription(IMenuOpenedArgs args)
    {
        var agent = AgentLookingForGroup.Instance();
        var lastViewed = agent != null ? agent->LastViewedListing.CommentString : null;

        // The detail window shows the listing that was opened last.
        if (args.AddonName == "LookingForGroupDetail") return lastViewed;

        if (args.Target is MenuTargetDefault { TargetContentId: not 0 } target)
        {
            if (descriptions.TryGetValue(target.TargetContentId, out var cached)) return cached;
            if (agent != null && agent->LastViewedListing.LeaderContentId == target.TargetContentId) return lastViewed;
        }
        return null;
    }

    private async Task TranslateAsync(string description)
    {
        SeString message;
        try
        {
            var result = await plugin.Translator.TranslateAsync(description, Target);
            plugin.AddLog(new LogEntry(DateTime.Now, "PF", "Party Finder", description, result ?? description, false));
            message = new SeStringBuilder()
                .AddUiForeground("[KY PF] ", 45)
                .AddText(result ?? $"(already in {Target}) {description}")
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Plugin.Log.Warning(ex, "Party Finder translation failed");
            message = new SeStringBuilder().AddUiForeground($"[KY PF] Translation failed: {ex.Message}", 17).Build();
        }

        await Plugin.Framework.RunOnFrameworkThread(() =>
            Plugin.ChatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = message }));
    }
}
