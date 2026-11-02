using System.Numerics;
using KeyboundAssist;

var checks = 0;
var failures = 0;

void Check(string name, bool condition, string detail)
{
    checks++;
    if (condition) return;
    failures++;
    Console.WriteLine($"FAIL {name}: {detail}");
}

Prompt[] W(params string[] words) =>
    words.Select(w => new Prompt(0, w, false, 0, 0, 1f, null)).ToArray();

Prompt P(string text, float? distance = null, uint node = 0, bool defense = false) =>
    new(node, text, false, 0, 0, 1f, distance, defense);
Prompt Pm(float x, float y, float scale, uint node) => new(node, "w" + node, false, x, y, scale, null);

Step Next(string field, Prompt[] prompts, TargetPriority priority = TargetPriority.ShortestPrompt,
    bool allowed = true, string reason = "prohibited", bool ignorePunctuation = true) =>
    TypingPlan.Next(field, prompts, priority, ignorePunctuation, allowed, reason);

// Empty field: pick the shortest prompt and start it.
var step = Next("", W("nidhogg", "ifrit", "bomb"));
Check("shortest-first", step.Kind == StepKind.Typing && step.Target == "bomb" && step.Next == 'b', step.ToString());

// Empty field, no prompts: wait.
step = Next("", W());
Check("no-prompts", step.Kind == StepKind.Waiting, step.ToString());

// Prefix already typed: continue at the right character, case-insensitively.
step = Next("nid", W("Nidhogg", "ifrit"));
Check("prefix-advance", step.Kind == StepKind.Typing && step.Target == "Nidhogg" && step.Next == 'h', step.ToString());

// Complete word: stop and hand the Enter to the player.
step = Next("nidhogg", W("nidhogg", "ifrit"));
Check("complete", step.Kind == StepKind.Ready && step.Target == "nidhogg", step.ToString());

// A longer field matches nothing: never touch text the plugin did not type.
step = Next("nidhogging", W("nidhogg"));
Check("stranger-text", step.Kind == StepKind.Waiting, step.ToString());

// Text that is a prefix of no visible prompt waits for the field to clear.
step = Next("zzz", W("nidhogg", "ifrit"));
Check("no-match", step.Kind == StepKind.Waiting, step.ToString());

// Accents are typed as their base letter, and the base-letter field counts as complete.
step = Next("", W("café", "bomb"));
Check("accent-first", step.Kind == StepKind.Typing && step.Next == 'c' && step.Target == "café", step.ToString());
step = Next("cafe", W("café"));
Check("accent-complete", step.Kind == StepKind.Ready, step.ToString());
step = Next("caf", W("café"));
Check("accent-advance", step.Kind == StepKind.Typing && step.Next == 'e', step.ToString());

// A prompt that vanished mid-word leaves the field alone.
step = Next("nid", W("ifrit"));
Check("vanished", step.Kind == StepKind.Waiting, step.ToString());

// The duty's own lock wins over everything, including a complete word.
step = Next("nidhogg", W("nidhogg"), allowed: false, reason: "Typing prohibited (3s).");
Check("lock", step.Kind == StepKind.Unavailable && step.Status == "Typing prohibited (3s).", step.ToString());

// Case-only difference between the field and the prompt still counts as typed.
step = Next("NIDHOGG", W("nidhogg"));
Check("case-insensitive", step.Kind == StepKind.Ready, step.ToString());

// Nearest enemy overrides length; a prompt with no distance never wins on a guess.
Prompt[] near = [P("bomb", 9f), P("ifrit", 2f)];
step = Next("", near);
Check("shortest-is-default", step.Target == "bomb", step.ToString());
step = Next("", near, TargetPriority.NearestEnemy);
Check("nearest-beats-shortest", step.Target == "ifrit", step.ToString());
step = Next("", [P("aa"), P("bbb", 3f)], TargetPriority.NearestEnemy);
Check("nearest-skips-unmatched", step.Target == "bbb", step.ToString());

// The player's own prefix restricts the choice before priority does.
step = Next("n", [P("bomb", 1f), P("nidhogg", 30f), P("nilbog", 8f)], TargetPriority.NearestEnemy);
Check("nearest-honours-prefix", step.Target == "nilbog", step.ToString());

// A label above an enemy is matched to it and gets the real distance.
var mapped = PromptMap.WithDistances([Pm(100, 150, 1f, 1)],
    [new ActorScreen(7, new Vector2(102, 200), new Vector3(0, 0, 10))], Vector3.Zero);
Check("map-match", mapped[0].Distance is { } d && Math.Abs(d - 10f) < 0.01f, mapped[0].ToString());

// Two enemies 6 px apart in screen X are ambiguous: no distance rather than a guess.
mapped = PromptMap.WithDistances([Pm(100, 150, 1f, 1)],
    [new ActorScreen(7, new Vector2(100, 200), new Vector3(0, 0, 10)),
     new ActorScreen(8, new Vector2(106, 200), new Vector3(0, 0, 20))], Vector3.Zero);
Check("map-ambiguous", mapped[0].Distance == null, mapped[0].ToString());

// Two labels claiming one enemy resolve neither of them.
mapped = PromptMap.WithDistances([Pm(100, 150, 1f, 1), Pm(101, 150, 1f, 2)],
    [new ActorScreen(7, new Vector2(100, 200), new Vector3(0, 0, 10))], Vector3.Zero);
Check("map-contested", mapped[0].Distance == null && mapped[1].Distance == null, string.Join(" ", mapped));

// The same two refusals above the scale-estimate threshold: a rejected match must not quietly regain a
// guessed distance from its label size (the 1.0-scale cases above sit below that threshold and miss it).
mapped = PromptMap.WithDistances([Pm(100, 150, 1.5f, 1)],
    [new ActorScreen(7, new Vector2(100, 200), new Vector3(0, 0, 10)),
     new ActorScreen(8, new Vector2(106, 200), new Vector3(0, 0, 20))], Vector3.Zero);
Check("map-ambiguous-scaled", mapped[0].Distance == null, mapped[0].ToString());

mapped = PromptMap.WithDistances([Pm(100, 150, 1.5f, 1), Pm(101, 150, 1.4f, 2)],
    [new ActorScreen(7, new Vector2(100, 200), new Vector3(0, 0, 10))], Vector3.Zero);
Check("map-contested-scaled", mapped[0].Distance == null && mapped[1].Distance == null,
    string.Join(" ", mapped));

// Label scale carries a distance estimate until it saturates (calibrated live).
Check("estimate-live", PromptMap.EstimateDistance(1.50f) is { } e && Math.Abs(e - 12.73f) < 0.05f,
    PromptMap.EstimateDistance(1.50f)?.ToString() ?? "null");
Check("estimate-saturated", PromptMap.EstimateDistance(1.10f) == null, "expected null");

// A label with no matched enemy still gets the scale proxy, so nearest mode can rank it.
var proxy = PromptMap.WithDistances([Pm(100, 150, 1.5f, 1)], [], Vector3.Zero);
Check("map-scale-proxy", proxy[0].Distance is { } pd && Math.Abs(pd - 12.73f) < 0.05f, proxy[0].ToString());

// Per-character delay: a fresh draw inside the range, equal ends fixed, bad bounds tolerated.
var rng = new Random(1234);
var draws = Enumerable.Range(0, 400).Select(_ => TypingPlan.DrawDelay(rng, 30, 60)).ToArray();
Check("delay-in-range", draws.All(d => d is >= 30 and <= 60), $"min={draws.Min()} max={draws.Max()}");
Check("delay-varies", draws.Distinct().Count() > 1, $"distinct={draws.Distinct().Count()}");
Check("delay-fixed-when-equal", TypingPlan.DrawDelay(rng, 40, 40) == 40, "expected 40");
Check("delay-swapped-bounds", TypingPlan.DrawDelay(rng, 60, 30) is >= 30 and <= 60, "expected 30..60");
Check("delay-clamps-out-of-range", TypingPlan.DrawDelay(rng, -20, 10) is >= 0 and <= 10, "expected 0..10");
Check("delay-allows-the-cap", TypingPlan.DrawDelay(rng, 900, 1500) is >= 900 and <= 1000, "expected 900..1000");
Check("delay-at-cap-is-fixed", TypingPlan.DrawDelay(rng, 1000, 1000) == 1000, "expected 1000");

// Backspace detection: only a shrink from text the plugin wrote counts as the player deleting.
Check("backspace-detected", TypingPlan.PlayerDeleted("nidhog", "nidhogg"), "expected true");
Check("backspace-not-ours", !TypingPlan.PlayerDeleted("nidhog", null), "expected false");
Check("backspace-complete-word", !TypingPlan.PlayerDeleted("nidhogg", "nidhogg"), "expected false");

// Prompt filter: only words this plugin can actually answer.
Check("word-ascii", TypingPlan.IsTypeableWord("nidhogg"), "expected true");
Check("word-accented", TypingPlan.IsTypeableWord("café"), "expected true");
Check("word-punctuated", TypingPlan.IsTypeableWord("don't-stop!"), "expected true");
Check("word-kana-skipped", !TypingPlan.IsTypeableWord("レッド"), "expected false");
Check("word-kanji-skipped", !TypingPlan.IsTypeableWord("漢字"), "expected false");
Check("word-empty-skipped", !TypingPlan.IsTypeableWord(""), "expected false");

// Punctuation the duty ignores is dropped before comparing and typing.
Check("punct-first-char", Next("", [P("don't-stop!")]).Next == 'd', "expected 'd'");
Check("punct-ready", Next("dontstop", [P("don't-stop!")]).Kind == StepKind.Ready, "expected ready");
Check("punct-exact-mode",
    Next("dont", [P("don't-stop!")], ignorePunctuation: false).Kind == StepKind.Waiting, "expected waiting");
Check("punct-prepared", TypingPlan.Prepared("a b1-c!", true) == "a b1c", TypingPlan.Prepared("a b1-c!", true));

// A prompt that opens with punctuation must still start on a letter, and a prompt that is nothing but
// punctuation can never be answered.
step = Next("", [P("'ello")]);
Check("punct-leading-first-char", step.Next == 'e', $"expected 'e', got '{step.Next}'");
Check("punct-only-skipped", Next("", [P("!!!")]).Kind == StepKind.Waiting, "expected waiting");
Check("punct-only-beside-real", Next("", [P("!!!"), P("real")]).Target == "real", "expected real");

// A barrier word outranks the configured priority even when it is longer than everything else.
Prompt[] barrier = [P("alpha"), P("longer defense line!", 5f, 9, defense: true)];
step = Next("", barrier);
Check("defense-first", step.Target == "longer defense line!", step.ToString());
Check("defense-flag-carried", step.Defense, "expected the chosen step to be marked defensive");
step = Next("", barrier, TargetPriority.NearestEnemy);
Check("defense-first-nearest", step.Target == "longer defense line!", step.ToString());

// The barrier line scales with the window instead of assuming 1080p.
Check("defense-line-1080", PromptMap.IsDefensePrompt(734, 1080) && !PromptMap.IsDefensePrompt(680, 1080),
    "734/1080 should be defensive, 680/1080 should not");
Check("defense-line-1440", PromptMap.IsDefensePrompt(934, 1440) && !PromptMap.IsDefensePrompt(900, 1440),
    "934/1440 should be defensive, 900/1440 should not");
Check("defense-line-unknown", !PromptMap.IsDefensePrompt(900, 0), "a zero height must not mark everything");

// A barrier word must be able to finish: at every prefix the plan names the same barrier target, which
// is what keeps preemption from clearing the barrier's own progress frame after frame.
Prompt[] onlyBarrier = [P("shield", null, 5, defense: true)];
var built = "";
var completes = true;
while (built.Length < "shield".Length)
{
    var nextStep = Next(built, onlyBarrier);
    if (nextStep.Kind != StepKind.Typing || nextStep.Target != "shield") { completes = false; break; }
    built += nextStep.Next;
}
Check("barrier-word-completes", completes && built == "shield" && Next(built, onlyBarrier).Kind == StepKind.Ready,
    built);

// Accents are prepared before the first character is taken, not only after it.
Check("accent-first-char", Next("", [P("éclair")]).Next == 'e', Next("", [P("éclair")]).ToString());

// Ownership: only a prefix of the target can be text the plugin wrote.
Check("owned-prefix-yes", TypingPlan.IsOwnedPrefix("b", "bomb", true), "expected owned");
Check("owned-prefix-no", !TypingPlan.IsOwnedPrefix("my note", "bomb", true), "expected not owned");
Check("owned-prefix-punct", TypingPlan.IsOwnedPrefix("dont", "don't-stop!", true), "expected owned");

// ---- The update loop itself, driven through the same TickPlan.Step the plugin calls every frame. ----

// F1: a barrier word completes under preemption instead of being cleared on its own progress.
var state = new AssistState(null, false, null, null, 0);
Prompt[] barrierOnly = [P("shield", null, 5, defense: true)];
var typedSoFar = "";
var clears = 0;
for (var frame = 0; frame < 12; frame++)
{
    var outcome = TickPlan.Step(typedSoFar, true, "Ready.", barrierOnly, state, 1000 + frame * 20,
        TargetPriority.ShortestPrompt, true);
    state = outcome.State;
    if (outcome.Clear) { clears++; typedSoFar = ""; continue; }
    if (outcome.Action != TickAction.Write) break;
    typedSoFar += outcome.Next;                                  // the frame's write lands
    state = state with { LastWritten = typedSoFar, WriteSeen = false };
}
Check("loop-barrier-completes", clears == 0 && typedSoFar == "shield", $"'{typedSoFar}', {clears} clears");

// F2: nothing is cleared while the duty prohibits typing - neither the takeover nor the backspace.
var lockedPartial = new AssistState("b", true, "bomb", null, 0);
var locked = TickPlan.Step("b", false, "Typing prohibited (3s).",
    [P("bomb"), P("shield", null, 9, defense: true)], lockedPartial, 5000, TargetPriority.ShortestPrompt, true);
Check("loop-locked-takeover-no-clear", !locked.Clear && locked.Action == TickAction.None, locked.ToString());

var lockedDeleted = new AssistState("bomb", true, "bomb", null, 0);
var lockedDel = TickPlan.Step("bom", false, "Typing prohibited (3s).", [P("bomb")], lockedDeleted, 5000,
    TargetPriority.ShortestPrompt, true);
Check("loop-locked-backspace-no-clear", !lockedDel.Clear && lockedDel.Action == TickAction.None,
    lockedDel.ToString());

// F3: text the player put there is never cleared, even when a barrier word is waiting.
var replaced = new AssistState("b", true, "bomb", null, 0);
var kept = TickPlan.Step("my note", true, "Ready.", [P("bomb"), P("shield", null, 9, defense: true)],
    replaced, 5000, TargetPriority.ShortestPrompt, true);
Check("loop-player-text-kept", !kept.Clear && kept.Action != TickAction.Write, kept.ToString());

// F1's other half: the same barrier does take over a normal word that is only half typed.
var midWord = new AssistState("bo", true, "bomb", null, 0);
var preempt = TickPlan.Step("bo", true, "Ready.", [P("bomb"), P("shield", null, 9, defense: true)], midWord,
    5000, TargetPriority.ShortestPrompt, true);
Check("loop-barrier-preempts", preempt.Clear, preempt.ToString());

// ...but a barrier word the typed text can still lead into is continued, not thrown away.
var sharedPrefix = TickPlan.Step("s", true, "Ready.", [P("sundae"), P("shield", null, 9, defense: true)],
    new AssistState("s", true, "sundae", null, 0), 5000, TargetPriority.ShortestPrompt, true);
Check("loop-barrier-shares-prefix", !sharedPrefix.Clear && sharedPrefix.State.CurrentTarget == "shield",
    sharedPrefix.ToString());

// F5: deleting the last remaining character of a one-character prompt clears and skips it.
var oneChar = new AssistState("x", true, "x", null, 0);
var lastChar = TickPlan.Step("", true, "Ready.", [P("x")], oneChar, 5000, TargetPriority.ShortestPrompt, true);
Check("loop-last-char-delete-skips", lastChar.Clear && lastChar.State.SkipTarget == "x", lastChar.ToString());

// A submitted word empties the field too; that is not a deletion and must skip nothing.
var submitted = new AssistState("bomb", true, "bomb", null, 0);
var afterSubmit = TickPlan.Step("", true, "Ready.", [P("next")], submitted, 5000,
    TargetPriority.ShortestPrompt, true);
Check("loop-submission-no-skip",
    !afterSubmit.Clear && afterSubmit.Action == TickAction.Write && afterSubmit.State.SkipTarget == null,
    afterSubmit.ToString());

// A skipped word stays out of the pool for the window, then comes back.
var skipping = TickPlan.Step("", true, "Ready.", [P("bomb")], new AssistState(null, false, null, "bomb", 6000),
    5000, TargetPriority.ShortestPrompt, true);
Check("loop-skip-window", skipping.Action == TickAction.None, skipping.ToString());
var skipExpired = TickPlan.Step("", true, "Ready.", [P("bomb")], skipping.State, 7000,
    TargetPriority.ShortestPrompt, true);
Check("loop-skip-expires", skipExpired.Action == TickAction.Write, skipExpired.ToString());

// ---- The handler's write path, through the same TickPlan.Apply the plugin calls every frame. ----

// A barrier word is typed character by character and never cleared on its own progress.
var field = new FakeField();
var liveState = new AssistState(null, false, null, null, 0);
long nextWrite = 0, nextFocus = 0;
var mirror = "";
for (var frame = 0; frame < 12; frame++)
{
    var at = 1000 + frame * 500;
    var tick = TickPlan.Step(mirror, true, "Ready.", barrierOnly, liveState, at,
        TargetPriority.ShortestPrompt, true);
    if (tick.Action == TickAction.None) break;
    liveState = TickPlan.Apply(tick, mirror, field, at, ref nextWrite, ref nextFocus, 0, 0, new Random(7), out _);
    mirror = tick.Clear ? "" : field.Writes[^1];
}
Check("handler-barrier-writes", string.Join(",", field.Writes) == "s,sh,shi,shie,shiel,shield",
    string.Join(",", field.Writes));

// Nothing is written while the duty prohibits typing.
var lockedField = new FakeField();
var lockedTick = TickPlan.Step("b", false, "Typing prohibited (3s).",
    [P("bomb"), P("shield", null, 9, defense: true)], lockedPartial, 5000, TargetPriority.ShortestPrompt, true);
TickPlan.Apply(lockedTick, "b", lockedField, 5000, ref nextWrite, ref nextFocus, 0, 0, new Random(7), out _);
Check("handler-locked-no-write", lockedField.Writes.Count == 0, string.Join(",", lockedField.Writes));

// Text the player replaced is never written over.
var playerField = new FakeField();
var playerTick = TickPlan.Step("my note", true, "Ready.", [P("bomb"), P("shield", null, 9, defense: true)],
    replaced, 5000, TargetPriority.ShortestPrompt, true);
TickPlan.Apply(playerTick, "my note", playerField, 5000, ref nextWrite, ref nextFocus, 0, 0, new Random(7), out _);
Check("handler-player-text-untouched", playerField.Writes.Count == 0, string.Join(",", playerField.Writes));

// Deleting the last character empties the field through the setter.
var deleteField = new FakeField();
var deleteTick = TickPlan.Step("", true, "Ready.", [P("x")], oneChar, 5000, TargetPriority.ShortestPrompt, true);
var afterDelete = TickPlan.Apply(deleteTick, "", deleteField, 5000, ref nextWrite, ref nextFocus, 0, 0,
    new Random(7), out _);
Check("handler-last-char-clears",
    deleteField.Writes.Count == 1 && deleteField.Writes[0] == "" && afterDelete.SkipTarget == "x",
    string.Join(",", deleteField.Writes));

// A submitted word leaves the field empty and the next word is typed, not skipped.
var submitField = new FakeField();
var submitTick = TickPlan.Step("", true, "Ready.", [P("next")], submitted, 5000,
    TargetPriority.ShortestPrompt, true);
TickPlan.Apply(submitTick, "", submitField, 5000, ref nextWrite, ref nextFocus, 0, 0, new Random(7), out _);
Check("handler-submission-types-next", submitField.Writes.Count == 1 && submitField.Writes[0] == "n",
    string.Join(",", submitField.Writes));

// An inactive field is focused first and gets no text on that frame.
var focusField = new FakeField { IsActive = false, FocusResult = true };
var focusTick = TickPlan.Step("", true, "Ready.", [P("bomb")], new AssistState(null, false, null, null, 0),
    5000, TargetPriority.ShortestPrompt, true);
long focusWrite = 0, focusNext = 0;
var focusedState = TickPlan.Apply(focusTick, "", focusField, 5000, ref focusWrite, ref focusNext, 0, 0,
    new Random(7), out _);
Check("handler-focus-before-write", focusField.Writes.Count == 0 && focusField.FocusCalls == 1,
    $"{focusField.Writes.Count} writes, {focusField.FocusCalls} focuses");

// Once focused, the same decision writes on the next frame.
var secondTick = TickPlan.Step("", true, "Ready.", [P("bomb")], focusedState, 5300, TargetPriority.ShortestPrompt,
    true);
TickPlan.Apply(secondTick, "", focusField, 5300, ref focusWrite, ref focusNext, 0, 0, new Random(7), out _);
Check("handler-writes-after-focus", focusField.Writes.Count == 1 && focusField.Writes[0] == "b",
    string.Join(",", focusField.Writes));

// ---- The gates and the whole frame, through Frame.Run: what Plugin.Tick now is without the native reads. ----
var gates = new FrameSettings(TargetPriority.ShortestPrompt, true, 0, 0);

var haltField = new FakeField();
var haltTimers = new Timers();
var haltResult = Frame.Run(new FrameContext(true, "Holding - Dynamic Sensory Jammer is locking input.", false,
        "", true, "Ready.", [P("bomb")]), gates, new AssistState(null, false, null, null, 0), haltField, 5000,
    ref haltTimers.Write, ref haltTimers.Focus, new Random(1));
Check("gate-halt-no-write", haltField.Writes.Count == 0 && haltResult.Status.StartsWith("Holding"),
    haltResult.Status);

var sceneField = new FakeField();
var sceneTimers = new Timers();
var scene = Frame.Run(new FrameContext(false, "", true, "", true, "Ready.", [P("bomb")]), gates,
    new AssistState(null, false, null, null, 0), sceneField, 5000, ref sceneTimers.Write, ref sceneTimers.Focus,
    new Random(1));
Check("gate-scene-no-write", sceneField.Writes.Count == 0 && scene.Status.StartsWith("Waiting - the duty"),
    scene.Status);

// ---- Audit parity: the review's twelve checks, driven through Frame.Run rather than by inspection. ----
AssistState RunFrame(FrameContext context, AssistState previous, FakeField into, long at)
{
    // A fresh write clock per frame keeps each scenario independent of the one before it.
    var timers = new Timers();
    return Frame.Run(context, gates, previous, into, at, ref timers.Write, ref timers.Focus, new Random(1)).State;
}

// Control 1: an ordinary word is typed one character at a time.
var normalField = new FakeField();
RunFrame(new FrameContext(false, "", false, "", true, "Ready.", [P("bomb")]), new AssistState(null, false, null, null, 0),
    normalField, 5000);
Check("audit-control-normal", normalField.Writes.Count == 1 && normalField.Writes[0] == "b",
    string.Join(",", normalField.Writes));

// Control 2: no append writes while the duty prohibits typing.
var lockedAppendField = new FakeField();
var lockedAppendState = RunFrame(new FrameContext(false, "", false, "b", false, "Typing prohibited (3s).", [P("bomb")]),
    new AssistState("b", true, "bomb", null, 0), lockedAppendField, 5000);
Check("audit-control-locked", lockedAppendField.Writes.Count == 0, string.Join(",", lockedAppendField.Writes));

// Control 3: deleting part of a multi-character word clears and skips it.
var partialField = new FakeField();
var partialState = RunFrame(new FrameContext(false, "", false, "bom", true, "Ready.", [P("bomb")]),
    new AssistState("bomb", true, "bomb", null, 0), partialField, 5000);
Check("audit-control-partial-delete", partialField.Writes.Count == 1 && partialField.Writes[0] == ""
    && partialState.SkipTarget == "bomb", string.Join(",", partialField.Writes));

// F1: a barrier word completes; it is never cleared by its own preemption.
var parBarrierField = new FakeField();
var parBarrierState = new AssistState(null, false, null, null, 0);
var parMirror = "";
for (var frame = 0; frame < 12; frame++)
{
    var at = 6000 + frame * 500;
    var before = parBarrierField.Writes.Count;
    parBarrierState = RunFrame(new FrameContext(false, "", false, parMirror, true, "Ready.", barrierOnly),
        parBarrierState, parBarrierField, at);
    if (parBarrierField.Writes.Count == before) break;
    parMirror = parBarrierField.Writes[^1];
}
Check("audit-F1-barrier-completes", parMirror == "shield", parMirror);

// F2: neither clear path touches the field while permission is false.
var lockedTakeoverField = new FakeField();
RunFrame(new FrameContext(false, "", false, "b", false, "Typing prohibited (3s).",
        [P("bomb"), P("shield", null, 9, defense: true)]), new AssistState("b", true, "bomb", null, 0),
    lockedTakeoverField, 5000);
Check("audit-F2-locked-takeover", lockedTakeoverField.Writes.Count == 0, string.Join(",", lockedTakeoverField.Writes));

var lockedDeleteField = new FakeField();
RunFrame(new FrameContext(false, "", false, "bom", false, "Typing prohibited (3s).", [P("bomb")]),
    new AssistState("bomb", true, "bomb", null, 0), lockedDeleteField, 5000);
Check("audit-F2-locked-backspace", lockedDeleteField.Writes.Count == 0, string.Join(",", lockedDeleteField.Writes));

// F3: text the player replaced is preserved even while a barrier word waits.
var replacedFrameField = new FakeField();
RunFrame(new FrameContext(false, "", false, "my note", true, "Ready.", [P("bomb"), P("shield", null, 9, defense: true)]),
    new AssistState("b", true, "bomb", null, 0), replacedFrameField, 5000);
Check("audit-F3-player-text-kept", replacedFrameField.Writes.Count == 0, string.Join(",", replacedFrameField.Writes));

// F4: the first character is taken from the prepared text.
var accentField = new FakeField();
RunFrame(new FrameContext(false, "", false, "", true, "Ready.", [P("éclair")]),
    new AssistState(null, false, null, null, 0), accentField, 5000);
Check("audit-F4-accent-first", accentField.Writes.Count == 1 && accentField.Writes[0] == "e",
    string.Join(",", accentField.Writes));

var punctField = new FakeField();
RunFrame(new FrameContext(false, "", false, "", true, "Ready.", [P("'bomb")]),
    new AssistState(null, false, null, null, 0), punctField, 5000);
Check("audit-F4-punct-first", punctField.Writes.Count == 1 && punctField.Writes[0] == "b",
    string.Join(",", punctField.Writes));

// F5: deleting the last owned character skips the word; a submission does not.
var lastCharFrameField = new FakeField();
var lastCharFrameState = RunFrame(new FrameContext(false, "", false, "", true, "Ready.", [P("x")]),
    new AssistState("x", true, "x", null, 0), lastCharFrameField, 5000);
Check("audit-F5-last-char-skips", lastCharFrameField.Writes.Count == 1 && lastCharFrameField.Writes[0] == ""
    && lastCharFrameState.SkipTarget == "x", string.Join(",", lastCharFrameField.Writes));

var submitFrameField = new FakeField();
var submitFrameState = RunFrame(new FrameContext(false, "", false, "", true, "Ready.", [P("next")]),
    new AssistState("bomb", true, "bomb", null, 0), submitFrameField, 5000);
Check("audit-F5-submission-no-skip", submitFrameField.Writes.Count == 1 && submitFrameField.Writes[0] == "n"
    && submitFrameState.SkipTarget == null, string.Join(",", submitFrameField.Writes));

// F6: an ambiguous or contested match keeps no distance, above the scale-estimate threshold either.
var ambiguousScaled = PromptMap.WithDistances([Pm(100, 150, 1.5f, 1)],
    [new ActorScreen(7, new Vector2(100, 200), new Vector3(0, 0, 10)),
     new ActorScreen(8, new Vector2(106, 200), new Vector3(0, 0, 20))], Vector3.Zero);
var contestedScaled = PromptMap.WithDistances([Pm(100, 150, 1.5f, 1), Pm(101, 150, 1.4f, 2)],
    [new ActorScreen(7, new Vector2(100, 200), new Vector3(0, 0, 10))], Vector3.Zero);
Check("audit-F6-no-guessed-distance",
    ambiguousScaled[0].Distance == null && contestedScaled[0].Distance == null && contestedScaled[1].Distance == null,
    $"{ambiguousScaled[0].Distance}/{contestedScaled[0].Distance}/{contestedScaled[1].Distance}");

Console.WriteLine($"{checks - failures}/{checks} checks passed.");
return failures == 0 ? 0 : 1;

// Records every write the plugin would make, so the application step is executed rather than reviewed.
sealed class FakeField : IInputField
{
    public bool IsActive { get; set; } = true;
    public bool FocusResult { get; set; } = true;
    public int FocusCalls;
    public List<string> Writes { get; } = [];

    public bool Focus()
    {
        FocusCalls++;
        IsActive = FocusResult;
        return FocusResult;
    }

    public void SetText(string text) => Writes.Add(text);
}

sealed class Timers
{
    public long Write;
    public long Focus;
}

