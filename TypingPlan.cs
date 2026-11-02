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
    internal static Prompt? Choose(IReadOnlyList<Prompt> prompts, string prefix, TargetPriority priority,
        bool ignorePunctuation)
    {
        var wanted = Prepared(prefix, ignorePunctuation);
        // A prompt that prepares to nothing ("!!", a stray symbol) can never be answered, so it is not a
        // candidate at all - otherwise it would match an empty field and report Ready with no text.
        var matches = prompts
            .Select(p => (Prompt: p, Text: Prepared(p.Text, ignorePunctuation)))
            .Where(p => p.Text.Length > 0 && p.Text.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0) return null;
        // An unmatched prompt has no distance and sorts last, so nearest-enemy falls back to the
        // shortest prompt instead of guessing.
        var ordered = priority == TargetPriority.NearestEnemy
            ? matches.OrderByDescending(p => p.Prompt.Defense)
                .ThenBy(p => p.Prompt.Distance ?? float.MaxValue)
                .ThenBy(p => p.Text.Length).ThenBy(p => p.Prompt.NodeId)
            : matches.OrderByDescending(p => p.Prompt.Defense)
                .ThenBy(p => p.Text.Length).ThenBy(p => p.Prompt.NodeId);
        return ordered.First().Prompt;
    }

    // Accents and the punctuation the duty ignores are removed before anything is compared or typed, so
    // the field, the prompt and the display all agree on what "typed so far" means.
    internal static string Prepared(string text, bool ignorePunctuation)
    {
        var typeable = Typeable(text);
        if (!ignorePunctuation) return typeable;
        var builder = new StringBuilder(typeable.Length);
        foreach (var c in typeable)
            if (char.IsLetterOrDigit(c) || c == ' ') builder.Append(c);
        return builder.ToString();
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

    // The field is the authority on what has been typed: the plugin only ever appends the next
    // character, and never deletes or overwrites what the player put there.
    internal static Step Next(string field, IReadOnlyList<Prompt> prompts, TargetPriority priority,
        bool ignorePunctuation, bool allowed, string reason)
    {
        if (!allowed) return new(StepKind.Unavailable, reason);
        if (field.Length == 0)
        {
            var fresh = Choose(prompts, "", priority, ignorePunctuation);
            if (fresh == null) return new(StepKind.Waiting, "Waiting for an enemy prompt.");
            // The first character comes from the prepared form, never the drawn one: a prompt that opens
            // with punctuation would otherwise type the punctuation and lose the field.
            var opening = Prepared(fresh.Value.Text, ignorePunctuation);
            return new(StepKind.Typing, $"Typing '{fresh.Value.Text}'.", fresh.Value.Text, opening[0],
                fresh.Value.Defense);
        }

        var match = Choose(prompts, field, priority, ignorePunctuation);
        if (match == null)
            return new(StepKind.Waiting, $"The input holds '{field}'. Waiting for it to clear.");

        var typed = Prepared(field, ignorePunctuation).Length;
        var target = Prepared(match.Value.Text, ignorePunctuation);
        if (typed >= target.Length)
            return new(StepKind.Ready, $"'{match.Value.Text}' is typed. Press Enter.", match.Value.Text,
                Defense: match.Value.Defense);
        return new(StepKind.Typing, $"Typing '{match.Value.Text}'.", match.Value.Text, target[typed],
            match.Value.Defense);
    }
}
