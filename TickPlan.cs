namespace KeyboundAssist;

internal enum TickAction { None, Write, Clear, Submit }

// Everything the update loop decides between reading the field and writing to it, with no Dalamud and no
// native dependency. tests/ drives this exact function, so the order of the permission gate, the
// ownership proof and the clears is executed rather than reviewed.
// Sent is the word auto-send submitted, until the field empties; SubmitAt is when the pending send fires.
internal readonly record struct AssistState(
    string? LastWritten, bool WriteSeen, string? CurrentTarget, string? SkipTarget, long SkipUntil,
    long? OrphanSince = null, string? Sent = null, long? SubmitAt = null);

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
    // The field's own Enter callback, as a physical Enter would invoke it. False when it could not be invoked.
    bool Submit();
}

internal static class TickPlan
{
    internal const int SkipMs = 3000;

    // How long our partial text must have no prompt left to finish it before it is cleared: long enough to
    // ride out a label that drops for a frame or two, short next to one 130 ms character.
    internal const int OrphanMs = 400;

    internal static TickOutcome Step(string field, bool allowed, string reason, Prompt[] prompts,
        AssistState state, long now, TargetPriority priority, bool autoSubmit = false)
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
        // One Backspace cannot empty a field holding a whole multi-character word we wrote: that is the
        // Enter, even when a twin of the word is still on another enemy. A one-character word stays ambiguous.
        var submittedWhole = lastWritten != null && lastWritten.Length > 1 && currentTarget != null &&
            PreparedEqual(lastWritten, currentTarget);
        // A field emptied after auto-send is the submission, never a backspace, even for a one-letter word.
        var sent = state.Sent;
        var deleted = writeSeen && lastWritten != null &&
            (field.Length == 0 ? wordShown && !submittedWhole && sent == null : TypingPlan.PlayerDeleted(field, lastWritten));
        if (field.Length == 0)
        {
            lastWritten = null;
            writeSeen = false;
            sent = null;
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

        // The enemy carrying the word died mid-word: no prompt on screen continues the text we wrote, so it
        // would sit in the field forever. Clear it - only our own text, only while typing is allowed, and only
        // once it has stayed orphaned for OrphanMs. A prompt sharing the prefix is not an orphan; it continues.
        var owned = field.Length > 0 && writeSeen && lastWritten != null &&
            string.Equals(field, lastWritten, StringComparison.OrdinalIgnoreCase);
        var orphaned = owned && TypingPlan.Choose(prompts, field, priority) == null;
        long? orphanSince = orphaned ? state.OrphanSince ?? now : null;
        if (orphaned && allowed && now - orphanSince >= OrphanMs)
            return new TickOutcome(new AssistState(null, false, null, skipTarget, skipUntil),
                TickAction.Clear, '\0', $"Cleared '{field}' - its enemy is gone.");

        var step = TypingPlan.Next(field, pool, priority, allowed, reason);

        // A barrier word outranks the word in progress, but the field is the authority: when what is typed
        // can still lead into the barrier word, the plan simply continues into it. When it cannot, the
        // partial word is dropped - but only text this plugin wrote, only while typing is allowed, and
        // never a finished word, which is one Enter away from being submitted.
        var barrier = BestBarrier(pool);
        if (allowed && currentTarget != null && field.Length > 0 && barrier != null &&
            !string.Equals(barrier, currentTarget, StringComparison.OrdinalIgnoreCase) &&
            writeSeen && lastWritten != null && TypingPlan.IsOwnedPrefix(field, lastWritten) &&
            TypingPlan.IsOwnedPrefix(field, currentTarget) &&
            !TypingPlan.IsOwnedPrefix(field, barrier) &&
            !PreparedEqual(field, currentTarget))
            return new TickOutcome(new AssistState(null, false, null, skipTarget, skipUntil),
                TickAction.Clear, '\0', "Dropping the partial word for a barrier prompt.");

        // Auto-send only a finished word this plugin typed and has read back, once: the player's own word is his
        // to send, and a word the duty left in the field after a send is not sent again.
        var sendable = autoSubmit && step.Kind == StepKind.Ready && owned && sent == null;
        var nextState = new AssistState(lastWritten, writeSeen,
            step.Kind is StepKind.Typing or StepKind.Ready ? step.Target : null, skipTarget, skipUntil, orphanSince,
            sent, sendable ? state.SubmitAt : null);
        if (sendable) return new TickOutcome(nextState, TickAction.Submit, '\0', step.Status);
        return step.Kind == StepKind.Typing
            ? new TickOutcome(nextState, TickAction.Write, step.Next, step.Status)
            : new TickOutcome(nextState, TickAction.None, '\0', step.Status);
    }

    // The barrier word the plan would answer first: the shortest, as TypingPlan.Choose ranks it.
    private static string? BestBarrier(Prompt[] pool) =>
        pool.Where(p => p.Defense)
            .OrderBy(p => TypingPlan.Typeable(p.Text).Length)
            .ThenBy(p => p.NodeId)
            .Select(p => p.Text)
            .FirstOrDefault();

    private static bool PreparedEqual(string a, string b) =>
        string.Equals(TypingPlan.Typeable(a), TypingPlan.Typeable(b),
            StringComparison.OrdinalIgnoreCase);

    // Applies one decision to the field: clear, or focus and then append a single character. Every write
    // the plugin can make goes through here.
    internal static AssistState Apply(TickOutcome outcome, string field, IInputField input, long now,
        ref long nextWriteAt, ref long nextFocusAt, int minMs, int maxMs, Random random, out string status,
        bool chatFocused = false, int submitMinMs = 300, int submitMaxMs = 500)
    {
        status = outcome.Status;
        if (outcome.Clear)
        {
            input.SetText("");
            return outcome.State;
        }
        if (outcome.Action == TickAction.Submit)
        {
            // The delay is drawn when the word is first seen finished, so a send never follows the last letter at once.
            if (outcome.State.SubmitAt is not { } at)
            {
                var delay = TypingPlan.DrawDelay(random, submitMinMs, submitMaxMs, TypingPlan.SubmitFloorMs, TypingPlan.SubmitCapMs);
                status = $"'{field}' is typed. Sending in {delay} ms.";
                return outcome.State with { SubmitAt = now + delay };
            }
            if (now < at) return outcome.State;
            if (!input.IsActive || chatFocused)
            {
                status = chatFocused ? "Waiting to send - the chat input is in use." : "Waiting to send - the activity input is not active.";
                return outcome.State;
            }
            if (!input.Submit())
            {
                status = "Could not send the word; press Enter.";
                return outcome.State with { Sent = field, SubmitAt = null };
            }
            status = $"Sent '{field}'.";
            return outcome.State with { Sent = field, SubmitAt = null };
        }
        if (outcome.Action != TickAction.Write) return outcome.State;

        // The player's Enter only reaches the field while it is the active text input.
        if (!input.IsActive)
        {
            // Pulling focus now would steal the chat line the player is typing in.
            if (chatFocused)
            {
                status = "Waiting - the chat input is in use.";
                return outcome.State;
            }
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
