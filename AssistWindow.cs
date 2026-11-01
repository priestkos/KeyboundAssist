using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KeyboundAssist;

internal sealed class AssistWindow(Configuration configuration, Action save, Func<string> status) : Window("Keybound Assist###KeyboundAssistMain")
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
        if (ImGui.DragIntRange2("Delay per character (ms)", ref delayMin, ref delayMax, 1f, 0, 300))
        {
            configuration.CharDelayMinMs = delayMin;
            configuration.CharDelayMaxMs = delayMax;
            save();
        }
        ImGui.TextDisabled("Each character draws a new delay in this range. Equal ends = fixed.");

        var priority = (int)configuration.Priority;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.Combo("Target", ref priority, new[] { "Shortest prompt", "Nearest enemy" }, 2))
        {
            configuration.Priority = (TargetPriority)priority;
            save();
        }

        ImGui.Separator();
        ImGui.TextWrapped("The plugin types the word and stops. Press Enter yourself to submit it, " +
                          "and the next prompt is filled once the field is empty again.");
        ImGui.Separator();
        ImGui.TextWrapped(status());
    }
}
