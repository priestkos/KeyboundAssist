namespace KeyboundAssist;

// Wins and words seen, with no Dalamud dependency so tests/ compiles it.
internal static class Stats
{
    // Masterstroke I-IV count the same Keybound Brawler clears to 1, 10, 50 and 100 (Achievement sheet, Data[0]).
    internal const uint FirstWinAchievement = 4070;
    internal const uint LastWinAchievement = 4073;

    // A reading below its maximum is the clear count. A lower tier read at its own maximum only says "at least",
    // so it is ignored; Masterstroke IV is taken as read, since nothing counts higher.
    internal static int? WinsFromProgress(uint id, uint current, uint max) =>
        id is >= FirstWinAchievement and <= LastWinAchievement && max > 0 && current <= max &&
        (current < max || id == LastWinAchievement) ? (int)current : null;

    // A word counts once per appearance: the same slot must stop showing it for RecountMs before it counts
    // again, so a label that blinks for a frame is not counted twice.
    internal const int RecountMs = 1000;

    internal static bool Observe(Dictionary<string, int> seen, Dictionary<(uint, string), long> shown,
        IReadOnlyList<Prompt> prompts, long now)
    {
        var changed = false;
        foreach (var prompt in prompts)
        {
            var key = (prompt.NodeId, prompt.Text);
            if (!shown.TryGetValue(key, out var last) || now - last > RecountMs)
            {
                seen[prompt.Text] = seen.GetValueOrDefault(prompt.Text) + 1;
                changed = true;
            }
            shown[key] = now;
        }
        foreach (var gone in shown.Where(kv => now - kv.Value > RecountMs).Select(kv => kv.Key).ToArray())
            shown.Remove(gone);
        return changed;
    }
}
