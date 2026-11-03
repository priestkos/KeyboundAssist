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

        var keepAlive = configuration.KeepAlive;
        if (ImGui.Checkbox("Keep the activity timer alive", ref keepAlive))
        {
            configuration.KeepAlive = keepAlive;
            save();
        }
        ImGui.TextDisabled("Clears the duty's own inactivity counters. The plugin still sends no keys.");

        var ignorePunctuation = configuration.IgnorePunctuation;
        if (ImGui.Checkbox("Ignore punctuation", ref ignorePunctuation))
        {
            configuration.IgnorePunctuation = ignorePunctuation;
            save();
        }
        ImGui.TextDisabled("Types \"dontstop\" for \"don't-stop!\". Turn it off if words stop matching.");

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
