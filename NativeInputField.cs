using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundAssist;

// Production side of IInputField: the duty's own text component, reached through the addon that owns it.
internal sealed unsafe class NativeInputField(AtkUnitBase* addon, AtkComponentTextInput* input) : IInputField
{
    public bool IsActive => input->IsActive;

    public bool Focus() => GameState.EnsureFocus(addon, input);

    public void SetText(string text) => input->SetText(text);

    // The component's own Enter callback - the route AutoKeyboundBrawler (DirectSubmit) and Keybound Pilot
    // (InputSubmission) both use - so no key is sent. Invoking it directly skips the component's own handling of
    // the result, so a ClearText result is applied here, on the component re-resolved after the callback.
    public bool Submit()
    {
        if (!input->Enabled || !input->IsActive || input->OwnerAddon != addon || input->Callback == null ||
            input->RawString.StringPtr.Value == null || input->EvaluatedString.StringPtr.Value == null) return false;
        var result = input->Callback(addon, InputCallbackType.Enter, input->RawString.StringPtr,
            input->EvaluatedString.StringPtr, input->CallbackEventKind);
        if (result == InputCallbackResult.ClearText)
        {
            var owner = GameState.InputAddon(Plugin.GameGui);
            if (owner == addon && GameState.InputComponent(owner) == input) input->SetText("");
        }
        return true;
    }
}
