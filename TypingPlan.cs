using System.Globalization;
using System.Text;

namespace KeyboundAssist;

internal enum StepKind { Unavailable, Waiting, Typing, Ready }

internal readonly record struct Step(StepKind Kind, string Status, string Target = "", char Next = '\0',
    bool Defense = false);

// Pure decision logic: no Dalamud, no native memory. tests/ compiles this same file.
internal static class TypingPlan
{
    // The duty accepts an accented prompt typed as its base letter, and the base letter is what a
    // US layout can produce.
    internal static string Typeable(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    // The prompts that continue what is already in the field, best first: a barrier word the player has
    // to answer outranks everything, then the configured priority.
    internal static Prompt? Choose(IReadOnlyList<Prompt> prompts, string prefix, TargetPriority priority)
    {
        var wanted = Typeable(prefix);
        // A prompt that prepares to nothing can never be answered, so it is not a candidate at all -
        // otherwise it would match an empty field and report Ready with no text.
        var matches = prompts
            .Select(p => (Prompt: p, Text: Typeable(p.Text)))
            .Where(p => !p.Prompt.Grey && p.Text.Length > 0 && p.Text.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0) return null;
        // An unmatched prompt has no distance and sorts last, so nearest-enemy falls back to the
        // shortest prompt instead of guessing.
        var defenseFirst = matches.OrderByDescending(p => p.Prompt.Defense);
        var ordered = priority switch
        {
            TargetPriority.NearestEnemy => defenseFirst.ThenBy(p => p.Prompt.Distance ?? float.MaxValue),
            // Within a tier the enemy lowest on screen - closest to the barrier - goes first.
            TargetPriority.Threat => defenseFirst.ThenBy(p => Threat(p.Prompt)).ThenByDescending(p => p.Prompt.ScreenY),
            _ => defenseFirst,
        };
        ordered = ordered.ThenBy(p => p.Text.Length).ThenBy(p => p.Prompt.NodeId);
        return ordered.First().Prompt;
    }

    // AutoKeyboundBrawler's tiers below the barrier word (which already outranks every mode): a Cactuar on
    // Needles, then an enemy already attacking the barrier (AKB has no such tier - it is live HP loss), other
    // casting adds, idle Cactuars, regular enemies, then the boss - casting before idle.
    internal static int Threat(Prompt p) =>
        p.NodeId == PromptMap.BossSlot ? (p.Casting ? 6 : 7)
        : p.Needles ? 1 : p.AtBarrier ? 2 : p.Casting ? 3 : p.Cactuar ? 4 : 5;

    // A fresh draw per character, so the typing has no fixed cadence. Equal bounds mean a fixed
    // delay; a hand-edited config with min > max is swapped rather than throwing out of Random.Next.
    internal const int MaxDelayMs = 1000;
    // No faster than a quick human typist (~90 WPM): the plugin fills the field, so this is its only pace.
    internal const int MinDelayMs = 130;

    // Auto-send: never sooner than SubmitFloorMs after the last character, default 300-500 ms - one to two
    // keystroke gaps at the default typing pace, which is where a person's own Enter lands.
    internal const int SubmitFloorMs = 200;
    internal const int SubmitCapMs = 2000;

    internal static int DrawDelay(Random random, int minMs, int maxMs, int floor = MinDelayMs, int cap = MaxDelayMs)
    {
        if (minMs > maxMs) (minMs, maxMs) = (maxMs, minMs);
        minMs = Math.Clamp(minMs, floor, cap);
        maxMs = Math.Clamp(maxMs, minMs, cap);
        return random.Next(minMs, maxMs + 1);
    }

    // A prompt the plugin can actually answer: printable ASCII plus Latin letters, because an accented
    // prompt is typed as its base letter. Kanji, kana and Cyrillic belong to a client whose real answer
    // sits in a different label, so those words are skipped rather than typed wrong.
    internal static bool IsTypeableWord(string text) =>
        text.Length is > 0 and <= 160 &&
        text.All(c => c is >= ' ' and <= '~' || c is >= '\u00C0' and <= '\u024F');

    // True when the field shrank from the last text the plugin wrote: the player backspaced.
    // Case-insensitive, because the duty accepts either case for text we wrote.
    internal static bool PlayerDeleted(string field, string? lastWritten) =>
        lastWritten != null && lastWritten.Length > field.Length &&
        lastWritten.StartsWith(field, StringComparison.OrdinalIgnoreCase);

    // True when the field is exactly what this plugin's append path could have produced towards that
    // prompt, in prepared form. Anything else is the player's own text and is never cleared.
    internal static bool IsOwnedPrefix(string field, string target) =>
        field.Length > 0 &&
        Typeable(target).StartsWith(Typeable(field),
            StringComparison.OrdinalIgnoreCase);

    // The field is the authority on what has been typed: the plugin only ever appends the next
    // character, and never deletes or overwrites what the player put there.
    internal static Step Next(string field, IReadOnlyList<Prompt> prompts, TargetPriority priority,
        bool allowed, string reason)
    {
        if (!allowed) return new(StepKind.Unavailable, reason);
        if (field.Length == 0)
        {
            var fresh = Choose(prompts, "", priority);
            if (fresh == null) return new(StepKind.Waiting, "Waiting for an enemy prompt.");
            // The first character comes from the prepared form, never the drawn one, so an accented
            // opening letter is typed as its base letter.
            var opening = Typeable(fresh.Value.Text);
            return new(StepKind.Typing, $"Typing '{fresh.Value.Text}'.", fresh.Value.Text, opening[0],
                fresh.Value.Defense);
        }

        var match = Choose(prompts, field, priority);
        if (match == null)
            return new(StepKind.Waiting, $"The input holds '{field}'. Waiting for it to clear.");

        var typed = Typeable(field).Length;
        var target = Typeable(match.Value.Text);
        if (typed >= target.Length)
            return new(StepKind.Ready, $"'{match.Value.Text}' is typed. Press Enter.", match.Value.Text,
                Defense: match.Value.Defense);
        return new(StepKind.Typing, $"Typing '{match.Value.Text}'.", match.Value.Text, target[typed],
            match.Value.Defense);
    }
}
