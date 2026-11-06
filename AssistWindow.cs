using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace KeyboundAssist;

internal sealed class AssistWindow(Configuration configuration, Action save, Func<string> status,
    Func<string> syncStatus, Action requestSync) : Window("Keybound Assist###KeyboundAssistMain")
{
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
        if (ImGui.Combo("Target", ref priority, new[] { "Shortest prompt", "Nearest enemy", "Threat" }, 3))
        {
            configuration.Priority = (TargetPriority)priority;
            save();
        }
        if (configuration.Priority == TargetPriority.Threat)
            ImGui.TextDisabled("Needles Cactuar, casting adds, Cactuars, adds, then the boss; lowest first.");

        ImGui.Separator();
        ImGui.TextWrapped(configuration.AutoSubmit
            ? "The plugin types the word, waits the delay above and sends it; the next prompt is filled once the field is empty again."
            : "The plugin types the word and stops. Press Enter yourself to submit it, " +
              "and the next prompt is filled once the field is empty again.");
        ImGui.Separator();
        ImGui.TextWrapped(status());

        ImGui.Separator();
        var wins = configuration.UseManualWins ? configuration.ManualWins : configuration.AchievementWins;
        ImGui.TextUnformatted(wins is { } count ? $"Wins: {count}" : "Wins: not read yet");
        ImGui.SameLine();
        if (ImGui.Button("Sync from achievements")) requestSync();
        ImGui.TextDisabled(syncStatus());
        var manual = configuration.UseManualWins;
        if (ImGui.Checkbox("Use a manual count", ref manual))
        {
            configuration.UseManualWins = manual;
            // Start the manual count from the achievement one rather than from zero.
            if (manual && configuration.ManualWins == 0) configuration.ManualWins = configuration.AchievementWins ?? 0;
            save();
        }
        if (manual)
        {
            var manualWins = configuration.ManualWins;
            ImGui.SetNextItemWidth(120f);
            if (ImGui.InputInt("Manual wins", ref manualWins))
            {
                configuration.ManualWins = Math.Max(0, manualWins);
                save();
            }
        }

        if (ImGui.CollapsingHeader($"Words seen ({configuration.WordsSeen.Count})"))
        {
            using var list = ImRaii.Child("##wordsSeen", new Vector2(0, 200 * ImGuiHelpers.GlobalScale), true);
            if (list.Success)
                foreach (var (word, seen) in configuration.WordsSeen.OrderByDescending(w => w.Value).ThenBy(w => w.Key))
                    ImGui.TextUnformatted($"{seen,5}  {word}");
        }
    }
}
