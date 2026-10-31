using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundAssist;

internal readonly record struct ActivityInput(string Field, bool Allowed, string Reason);

// Every read goes through the addon manager on the frame that uses it: no pointer is cached across
// frames, so a closed duty can never leave a dangling node behind.
internal static unsafe class GameState
{
    // Territory ids verified against the installed client (XivType, KeyboundPilot): 1359 is the
    // duty itself, 1360 the Square outside it.
    internal const uint GameTerritory = 1359;

    internal static bool InGame(IClientState client) => client.TerritoryType == GameTerritory;

    internal static AtkUnitBase* InputAddon(IGameGui gui)
    {
        var addon = gui.GetAddonByName<AtkUnitBase>("KTGInput");
        return addon != null && addon->IsVisible && Shown(addon->RootNode) ? addon : null;
    }

    // KTGInput carries the duty's text field as component node 7.
    internal static AtkComponentTextInput* InputComponent(AtkUnitBase* addon)
    {
        if (addon == null || !addon->IsReady) return null;
        var component = addon->GetComponentByNodeId(7);
        if (component == null || component->GetComponentType() != ComponentType.TextInput) return null;
        var input = (AtkComponentTextInput*)component;
        return input->OwnerAddon == addon ? input : null;
    }

    // AtkValues[2] is the duty's "typing allowed" bool and AtkValues[4] the remaining lock seconds.
    internal static ActivityInput? Read(AtkUnitBase* addon, AtkComponentTextInput* input)
    {
        if (addon == null || input == null) return null;
        var raw = input->EvaluatedString.AsSpan();
        if (raw.Length > 1024) return null;
        var field = SeString.Parse(raw).TextValue;

        var values = addon->AtkValues;
        if (values == null || addon->AtkValuesCount < 5 ||
            values[2].Type != AtkValueType.Bool ||
            values[4].Type is not (AtkValueType.Int or AtkValueType.UInt))
            return new(field, false, "Waiting for the activity input permission.");

        var lockSeconds = Math.Max(0, values[4].Int);
        if (lockSeconds > 0) return new(field, false, $"Typing prohibited ({lockSeconds}s).");
        if (!values[2].Bool) return new(field, false, "The activity prohibits typing.");
        // Second, independent gate: the component itself reports whether it will take text.
        if (!input->Enabled) return new(field, false, "The activity disabled its input field.");
        return new(field, true, "Ready.");
    }

    // KTGEWord draws each enemy's prompt as a text label (node 20) inside its own component node.
    internal static string[] Words(IGameGui gui)
    {
        var addon = gui.GetAddonByName<AtkUnitBase>("KTGEWord");
        if (addon == null || !addon->IsVisible) return [];
        var manager = &addon->UldManager;
        if (manager->NodeList == null) return [];

        var words = new List<string>();
        for (var i = 0; i < Math.Min((int)manager->NodeListCount, 4096); i++)
        {
            var node = manager->NodeList[i];
            if (node == null || (int)node->Type < 1000 || !Shown(node)) continue;
            var component = ((AtkComponentNode*)node)->Component;
            if (component == null) continue;
            var label = FindNode(&component->UldManager, 20);
            if (label == null || label->Type != NodeType.Text || !Shown(label)) continue;
            var text = SeString.Parse(((AtkTextNode*)label)->NodeText.AsSpan()).TextValue.Trim();
            if (text.Length is > 0 and <= 160 && text.All(c => c is >= ' ' and <= '~' || char.IsLetter(c)))
                words.Add(text);
        }
        return words.ToArray();
    }

    internal static AtkResNode* FindNode(AtkUldManager* manager, uint id)
    {
        if (manager == null || manager->NodeList == null) return null;
        for (var i = 0; i < Math.Min((int)manager->NodeListCount, 4096); i++)
            if (manager->NodeList[i] != null && manager->NodeList[i]->NodeId == id) return manager->NodeList[i];
        return null;
    }

    // The field has to be the active text input or the player's own Enter goes nowhere. Focus is
    // granted in process; the plugin sends no key of its own.
    internal static bool EnsureFocus(AtkUnitBase* addon, AtkComponentTextInput* input)
    {
        if (addon == null || input == null) return false;
        if (input->IsActive) return true;
        if (input->CollisionNode == null) return false;
        addon->Focus();
        return addon->SetFocusNode((AtkResNode*)input->CollisionNode);
    }

    private static bool Shown(AtkResNode* node)
    {
        var depth = 0;
        for (; node != null && depth < 32; depth++, node = node->ParentNode)
            if (!node->IsVisible()) return false;
        return node == null;
    }
}
