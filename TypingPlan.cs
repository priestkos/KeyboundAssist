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

    // The duty scores per submitted word, so the shortest prompt is the cheapest one to finish.
    internal static string? Choose(IReadOnlyList<string> words, string prefix)
    {
        string? best = null;
        var wanted = Typeable(prefix);
        foreach (var word in words)
        {
            if (!Typeable(word).StartsWith(wanted, StringComparison.OrdinalIgnoreCase)) continue;
            if (best == null || word.Length < best.Length) best = word;
        }
        return best;
    }

    // The field is the authority on what has been typed: the plugin only ever appends the next
    // character, and never deletes or overwrites what the player put there.
    internal static Step Next(string field, IReadOnlyList<string> words, bool allowed, string reason)
    {
        if (!allowed) return new(StepKind.Unavailable, reason);
        if (field.Length == 0)
        {
            var fresh = Choose(words, "");
            return fresh == null
                ? new(StepKind.Waiting, "Waiting for an enemy prompt.")
                : new(StepKind.Typing, $"Typing '{fresh}'.", fresh, fresh[0]);
        }

        var match = Choose(words, field);
        if (match == null)
            return new(StepKind.Waiting, $"The input holds '{field}'. Waiting for it to clear.");

        var typed = Typeable(field).Length;
        var target = Typeable(match);
        if (typed >= target.Length)
            return new(StepKind.Ready, $"'{match}' is typed. Press Enter.", match);
        return new(StepKind.Typing, $"Typing '{match}'.", match, target[typed]);
    }
}
