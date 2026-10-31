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

        var delay = configuration.CharDelayMs;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderInt("Delay per character (ms)", ref delay, 0, 300))
        {
            configuration.CharDelayMs = delay;
            save();
        }

        ImGui.Separator();
        ImGui.TextWrapped("The plugin types the word and stops. Press Enter yourself to submit it, " +
                          "and the next prompt is filled once the field is empty again.");
        ImGui.Separator();
        ImGui.TextWrapped(status());
    }
}
