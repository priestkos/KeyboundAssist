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

    // This frame's spell labels by cast bar slot, for the threat order. Node 5 is the cast timer and nodes 2/3 the
    // "Interrupted!" banner (AutoKeyboundBrawler's map), so they are not spells.
    internal static (uint Slot, string Spell)[] Casts { get; private set; } = [];

    // True while a locking cast is up, and for a few seconds after it resolves.
    internal static bool Blocked(IGameGui gui, long now, out string reason)
    {
        var labels = Labels(gui);
        Casts = labels.Where(l => l.Slot != 0 && l.Node is not (2 or 3 or 5) && l.Text.Any(char.IsLetter) &&
                                  !l.Text.Equals("Interrupted!", StringComparison.OrdinalIgnoreCase) &&
                                  !l.Text.Equals("Casting", StringComparison.OrdinalIgnoreCase))
            .Select(l => (l.Slot, l.Text)).ToArray();
        foreach (var label in labels)
        {
            if (!Halt(label.Text)) continue;
            holdUntil = now + HoldAfterCastMs;
            heldSpell = label.Text;
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
        Casts = [];
    }

    private static bool Halt(string label) =>
        label.Contains("Dynamic Sensory Jammer", StringComparison.OrdinalIgnoreCase) ||
        label.Contains("Cursed Gaze", StringComparison.OrdinalIgnoreCase);

    private static List<(uint Slot, uint Node, string Text)> Labels(IGameGui gui)
    {
        var labels = new List<(uint Slot, uint Node, string Text)>();
        var addon = gui.GetAddonByName<AtkUnitBase>("CastBarEnemy");
        if (addon == null || !addon->IsVisible || !addon->IsReady)
        {
            // The old name is still what some clients load the duty's cast bar under.
            addon = gui.GetAddonByName<AtkUnitBase>("KTGEnemyCastBar");
            if (addon == null || !addon->IsVisible || !addon->IsReady) return labels;
        }
        Walk(&addon->UldManager, labels, [], 0, 0);
        return labels;
    }

    // Slot is the top-level component a label sits in: one per enemy cast bar.
    private static void Walk(AtkUldManager* manager, List<(uint Slot, uint Node, string Text)> labels,
        HashSet<nint> visited, int depth, uint slot)
    {
        if (manager == null || manager->NodeList == null || depth > MaxDepth || visited.Count >= MaxNodes) return;
        for (var i = 0; i < Math.Min((int)manager->NodeListCount, MaxNodes); i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !visited.Add((nint)node) || !Shown(node)) continue;
            if (node->Type == NodeType.Text)
            {
                var text = SeString.Parse(((AtkTextNode*)node)->NodeText.AsSpan()).TextValue.Trim();
                if (text.Length is > 2 and <= 96) labels.Add((slot, node->NodeId, text));
            }
            else if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                    Walk(&component->UldManager, labels, visited, depth + 1, depth == 0 ? node->NodeId : slot);
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
