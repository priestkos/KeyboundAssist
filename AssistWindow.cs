using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace KeyboundAssist;

internal sealed class AssistWindow(Configuration configuration, Action save, Func<string> status,
    Func<string> syncStatus, Action requestSync) : Window("Keybound Assist###KeyboundAssistMain")
{
    private static readonly string[] Targets = ["Shortest prompt", "Nearest enemy", "Threat"];

    public override void Draw()
    {
        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Fill prompts for me", ref enabled))
        {
            configuration.Enabled = enabled;
            save();
        }

        var delayMin = configuration.CharDelayMinMs;
        var delayMax = configuration.CharDelayMaxMs;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.DragIntRange2("Delay per character (ms)", ref delayMin, ref delayMax, 1f, TypingPlan.MinDelayMs, TypingPlan.MaxDelayMs))
        {
            configuration.CharDelayMinMs = delayMin;
            configuration.CharDelayMaxMs = delayMax;
            save();
        }
        ImGui.TextDisabled("Each character draws a new delay in this range. Equal ends = fixed.");

        var autoSubmit = configuration.AutoSubmit;
        if (ImGui.Checkbox("Send the word automatically", ref autoSubmit))
        {
            configuration.AutoSubmit = autoSubmit;
            save();
        }
        if (autoSubmit)
        {
            var submitMin = configuration.SubmitDelayMinMs;
            var submitMax = configuration.SubmitDelayMaxMs;
            ImGui.SetNextItemWidth(200f);
            if (ImGui.DragIntRange2("Delay before sending (ms)", ref submitMin, ref submitMax, 1f,
                    TypingPlan.SubmitFloorMs, TypingPlan.SubmitCapMs))
            {
                configuration.SubmitDelayMinMs = submitMin;
                configuration.SubmitDelayMaxMs = submitMax;
                save();
            }
            ImGui.TextDisabled("Waited after the last letter, drawn fresh per word. Default 300-500.");
        }

        var priority = (int)configuration.Priority;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.Combo("Target", ref priority, Targets, Targets.Length))
        {
            configuration.Priority = (TargetPriority)priority;
            save();
        }
        if (configuration.Priority == TargetPriority.Threat)
            ImGui.TextDisabled("Needles Cactuar, enemies at the barrier, casting adds, Cactuars, adds, then the boss; lowest first.");

        ImGui.Separator();
        ImGui.TextWrapped(configuration.AutoSubmit
            ? "The plugin types the word, waits the delay above and sends it; the next prompt is filled once the field is empty again."
            : "The plugin types the word and stops. Press Enter yourself to submit it, " +
              "and the next prompt is filled once the field is empty again.");
        ImGui.Separator();
        ImGui.TextWrapped(status());

        ImGui.Separator();
        ImGui.TextUnformatted(configuration.AchievementWins is { } count ? $"Wins: {count}" : "Wins: not read yet");
        ImGui.SameLine();
        if (ImGui.Button("Sync from achievements")) requestSync();
        ImGui.TextDisabled(syncStatus());

        if (ImGui.CollapsingHeader($"Words seen ({configuration.WordsSeen.Count})"))
        {
            using var list = ImRaii.Child("##wordsSeen", new Vector2(0, 200 * ImGuiHelpers.GlobalScale), true);
            if (list.Success)
                foreach (var (word, seen) in configuration.WordsSeen.OrderByDescending(w => w.Value).ThenBy(w => w.Key))
                    ImGui.TextUnformatted($"{seen,5}  {word}");
        }
    }
}
