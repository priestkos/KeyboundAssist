using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundAssist;

// The duty's enemy cast bar is the only early warning for a locking cast. Dynamic Sensory Jammer and
// Cursed Gaze punish typing for their whole cast, and the input field's own lock only reports that once
// it has already engaged, so the bar is read ahead of it. Label scanning follows XivType (MIT), which
// detects the same two spells against this client; the hold window follows AutoKeyboundBrawler's live
// note that the penalty outlives the cast by a few seconds.
internal static unsafe class CastBar
{
    private const int HoldAfterCastMs = 3500;
    private const int MaxNodes = 4096;
    private const int MaxDepth = 12;

    private static long holdUntil;
    private static string heldSpell = "";

    // True while a locking cast is up, and for a few seconds after it resolves.
    internal static bool Blocked(IGameGui gui, long now, out string reason)
    {
        foreach (var label in Labels(gui))
        {
            if (!Halt(label)) continue;
            holdUntil = now + HoldAfterCastMs;
            heldSpell = label;
            break;
        }

        if (now < holdUntil)
        {
            reason = $"Holding - {heldSpell} is locking input.";
            return true;
        }
        reason = "";
        return false;
    }

    internal static void Reset()
    {
        holdUntil = 0;
        heldSpell = "";
    }

    private static bool Halt(string label) =>
        label.Contains("Dynamic Sensory Jammer", StringComparison.OrdinalIgnoreCase) ||
        label.Contains("Cursed Gaze", StringComparison.OrdinalIgnoreCase);

    private static List<string> Labels(IGameGui gui)
    {
        var labels = new List<string>();
        var addon = gui.GetAddonByName<AtkUnitBase>("CastBarEnemy");
        if (addon == null || !addon->IsVisible || !addon->IsReady)
        {
            // The old name is still what some clients load the duty's cast bar under.
            addon = gui.GetAddonByName<AtkUnitBase>("KTGEnemyCastBar");
            if (addon == null || !addon->IsVisible || !addon->IsReady) return labels;
        }
        Walk(&addon->UldManager, labels, [], 0);
        return labels;
    }

    private static void Walk(AtkUldManager* manager, List<string> labels, HashSet<nint> visited, int depth)
    {
        if (manager == null || manager->NodeList == null || depth > MaxDepth || visited.Count >= MaxNodes) return;
        for (var i = 0; i < Math.Min((int)manager->NodeListCount, MaxNodes); i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !visited.Add((nint)node) || !Shown(node)) continue;
            if (node->Type == NodeType.Text)
            {
                var text = SeString.Parse(((AtkTextNode*)node)->NodeText.AsSpan()).TextValue.Trim();
                if (text.Length is > 2 and <= 96) labels.Add(text);
            }
            else if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null) Walk(&component->UldManager, labels, visited, depth + 1);
            }
        }
    }

    private static bool Shown(AtkResNode* node)
    {
        var depth = 0;
        for (; node != null && depth < 32; depth++, node = node->ParentNode)
            if (!node->IsVisible()) return false;
        return node == null;
    }
}
