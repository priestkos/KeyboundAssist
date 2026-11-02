using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundAssist;

// Production side of IInputField: the duty's own text component, reached through the addon that owns it.
internal sealed unsafe class NativeInputField(AtkUnitBase* addon, AtkComponentTextInput* input) : IInputField
{
    public bool IsActive => input->IsActive;

    public bool Focus() => GameState.EnsureFocus(addon, input);

    public void SetText(string text) => input->SetText(text);
}
