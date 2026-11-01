using System.Globalization;
using System.Text;

namespace KeyboundAssist;

internal enum StepKind { Unavailable, Waiting, Typing, Ready }

internal readonly record struct Step(StepKind Kind, string Status, string Target = "", char Next = '\0');

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

    // The prompts that continue what is already in the field. One prompt is aimed at per word:
    // whichever the configured priority puts first.
    internal static Prompt? Choose(IReadOnlyList<Prompt> prompts, string prefix, TargetPriority priority)
    {
        var wanted = Typeable(prefix);
        var matches = prompts
            .Where(p => Typeable(p.Text).StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0) return null;
        // An unmatched prompt has no distance and sorts last, so nearest-enemy falls back to the
        // shortest prompt instead of guessing.
        return priority == TargetPriority.NearestEnemy
            ? matches.OrderBy(p => p.Distance ?? float.MaxValue).ThenBy(p => p.Text.Length).ThenBy(p => p.NodeId).First()
            : matches.OrderBy(p => p.Text.Length).ThenBy(p => p.NodeId).First();
    }

    // A fresh draw per character, so the typing has no fixed cadence. Equal bounds mean a fixed
    // delay; a hand-edited config with min > max is swapped rather than throwing out of Random.Next.
    internal const int MaxDelayMs = 1000;

    internal static int DrawDelay(Random random, int minMs, int maxMs)
    {
        if (minMs > maxMs) (minMs, maxMs) = (maxMs, minMs);
        minMs = Math.Clamp(minMs, 0, MaxDelayMs);
        maxMs = Math.Clamp(maxMs, minMs, MaxDelayMs);
        return random.Next(minMs, maxMs + 1);
    }

    // The field is the authority on what has been typed: the plugin only ever appends the next
    // character, and never deletes or overwrites what the player put there.
    internal static Step Next(string field, IReadOnlyList<Prompt> prompts, TargetPriority priority,
        bool allowed, string reason)
    {
        if (!allowed) return new(StepKind.Unavailable, reason);
        if (field.Length == 0)
        {
            var fresh = Choose(prompts, "", priority);
            return fresh == null
                ? new(StepKind.Waiting, "Waiting for an enemy prompt.")
                : new(StepKind.Typing, $"Typing '{fresh.Value.Text}'.", fresh.Value.Text, fresh.Value.Text[0]);
        }

        var match = Choose(prompts, field, priority);
        if (match == null)
            return new(StepKind.Waiting, $"The input holds '{field}'. Waiting for it to clear.");

        var typed = Typeable(field).Length;
        var target = Typeable(match.Value.Text);
        if (typed >= target.Length)
            return new(StepKind.Ready, $"'{match.Value.Text}' is typed. Press Enter.", match.Value.Text);
        return new(StepKind.Typing, $"Typing '{match.Value.Text}'.", match.Value.Text, target[typed]);
    }
}
