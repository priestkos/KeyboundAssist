namespace KeyboundAssist;

// One frame's inputs, gathered from the game as plain values, so the gate order, the decision and the write
// can be executed without a client.
internal readonly record struct FrameContext(
    bool Halted, string HaltReason, bool SceneChanging,
    string Field, bool Allowed, string Reason, Prompt[] Prompts, bool ChatFocused = false);

internal readonly record struct FrameSettings(
    TargetPriority Priority, int MinDelayMs, int MaxDelayMs,
    bool AutoSubmit = false, int SubmitMinMs = 300, int SubmitMaxMs = 500);

internal static class Frame
{
    internal static (AssistState State, string Status) Run(FrameContext context, FrameSettings settings,
        AssistState state, IInputField input, long now, ref long nextWriteAt, ref long nextFocusAt,
        Random random)
    {
        // The locking cast is the early warning; the field's own lock only reports it once it has engaged.
        // A pending send is dropped on either gate and re-armed with a fresh delay once it lifts.
        if (context.Halted) return (state with { SubmitAt = null }, context.HaltReason);
        // A wave transition still looks like an active duty, but nobody can type during one.
        if (context.SceneChanging) return (state with { SubmitAt = null }, "Waiting - the duty is changing scenes.");

        var outcome = TickPlan.Step(context.Field, context.Allowed, context.Reason, context.Prompts, state, now,
            settings.Priority, settings.AutoSubmit);
        var next = TickPlan.Apply(outcome, context.Field, input, now, ref nextWriteAt, ref nextFocusAt,
            settings.MinDelayMs, settings.MaxDelayMs, random, out var status, context.ChatFocused,
            settings.SubmitMinMs, settings.SubmitMaxMs);
        return (next, status);
    }
}
