using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
using Konyakku.Windows;
using Lumina.Excel.Sheets;

namespace Konyakku;

/// <summary>
/// Dancing Mad (Ultimate) helper: marks yourself for P2 Path of Light and P3 Black Hole, and shows what your P4
/// debuffs ask of you. IDs come from cactbot's dancing_mad.ts and Nainaiowo's DMU Helper (MIT).
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
    private const uint DynamicFluid = 5548, Entropy = 5547;

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

    private readonly Plugin plugin;
    private readonly Hook<ActionEffectHandler.Delegates.Receive> effectHook;
    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket> actorControlHook;
    private readonly HashSet<uint> neoExdeathIds, chaosIds;
    private readonly Queue<string> marks = new();
    private readonly Dictionary<uint, bool?> realities = [];
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
    private volatile string[] guide = [];

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
        if (category != HeadMarkerCategory || towersResolved != 3 || !InDmu || !plugin.Config.DmuP2Marks) return;
        if (entityId != Plugin.ObjectTable.LocalPlayer?.EntityId) return;
        // Queued, not sent from inside the packet handler.
        if (arg1 == SpreadMarker) marks.Enqueue(plugin.Config.DmuP2Circle);
        else if (arg1 == ConeMarker) marks.Enqueue(plugin.Config.DmuP2Cone);
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (!InDmu || me == null || !Plugin.Condition[ConditionFlag.InCombat])
        {
            Reset();
            return;
        }

        while (marks.TryDequeue(out var command))
            if (command.StartsWith('/')) ChatSender.Send(command);

        var statuses = me.StatusList.Where(s => s.StatusId != 0).ToArray();
        MarkBlackHole(statuses);
        ReadTells();
        var lines = new List<string>();
        if (plugin.Config.DmuP3Guide) lines.AddRange(BlackHoleGuide(statuses, IsDps(me)));
        if (plugin.Config.DmuGuide) lines.AddRange(BuildGuide(statuses));
        guide = lines.ToArray();
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
        marks.Clear();
        guide = [];
    }

    private void MarkBlackHole(IEnumerable<Dalamud.Game.ClientState.Statuses.IStatus> statuses)
    {
        var line = statuses.FirstOrDefault(s => s.StatusId is >= FirstInLine and <= ThirdInLine)?.StatusId ?? 0;
        if (line != 0 && blackHoleStart == null)
        {
            blackHoleStart = DateTime.UtcNow;
            myLine = (int)(line - FirstInLine) + 1;
        }
        if (line != 0 && line != markedLine && plugin.Config.DmuP3Marks)
        {
            var command = (line - FirstInLine) switch { 0 => plugin.Config.DmuP3First, 1 => plugin.Config.DmuP3Second, _ => plugin.Config.DmuP3Third };
            if (command.StartsWith('/')) ChatSender.Send(command);
        }
        markedLine = line;
    }

    private void ReadTells()
    {
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IBattleNpc npc) continue;
            var isNeo = neoExdeathIds.Contains(npc.NameId);
            if (!isNeo && !chaosIds.Contains(npc.NameId)) continue;
            var tell = npc.StatusList.FirstOrDefault(s => s.StatusId == BossTell);
            if (tell == null || !TellIsReal.TryGetValue(tell.Param, out var real)) continue;
            if (isNeo) neoTell = (real, DateTime.UtcNow);
            else chaosTell = (real, DateTime.UtcNow);
        }
    }

    private string[] BuildGuide(Dalamud.Game.ClientState.Statuses.IStatus[] statuses)
    {
        var ids = statuses.Select(s => s.StatusId).ToHashSet();
        if (ids.Contains(WhiteWound1) || ids.Contains(BlackWound2)) wound = "Black";
        else if (ids.Contains(WhiteWound2) || ids.Contains(BlackWound1)) wound = "White";
        else if (!ids.Contains(AllaganField) && !ids.Contains(BeyondDeathLive) && !ids.Contains(BeyondDeathDie)) wound = null;

        foreach (var id in realities.Keys.Where(id => !ids.Contains(id)).ToArray()) realities.Remove(id);

        var lines = new List<string>();
        foreach (var s in statuses)
        {
            var tell = s.StatusId is DynamicFluid or Entropy ? chaosTell : neoTell;
            // The tell can vanish before the debuff resolves, so keep the first answer.
            if (realities.GetValueOrDefault(s.StatusId) == null)
                realities[s.StatusId] = tell is { } t && DateTime.UtcNow - t.Seen < TellFreshness ? t.Real : null;
            if (Guide(s.StatusId, realities[s.StatusId], wound) is { } text)
                lines.Add($"{text}  ({s.RemainingTime:0}s)");
            else realities.Remove(s.StatusId);
        }
        return lines.ToArray();
    }

    private static bool IsDps(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter me) => me.ClassJob.Value.Role is 2 or 3;

    private List<string> BlackHoleGuide(Dalamud.Game.ClientState.Statuses.IStatus[] statuses, bool isDps)
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

    /// <summary>What a P4 debuff asks of you. Null for statuses this helper doesn't know.</summary>
    internal static string? Guide(uint status, bool? real, string? wound) => status switch
    {
        5545 => real switch { true => "Water (real): stack with your group", false => "Water (fake): spread from your group", _ => "Water: real stacks, fake spreads" },
        5544 => real switch { true => "Lightning (real): spread from your group", false => "Lightning (fake): stack with your group", _ => "Lightning: real spreads, fake stacks" },
        5543 => real switch { true => "Shriek (real): stand out, everyone looks away", false => "Shriek (fake): stand center, everyone looks at you", _ => "Shriek: real look away, fake look at" },
        5546 => real switch { true => "Bomb (real): stop moving and acting", false => "Bomb (fake): keep moving", _ => "Bomb: real stillness, fake motion" },
        DynamicFluid => real switch { true => "Dynamic Fluid (real): delayed donut", false => "Dynamic Fluid (fake): delayed point-blank", _ => "Dynamic Fluid: real donut, fake point-blank" },
        Entropy => real switch { true => "Entropy (real): delayed point-blank", false => "Entropy (fake): delayed donut", _ => "Entropy: real point-blank, fake donut" },
        // Survive → go to the opposite wound; die → go to your own.
        AllaganField => real == null ? "Allagan Field: waiting for the truth/lie tell" : wound == null ? "Allagan Field: waiting for Wound" : $"Allagan Field ({(real.Value ? "real" : "fake")}): {Destination(wound, die: !real.Value)}",
        BeyondDeathLive or BeyondDeathDie => wound == null ? "Beyond Death: waiting for Wound" : $"Beyond Death: {Destination(wound, die: status == BeyondDeathDie)}",
        _ => null,
    };

    private static string Destination(string wound, bool die)
    {
        var to = die ? wound : wound == "Black" ? "White" : "Black";
        return $"go {(to == "Black" ? "Blue" : "Purple")} ({to})";
    }

    private static readonly string[] PreviewLines =
    [
        .. BlackHoleLines(2, true, false, false, 50),
        $"{Guide(5545, true, null)}  (12s)",
        $"{Guide(AllaganField, false, "Black")}  (20s)",
        $"{Guide(5546, null, null)}  (8s)",
    ];

    /// <summary>The P3/P4 overlay. Drag it anywhere; ImGui remembers where.</summary>
    public void Draw()
    {
        var lines = Preview ? PreviewLines : guide;
        if (lines.Length == 0) return;
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * new Vector2(0.5f, 0.22f), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0));
        ImGui.SetNextWindowBgAlpha(0.6f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
                                       ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;
        if (ImGui.Begin("Jelly DMU###KonyakkuDmu", flags))
        {
            ImGui.SetWindowFontScale(1.5f);
            foreach (var line in lines) ImGui.TextColored(Theme.Yellow, line);
        }
        ImGui.End();
    }
}
