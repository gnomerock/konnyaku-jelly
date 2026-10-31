using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Hooking;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
using Lumina.Excel.Sheets;

namespace Konyakku;

/// <summary>A P4 tile: the short call, the full instruction and the colour of both. <paramref name="Id"/> is a status,
/// or an action when <paramref name="IsAction"/> (a boss cast).</summary>
internal sealed record Callout(uint Id, string Label, string Detail, Vector4 Color, float Remaining, bool IsAction = false);

/// <summary>
/// Dancing Mad (Ultimate) helper: marks yourself for P2 Path of Light and P3 Black Hole, and shows your Black Hole
/// tethers and P4 debuff calls in an overlay. IDs, timings and calls come from cactbot's dancing_mad.ts and
/// Nainaiowo's DMU Helper (MIT), whose callout tiles the overlay copies.
/// </summary>
internal sealed unsafe class DancingMad : IDisposable
{
    private const ushort Territory = 1363;
    // Kefka's tower resolution. The four soakers of tower N get new headmarkers ~0.5s after the Nth cast.
    private const uint PathOfLight = 0xBABE;
    private const uint HeadMarkerCategory = 34;
    private const uint SpreadMarker = 0x02CC, ConeMarker = 0x02CD;
    private const uint FirstInLine = 3004, ThirdInLine = 3006;
    // On Neo Exdeath and Chaos; the param says whether the debuffs just handed out are real or fake.
    private const uint BossTell = 2056;
    private static readonly Dictionary<ushort, bool> TellIsReal = new() { [1119] = false, [1120] = true, [1121] = false, [1122] = true };
    private static readonly TimeSpan TellFreshness = TimeSpan.FromSeconds(20);

    private const uint WhiteWound1 = 4887, BlackWound1 = 4888, WhiteWound2 = 5541, BlackWound2 = 5542;
    private const uint AllaganField = 454, BeyondDeathLive = 5464, BeyondDeathDie = 1382;
    private const uint DynamicFluid = 5548, Entropy = 5547, CursedShriek = 5543;
    // Chaos's casts; real/fake comes from Chaos's tell.
    private const uint Inferno = 0xBB20, Tsunami = 0xBB21;

    // P3 Black Hole: tether waves as seconds after the In Line debuffs, and who grabs which tether.
    // Role: D = DPS, S = support (swapped by the Fae strategy), A = had Accretion. Line 1-3 = First/Second/Third in Line.
    private const uint Accretion = 1604;
    private const double BlackHoleEnd = 134;
    private static readonly (int Set, int Wave, double Start, double End)[] Waves =
    [
        (1, 1, 0.0, 6.2), (1, 2, 6.2, 11.2),
        (2, 1, 51.9, 58.4), (2, 2, 58.4, 63.5), (2, 3, 63.5, 69.1),
        (3, 1, 86.2, 92.7), (3, 2, 92.7, 97.8), (3, 3, 97.8, 103.4),
        (4, 1, 119.6, 127.2), (4, 2, 127.2, 133.7),
    ];
    private static readonly (int Set, int Wave, int Line, char Role, string Action)[] Tethers =
    [
        (1, 1, 1, 'D', "grab the first and only tether clockwise from Kefka"),
        (1, 2, 1, 'D', "grab the new tether clockwise from Kefka"),
        (1, 2, 1, 'S', "grab the second and last tether clockwise from Kefka"),
        (2, 1, 1, 'D', "grab the first tether clockwise from Kefka"),
        (2, 1, 1, 'S', "grab the second tether clockwise from Kefka"),
        (2, 1, 1, 'A', "grab the third tether clockwise from Kefka"),
        (2, 2, 2, 'D', "grab the first tether clockwise from Kefka"),
        (2, 2, 1, 'S', "grab the second tether clockwise from Kefka"),
        (2, 2, 1, 'A', "grab the third tether clockwise from Kefka"),
        (2, 3, 2, 'D', "grab the first tether clockwise from Kefka"),
        (2, 3, 2, 'S', "grab the second tether clockwise from Kefka"),
        (2, 3, 1, 'A', "grab the third tether clockwise from Kefka"),
        (3, 1, 2, 'D', "grab the first tether clockwise from Kefka"),
        (3, 1, 2, 'S', "grab the second tether clockwise from Kefka"),
        (3, 1, 2, 'A', "grab the third tether clockwise from Kefka"),
        (3, 2, 3, 'D', "grab the first tether clockwise from Kefka"),
        (3, 2, 2, 'S', "grab the second tether clockwise from Kefka"),
        (3, 2, 2, 'A', "grab the third tether clockwise from Kefka"),
        (3, 3, 3, 'D', "grab the first tether clockwise from Kefka"),
        (3, 3, 3, 'S', "grab the second tether clockwise from Kefka"),
        (3, 3, 2, 'A', "grab the third tether clockwise from Kefka"),
        (4, 1, 3, 'D', "grab the first tether clockwise from Kefka"),
        (4, 1, 3, 'S', "grab the second tether clockwise from Kefka"),
        (4, 2, 3, 'S', "grab the only tether clockwise from Kefka"),
    ];

    // DMU Helper's palette.
    private static readonly Vector4 Gold = new(1.0f, 0.78f, 0.18f, 1.0f);
    private static readonly Vector4 RealColor = new(0.25f, 0.85f, 1.0f, 1.0f);
    private static readonly Vector4 FakeColor = new(1.0f, 0.28f, 0.22f, 1.0f);
    private static readonly Vector4 UnknownColor = new(0.75f, 0.75f, 0.75f, 1.0f);
    private static readonly Vector4 BlueSide = new(0.35f, 0.72f, 1.0f, 1.0f);
    private static readonly Vector4 PurpleSide = new(0.82f, 0.55f, 1.0f, 1.0f);
    private static readonly Vector4 TimerColor = new(0.94f, 0.98f, 1.0f, 1.0f);

    /// <summary>What the overlay draws: your In Line (and Accretion) icons with the Black Hole lines, then P4 tiles.</summary>
    private sealed record Overlay(uint[] Icons, List<string> BlackHole, List<Callout> Debuffs)
    {
        public static readonly Overlay Empty = new([], [], []);
        public bool IsEmpty => BlackHole.Count == 0 && Debuffs.Count == 0;
    }

    private readonly Plugin plugin;
    private readonly Hook<ActionEffectHandler.Delegates.Receive> effectHook;
    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket> actorControlHook;
    private readonly HashSet<uint> neoExdeathIds, chaosIds;
    private readonly Dictionary<(bool Action, uint Id), uint> icons = [];
    // Marks waiting for the delay. Filled from hooks and the settings window, sent from Framework.Update.
    private readonly List<(DateTime Due, string Command)> marks = [];
    // Real/fake per (player, debuff), caught when the debuff first shows.
    private readonly Dictionary<(uint Entity, uint Status), bool?> realities = [];
    private (uint Action, float Remaining)? chaosCast;
    private (bool Real, DateTime Seen)? neoTell, chaosTell;
    private int towersResolved;
    private DateTime lastTower;
    private uint markedLine;
    // shortcut: the Black Hole clock starts when *you* get In Line and follows a fixed timeline; DMU Helper also
    // recalibrates it from party-wide Primordial Crust / Earth Resistance pulses. Do that if waves drift in practice.
    private DateTime? blackHoleStart;
    private int myLine;
    private bool hadAccretion;
    private string? wound;
    private volatile Overlay overlay = Overlay.Empty;

    /// <summary>Show sample lines so the overlay can be placed. Set from the settings window.</summary>
    internal bool Preview { get; set; }

    public DancingMad(Plugin plugin)
    {
        this.plugin = plugin;
        // Bosses are matched by name ID, so this works on every client language.
        var names = Plugin.DataManager.GetExcelSheet<BNpcName>(ClientLanguage.English);
        HashSet<uint> Ids(string name) => names.Where(r => r.Singular.ExtractText().Equals(name, StringComparison.OrdinalIgnoreCase)).Select(r => r.RowId).ToHashSet();
        neoExdeathIds = Ids("Neo Exdeath");
        chaosIds = Ids("Chaos");

        effectHook = Plugin.GameInterop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(ActionEffectHandler.MemberFunctionPointers.Receive, OnActionEffect);
        actorControlHook = Plugin.GameInterop.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket, OnActorControl);
        effectHook.Enable();
        actorControlHook.Enable();
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        effectHook.Dispose();
        actorControlHook.Dispose();
    }

    private static bool InDmu => Plugin.ClientState.TerritoryType == Territory;

    /// <summary>Sends a mark command after the configured delay. Also used by the settings window's Test buttons.</summary>
    internal void QueueMark(string command)
    {
        command = command.Trim();
        if (!command.StartsWith('/')) return;
        var c = plugin.Config;
        // Humanize: a random wait in [min, max], so the mark doesn't land the same instant every pull.
        var delay = c.DmuHumanize ? Random.Shared.Next(c.DmuDelayMinMs, Math.Max(c.DmuDelayMinMs, c.DmuDelayMaxMs) + 1) : c.DmuMarkDelayMs;
        lock (marks) marks.Add((DateTime.UtcNow.AddMilliseconds(Math.Clamp(delay, 0, Configuration.MaxDmuDelayMs)), command));
    }

    private void OnActionEffect(uint casterId, Character* caster, Vector3* pos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targets)
    {
        effectHook.Original(casterId, caster, pos, header, effects, targets);
        // Ignore repeats of the same cast within a second.
        if (header->ActionId != PathOfLight || !InDmu || DateTime.UtcNow - lastTower < TimeSpan.FromSeconds(1)) return;
        lastTower = DateTime.UtcNow;
        towersResolved++;
    }

    private void OnActorControl(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded)
    {
        actorControlHook.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
        if (category != HeadMarkerCategory || towersResolved != 3 || !InDmu || !plugin.Config.DmuEnabled || !plugin.Config.DmuP2Marks) return;
        if (entityId != Plugin.ObjectTable.LocalPlayer?.EntityId) return;
        if (arg1 == SpreadMarker) QueueMark(plugin.Config.DmuP2Circle);
        else if (arg1 == ConeMarker) QueueMark(plugin.Config.DmuP2Cone);
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        SendDueMarks();

        var me = Plugin.ObjectTable.LocalPlayer;
        if (!plugin.Config.DmuEnabled || !InDmu || me == null || !Plugin.Condition[ConditionFlag.InCombat])
        {
            Reset();
            return;
        }

        var statuses = me.StatusList.Where(s => s.StatusId != 0).ToArray();
        MarkBlackHole(statuses);
        ReadTells();
        var blackHole = plugin.Config.DmuP3Guide ? BlackHoleGuide(statuses, IsDps(me)) : [];
        var icons = blackHole.Count == 0 ? [] : hadAccretion ? new[] { FirstInLine + (uint)myLine - 1, Accretion } : new[] { FirstInLine + (uint)myLine - 1 };
        overlay = new Overlay(icons, blackHole, plugin.Config.DmuGuide ? BuildCallouts(me, statuses) : []);
    }

    private void SendDueMarks()
    {
        lock (marks)
        {
            var now = DateTime.UtcNow;
            foreach (var (_, command) in marks.Where(m => m.Due <= now)) ChatSender.Send(command);
            marks.RemoveAll(m => m.Due <= now);
        }
    }

    private void Reset()
    {
        towersResolved = 0;
        markedLine = 0;
        blackHoleStart = null;
        hadAccretion = false;
        wound = null;
        neoTell = chaosTell = null;
        realities.Clear();
        overlay = Overlay.Empty;
    }

    private void MarkBlackHole(IEnumerable<IStatus> statuses)
    {
        var line = statuses.FirstOrDefault(s => s.StatusId is >= FirstInLine and <= ThirdInLine)?.StatusId ?? 0;
        if (line != 0 && blackHoleStart == null)
        {
            blackHoleStart = DateTime.UtcNow;
            myLine = (int)(line - FirstInLine) + 1;
        }
        if (line != 0 && line != markedLine && plugin.Config.DmuP3Marks)
            QueueMark((line - FirstInLine) switch { 0 => plugin.Config.DmuP3First, 1 => plugin.Config.DmuP3Second, _ => plugin.Config.DmuP3Third });
        markedLine = line;
    }

    private void ReadTells()
    {
        chaosCast = null;
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IBattleNpc npc) continue;
            var isNeo = neoExdeathIds.Contains(npc.NameId);
            if (!isNeo && !chaosIds.Contains(npc.NameId)) continue;
            if (!isNeo && npc.IsCasting && npc.CastActionId is Inferno or Tsunami)
                chaosCast = (npc.CastActionId, npc.TotalCastTime - npc.CurrentCastTime);
            var tell = npc.StatusList.FirstOrDefault(s => s.StatusId == BossTell);
            if (tell == null || !TellIsReal.TryGetValue(tell.Param, out var real)) continue;
            if (isNeo) neoTell = (real, DateTime.UtcNow);
            else chaosTell = (real, DateTime.UtcNow);
        }
    }

    private List<Callout> BuildCallouts(IPlayerCharacter me, IStatus[] statuses)
    {
        var ids = statuses.Select(s => s.StatusId).ToHashSet();
        if (ids.Contains(WhiteWound1) || ids.Contains(BlackWound2)) wound = "Black";
        else if (ids.Contains(WhiteWound2) || ids.Contains(BlackWound1)) wound = "White";
        else if (!ids.Contains(AllaganField) && !ids.Contains(BeyondDeathLive) && !ids.Contains(BeyondDeathDie)) wound = null;

        var seen = new HashSet<(uint, uint)>();
        var callouts = new List<Callout>();
        foreach (var s in statuses)
            if (P4Callout(s.StatusId, Reality(me.EntityId, s.StatusId, seen), wound, s.RemainingTime) is { } callout) callouts.Add(callout);

        if (chaosCast is { } cast)
            callouts.Add(ChaosCastCallout(cast.Action, chaosTell is { } t && DateTime.UtcNow - t.Seen < TellFreshness ? t.Real : null, cast.Remaining));

        // Other players' Cursed Shriek: everyone else has to face it or look away. In DMU everyone nearby is in your party.
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IPlayerCharacter p || p.EntityId == me.EntityId) continue;
            if (p.StatusList.FirstOrDefault(s => s.StatusId == CursedShriek) is { } shriek)
                callouts.Add(GazeCallout(p.Name.TextValue, Reality(p.EntityId, CursedShriek, seen), shriek.RemainingTime));
        }

        foreach (var key in realities.Keys.Where(k => !seen.Contains(k)).ToArray()) realities.Remove(key);
        return callouts;
    }

    /// <summary>Whether a player's debuff is real. The tell can vanish before the debuff resolves, so the first answer is kept.</summary>
    private bool? Reality(uint entity, uint status, HashSet<(uint, uint)> seen)
    {
        var key = (entity, status);
        seen.Add(key);
        if (realities.GetValueOrDefault(key) is { } known) return known;
        var tell = status is DynamicFluid or Entropy ? chaosTell : neoTell;
        return realities[key] = tell is { } t && DateTime.UtcNow - t.Seen < TellFreshness ? t.Real : null;
    }

    /// <summary>Chaos's Tsunami/Inferno, as cactbot calls it.</summary>
    internal static Callout ChaosCastCallout(uint action, bool? real, float remaining)
    {
        var name = action == Inferno ? "Inferno" : "Tsunami";
        var call = (action == Inferno, real) switch
        {
            (true, true) => "Puddles first",
            (true, false) => "Donuts first",
            (false, true) => "Donuts second",
            (false, false) => "Puddles second",
            _ => name,
        };
        var detail = real == null ? $"Chaos casts {name}: real/fake not caught" : $"Chaos casts {name} ({(real.Value ? "real" : "fake")}): {call.ToLowerInvariant()}";
        return new(action, call, detail, real == null ? UnknownColor : real.Value ? RealColor : FakeColor, remaining, IsAction: true);
    }

    /// <summary>Another player's Cursed Shriek: real → look away from them, fake → face them.</summary>
    internal static Callout GazeCallout(string name, bool? real, float remaining)
    {
        var first = name.Split(' ')[0];
        var call = real switch { true => $"Look away from {first}", false => $"Face {first}", _ => $"Gaze: {first}" };
        var detail = real switch { true => $"Cursed Shriek on {name} (real): look away from them", false => $"Cursed Shriek on {name} (fake): face them", _ => $"Cursed Shriek on {name}: real looks away, fake faces them" };
        return new(CursedShriek, call, detail, real == null ? UnknownColor : real.Value ? RealColor : FakeColor, remaining);
    }

    private static bool IsDps(IPlayerCharacter me) => me.ClassJob.Value.Role is 2 or 3;

    private List<string> BlackHoleGuide(IStatus[] statuses, bool isDps)
    {
        if (blackHoleStart is not { } start) return [];
        if (statuses.Any(s => s.StatusId == Accretion)) hadAccretion = true;
        return BlackHoleLines(myLine, isDps, hadAccretion, plugin.Config.DmuBlackHoleStrategy == "Fae", (DateTime.UtcNow - start).TotalSeconds);
    }

    /// <summary>Your Black Hole role, then the current and next tether you grab.</summary>
    internal static List<string> BlackHoleLines(int line, bool isDps, bool accretion, bool fae, double elapsed)
    {
        if (elapsed > BlackHoleEnd) return [];
        var role = accretion ? 'A' : isDps != fae ? 'D' : 'S';
        var lineName = line switch { 1 => "First", 2 => "Second", _ => "Third" };
        var roleName = role switch { 'A' => "Accretion", 'D' => "DPS", _ => "Support" };
        var lines = new List<string> { $"Black Hole: {lineName} in Line, {roleName}" };
        foreach (var t in Tethers.Where(t => t.Line == line && t.Role == role))
        {
            var wave = Waves.First(w => w.Set == t.Set && w.Wave == t.Wave);
            if (wave.End <= elapsed) continue;
            var when = wave.Start <= elapsed ? "NOW" : $"in {wave.Start - elapsed:0}s";
            lines.Add($"Set {t.Set} wave {t.Wave} {when}: {t.Action}");
            if (lines.Count == 3) break;
        }
        return lines;
    }

    /// <summary>The tile for a P4 debuff, as DMU Helper calls it. Null for statuses this helper doesn't know.</summary>
    internal static Callout? P4Callout(uint status, bool? real, string? wound, float remaining)
    {
        if (Guide(status, real, wound) is not { } detail) return null;
        if (status is WhiteWound1 or WhiteWound2) return new(status, "White Wound", detail, PurpleSide, remaining);
        if (status is BlackWound1 or BlackWound2) return new(status, "Black Wound", detail, BlueSide, remaining);
        if (Destination(status, real, wound) is { } to) return new(status, $"Go {to}", detail, to == "Black" ? BlueSide : PurpleSide, remaining);
        var label = status switch
        {
            AllaganField => "Allagan Field",
            BeyondDeathLive => "Opposite Wound",
            BeyondDeathDie => "Same Wound",
            _ when real == null => "Unknown",
            _ => $"{(real.Value ? "Real" : "Fake")}: {Resolution(status, real.Value)}",
        };
        var color = status is AllaganField or BeyondDeathLive or BeyondDeathDie || real == null ? UnknownColor : real.Value ? RealColor : FakeColor;
        return new(status, label, detail, color, remaining);
    }

    private static string Resolution(uint status, bool real) => status switch
    {
        5545 => real ? "Stack" : "Spread",
        5544 => real ? "Spread" : "Stack",
        5543 => real ? "Look away" : "Look toward",
        5546 => real ? "Stop" : "Move",
        DynamicFluid => real ? "Stack for donuts" : "Bait puddles",
        Entropy => real ? "Bait puddles" : "Stack for donuts",
        _ => real ? "Real" : "Fake",
    };

    /// <summary>The Wound to go to for Flood: survive → the opposite one, die → your own.</summary>
    private static string? Destination(uint status, bool? real, string? wound)
    {
        bool? die = status switch { AllaganField => !real, BeyondDeathLive => false, BeyondDeathDie => true, _ => null };
        if (wound == null || die == null) return null;
        return die.Value ? wound : wound == "Black" ? "White" : "Black";
    }

    /// <summary>What a P4 debuff asks of you. Null for statuses this helper doesn't know.</summary>
    internal static string? Guide(uint status, bool? real, string? wound) => status switch
    {
        5545 => real switch { true => "Water (real): stack with your group", false => "Water (fake): spread from your group", _ => "Water: real stacks, fake spreads" },
        5544 => real switch { true => "Lightning (real): spread from your group", false => "Lightning (fake): stack with your group", _ => "Lightning: real spreads, fake stacks" },
        5543 => real switch { true => "Shriek (real): stand out, everyone looks away", false => "Shriek (fake): stand center, everyone looks at you", _ => "Shriek: real look away, fake look at" },
        5546 => real switch { true => "Bomb (real): stop moving and acting", false => "Bomb (fake): keep moving", _ => "Bomb: real stillness, fake motion" },
        DynamicFluid => real switch { true => "Dynamic Fluid (real): stack for donuts", false => "Dynamic Fluid (fake): bait puddles", _ => "Dynamic Fluid: real donuts, fake puddles" },
        Entropy => real switch { true => "Entropy (real): bait puddles", false => "Entropy (fake): stack for donuts", _ => "Entropy: real puddles, fake donuts" },
        WhiteWound1 or WhiteWound2 => "White Wound",
        BlackWound1 or BlackWound2 => "Black Wound",
        AllaganField when real == null => "Allagan Field: waiting for the truth/lie tell",
        AllaganField or BeyondDeathLive or BeyondDeathDie when Destination(status, real, wound) is { } to =>
            $"{(status == AllaganField ? $"Allagan Field ({(real!.Value ? "real" : "fake")})" : "Beyond Death")}: go to {(to == "Black" ? "Blue" : "Purple")} ({to})",
        AllaganField => "Allagan Field: waiting for Wound",
        BeyondDeathLive or BeyondDeathDie => "Beyond Death: waiting for Wound",
        _ => null,
    };

    private static readonly Overlay PreviewOverlay = new(
        [3005],
        BlackHoleLines(2, true, false, false, 50),
        [
            P4Callout(5545, true, null, 12.3f)!,
            P4Callout(5546, false, null, 18.6f)!,
            P4Callout(Entropy, false, null, 25.4f)!,
            P4Callout(AllaganField, false, "Black", 21.0f)!,
            ChaosCastCallout(Inferno, true, 6.1f),
            GazeCallout("Alisaie Leveilleur", true, 9.8f),
        ]);

    /// <summary>The overlay. Drag it anywhere; ImGui remembers where.</summary>
    public void Draw()
    {
        var o = !plugin.Config.DmuEnabled ? Overlay.Empty : Preview ? PreviewOverlay : overlay;
        if (o.IsEmpty) return;
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * new Vector2(0.5f, 0.22f), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0));
        ImGui.SetNextWindowBgAlpha(0.45f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
                                       ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;
        if (ImGui.Begin("Jelly DMU###KonyakkuDmu", flags))
        {
            ImGui.SetWindowFontScale(1.2f);
            // Status icons are 24×32.
            var iconSize = new Vector2(30, 40) * ImGuiHelpers.GlobalScale;
            if (o.BlackHole.Count > 0) DrawBlackHole(o, iconSize);
            if (o.BlackHole.Count > 0 && o.Debuffs.Count > 0) ImGui.Separator();
            for (var i = 0; i < o.Debuffs.Count; i++)
            {
                if (i > 0) ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X * 2);
                DrawTile(o.Debuffs[i], iconSize);
            }
        }
        ImGui.End();
    }

    private void DrawBlackHole(Overlay o, Vector2 iconSize)
    {
        foreach (var icon in o.Icons)
        {
            DrawIcon(icon, false, iconSize, Gold);
            ImGui.SameLine();
        }
        ImGui.BeginGroup();
        ImGui.TextColored(Gold, o.BlackHole[0]);
        foreach (var line in o.BlackHole.Skip(1)) ImGui.TextColored(line.Contains("NOW") ? RealColor : TimerColor, line);
        ImGui.EndGroup();
    }

    /// <summary>DMU Helper's tile: the call, the icon framed in the call's colour, the timer; the instruction on hover.</summary>
    private void DrawTile(Callout c, Vector2 iconSize)
    {
        var timer = c.Remaining > 0 ? $"{c.Remaining:0.0}s" : "-";
        var width = MathF.Max(iconSize.X, MathF.Max(ImGui.CalcTextSize(c.Label).X, ImGui.CalcTextSize(timer).X));
        var x = ImGui.GetCursorPosX();
        ImGui.BeginGroup();
        ImGui.SetCursorPosX(x + (width - ImGui.CalcTextSize(c.Label).X) / 2);
        ImGui.TextColored(c.Color, c.Label);
        ImGui.SetCursorPosX(x + (width - iconSize.X) / 2);
        // Action icons are square, status icons 24×32.
        DrawIcon(c.Id, c.IsAction, c.IsAction ? new Vector2(iconSize.Y) : iconSize, c.Color);
        ImGui.SetCursorPosX(x + (width - ImGui.CalcTextSize(timer).X) / 2);
        ImGui.TextColored(TimerColor, timer);
        ImGui.EndGroup();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(c.Detail);
    }

    private void DrawIcon(uint id, bool action, Vector2 size, Vector4 border)
    {
        if (!icons.TryGetValue((action, id), out var icon))
            icons[(action, id)] = icon = action
                ? Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(id)?.Icon ?? 0u
                : Plugin.DataManager.GetExcelSheet<Status>().GetRowOrDefault(id)?.Icon ?? 0u;
        var start = ImGui.GetCursorScreenPos();
        if (icon != 0 && Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrDefault() is { } wrap)
            ImGui.Image(wrap.Handle, size);
        else ImGui.Dummy(size);
        ImGui.GetWindowDrawList().AddRect(start - Vector2.One, start + size + Vector2.One, ImGui.GetColorU32(border), 4f, ImDrawFlags.None, 2f);
    }
}
