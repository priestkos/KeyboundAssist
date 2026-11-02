namespace KeyboundAssist;

internal enum TickAction { None, Write, Clear }

// Everything the update loop decides between reading the field and writing to it, with no Dalamud and no
// native dependency. tests/ drives this exact function, so the order of the permission gate, the
// ownership proof and the clears is executed rather than reviewed.
internal readonly record struct AssistState(
    string? LastWritten, bool WriteSeen, string? CurrentTarget, string? SkipTarget, long SkipUntil);

internal readonly record struct TickOutcome(AssistState State, TickAction Action, char Next, string Status)
{
    internal bool Clear => Action == TickAction.Clear;
}

// The one place a decision reaches the game. Production passes the native component; tests pass a fake
// that records what was written, so the clear/write/focus sequence is executed rather than reviewed.
internal interface IInputField
{
    bool IsActive { get; }
    bool Focus();
    void SetText(string text);
}

internal static class TickPlan
{
    internal const int SkipMs = 3000;

    internal static TickOutcome Step(string field, bool allowed, string reason, Prompt[] prompts,
        AssistState state, long now, TargetPriority priority, bool ignorePunctuation)
    {
        var pool = prompts;
        var skipTarget = state.SkipTarget;
        var skipUntil = state.SkipUntil;
        if (skipTarget != null)
        {
            if (now < skipUntil) pool = prompts.Where(p => p.Text != skipTarget).ToArray();
            else skipTarget = null;
        }

        // Ownership: only text this plugin wrote, and has since read back from the field, is ours.
        var lastWritten = state.LastWritten;
        var writeSeen = state.WriteSeen;
        if (lastWritten != null && string.Equals(field, lastWritten, StringComparison.OrdinalIgnoreCase))
            writeSeen = true;

        // A backspace over our own text empties the field and skips the word, or the next frame writes the
        // character straight back. An empty field is a deletion only while its word is still on screen:
        // once the duty consumes a submitted word the prompt is gone, and that must skip nothing.
        var currentTarget = state.CurrentTarget;
        var wordShown = currentTarget != null &&
            pool.Any(p => string.Equals(p.Text, currentTarget, StringComparison.OrdinalIgnoreCase));
        var deleted = writeSeen && lastWritten != null &&
            (field.Length == 0 ? wordShown : TypingPlan.PlayerDeleted(field, lastWritten));
        if (field.Length == 0)
        {
            lastWritten = null;
            writeSeen = false;
        }
        if (deleted)
        {
            if (!allowed)
            {
                // Locked: leave the field exactly as the player left it, and let the plan report the lock.
                currentTarget = null;
            }
            else
            {
                return new TickOutcome(new AssistState(null, false, null, currentTarget, now + SkipMs),
                    TickAction.Clear, '\0',
                    currentTarget == null ? "Cleared the input." : $"Cleared the input; skipping '{currentTarget}' for a moment.");
            }
        }

        var step = TypingPlan.Next(field, pool, priority, ignorePunctuation, allowed, reason);

        // A barrier word outranks the word in progress, but the field is the authority: when what is typed
        // can still lead into the barrier word, the plan simply continues into it. When it cannot, the
        // partial word is dropped - but only text this plugin wrote, only while typing is allowed, and
        // never a finished word, which is one Enter away from being submitted.
        var barrier = BestBarrier(pool, ignorePunctuation);
        if (allowed && currentTarget != null && field.Length > 0 && barrier != null &&
            !string.Equals(barrier, currentTarget, StringComparison.OrdinalIgnoreCase) &&
            TypingPlan.IsOwnedPrefix(field, currentTarget, ignorePunctuation) &&
            !TypingPlan.IsOwnedPrefix(field, barrier, ignorePunctuation) &&
            !PreparedEqual(field, currentTarget, ignorePunctuation))
            return new TickOutcome(new AssistState(null, false, null, skipTarget, skipUntil),
                TickAction.Clear, '\0', "Dropping the partial word for a barrier prompt.");

        var nextState = new AssistState(lastWritten, writeSeen,
            step.Kind is StepKind.Typing or StepKind.Ready ? step.Target : null, skipTarget, skipUntil);
        return step.Kind == StepKind.Typing
            ? new TickOutcome(nextState, TickAction.Write, step.Next, step.Status)
            : new TickOutcome(nextState, TickAction.None, '\0', step.Status);
    }

    // The barrier word the plan would answer first: the shortest, as TypingPlan.Choose ranks it.
    private static string? BestBarrier(Prompt[] pool, bool ignorePunctuation) =>
        pool.Where(p => p.Defense)
            .OrderBy(p => TypingPlan.Prepared(p.Text, ignorePunctuation).Length)
            .ThenBy(p => p.NodeId)
            .Select(p => p.Text)
            .FirstOrDefault();

    private static bool PreparedEqual(string a, string b, bool ignorePunctuation) =>
        string.Equals(TypingPlan.Prepared(a, ignorePunctuation), TypingPlan.Prepared(b, ignorePunctuation),
            StringComparison.OrdinalIgnoreCase);

    // Applies one decision to the field: clear, or focus and then append a single character. Every write
    // the plugin can make goes through here.
    internal static AssistState Apply(TickOutcome outcome, string field, IInputField input, long now,
        ref long nextWriteAt, ref long nextFocusAt, int minMs, int maxMs, Random random, out string status)
    {
        status = outcome.Status;
        if (outcome.Clear)
        {
            input.SetText("");
            return outcome.State;
        }
        if (outcome.Action != TickAction.Write) return outcome.State;

        // The player's Enter only reaches the field while it is the active text input.
        if (!input.IsActive)
        {
            if (now < nextFocusAt) return outcome.State;
            nextFocusAt = now + 250;
            if (!input.Focus()) status = "Could not focus the activity input.";
            return outcome.State;
        }

        if (now < nextWriteAt) return outcome.State;
        nextWriteAt = now + TypingPlan.DrawDelay(random, minMs, maxMs);
        var written = field + outcome.Next;
        input.SetText(written);
        return outcome.State with { LastWritten = written, WriteSeen = false };
    }
}
