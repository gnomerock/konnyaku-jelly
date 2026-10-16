using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Konyakku.Windows;

/// <summary>Shows translations (with a Thai-capable font) and lets you write outgoing messages.</summary>
internal sealed class LogWindow : Window
{
    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 Mine = new(0.55f, 0.85f, 1f, 1f);

    private readonly Plugin plugin;
    private string input = string.Empty;
    private bool busy;
    private int lastCount;

    public LogWindow(Plugin plugin) : base("Konyakku — วุ้นแปลภาษา###KonyakkuLog")
    {
        this.plugin = plugin;
        Size = new Vector2(480, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        using var font = plugin.ThaiFont.Push();
        var config = plugin.Config;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Translate chat", ref enabled))
        {
            config.Enabled = enabled;
            config.Save();
        }
        ImGui.SameLine();
        ImGui.TextColored(Dim, $"→ {config.IncomingTarget}");
        ImGui.SameLine(ImGui.GetWindowWidth() - 110);
        if (ImGui.SmallButton("Clear")) plugin.ClearLog();
        ImGui.SameLine();
        if (ImGui.SmallButton("Settings")) plugin.OpenConfig();

        if (plugin.Translator.LastError is { } err)
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), err);

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("##log", new Vector2(0, -footer), true))
        {
            var entries = plugin.SnapshotLog();
            foreach (var e in entries)
            {
                ImGui.TextColored(Dim, $"{e.Time:HH:mm} [{e.Channel}]");
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, e.Outgoing ? Mine : new Vector4(1, 1, 1, 1));
                ImGui.TextWrapped($"{e.Sender}: {e.Translation}");
                ImGui.PopStyleColor();
                if (config.ShowOriginal)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Dim);
                    ImGui.TextWrapped($"    {e.Original}");
                    ImGui.PopStyleColor();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) plugin.CopyToClipboard(e.Translation);
            }

            if (entries.Count != lastCount && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 40)
                ImGui.SetScrollHereY(1f);
            lastCount = entries.Count;
        }
        ImGui.EndChild();

        DrawComposer(config);
    }

    private void DrawComposer(Configuration config)
    {
        var prefix = config.SendPrefix;
        ImGui.SetNextItemWidth(48);
        if (ImGui.InputText("##prefix", ref prefix, 16))
        {
            config.SendPrefix = prefix;
            config.Save();
        }
        ImGui.SameLine();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 120);
        var submit = ImGui.InputTextWithHint("##compose", $"พิมพ์ข้อความ → {config.OutgoingTarget}", ref input, 400,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        ImGui.BeginDisabled(busy || input.Trim().Length == 0);
        submit |= ImGui.Button("Send");
        ImGui.SameLine();
        var copy = ImGui.Button("Copy");
        ImGui.EndDisabled();

        if ((submit || copy) && !busy && input.Trim().Length > 0)
        {
            var text = input.Trim();
            var sendPrefix = copy || string.IsNullOrWhiteSpace(config.SendPrefix) ? null : config.SendPrefix.Trim();
            busy = true;
            _ = plugin.TranslateOutgoingAsync(text, sendPrefix).ContinueWith(t =>
            {
                if (t.Result != null) input = string.Empty;
                busy = false;
            });
        }
    }
}
