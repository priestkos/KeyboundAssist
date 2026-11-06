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
    bool allowed = true, string reason = "prohibited") =>
    TypingPlan.Next(field, prompts, priority, allowed, reason);

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
var draws = Enumerable.Range(0, 400).Select(_ => TypingPlan.DrawDelay(rng, 150, 300)).ToArray();
Check("delay-in-range", draws.All(d => d is >= 150 and <= 300), $"min={draws.Min()} max={draws.Max()}");
Check("delay-varies", draws.Distinct().Count() > 1, $"distinct={draws.Distinct().Count()}");
Check("delay-fixed-when-equal", TypingPlan.DrawDelay(rng, 200, 200) == 200, "expected 200");
Check("delay-swapped-bounds", TypingPlan.DrawDelay(rng, 300, 150) is >= 150 and <= 300, "expected 150..300");
// The 130 ms floor holds for any config, including the old 30-60 default and 0.
Check("delay-floor-old-default", TypingPlan.DrawDelay(rng, 30, 60) == TypingPlan.MinDelayMs, "expected 130");
Check("delay-floor-zero", TypingPlan.DrawDelay(rng, -20, 0) == TypingPlan.MinDelayMs, "expected 130");
Check("delay-floor-straddles", Enumerable.Range(0, 200).All(_ => TypingPlan.DrawDelay(rng, 100, 140) is >= 130 and <= 140), "expected 130..140");
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

// Punctuation is always typed exactly as drawn.
Check("punct-first-char", Next("", [P("don't-stop!")]).Next == 'd', "expected 'd'");
Check("punct-typed", Next("don", [P("don't-stop!")]).Next == '\'', "expected the apostrophe");
Check("punct-ready", Next("don't-stop!", [P("don't-stop!")]).Kind == StepKind.Ready, "expected ready");
Check("punct-stripped-rejected", Next("dont", [P("don't-stop!")]).Kind == StepKind.Waiting, "expected waiting");
step = Next("", [P("'ello")]);
Check("punct-leading-first-char", step.Next == '\'', $"expected the apostrophe, got '{step.Next}'");
Check("punct-only-typed", Next("", [P("!!!")]).Next == '!', "expected '!'");

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
Check("owned-prefix-yes", TypingPlan.IsOwnedPrefix("b", "bomb"), "expected owned");
Check("owned-prefix-no", !TypingPlan.IsOwnedPrefix("my note", "bomb"), "expected not owned");
Check("owned-prefix-punct", TypingPlan.IsOwnedPrefix("don'", "don't-stop!"), "expected owned");
Check("owned-prefix-stripped", !TypingPlan.IsOwnedPrefix("dont", "don't-stop!"), "expected not owned");

// ---- The update loop itself, driven through the same TickPlan.Step the plugin calls every frame. ----

// F1: a barrier word completes under preemption instead of being cleared on its own progress.
var state = new AssistState(null, false, null, null, 0);
Prompt[] barrierOnly = [P("shield", null, 5, defense: true)];
var typedSoFar = "";
var clears = 0;
for (var frame = 0; frame < 12; frame++)
{
    var outcome = TickPlan.Step(typedSoFar, true, "Ready.", barrierOnly, state, 1000 + frame * 20,
        TargetPriority.ShortestPrompt);
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
    [P("bomb"), P("shield", null, 9, defense: true)], lockedPartial, 5000, TargetPriority.ShortestPrompt);
Check("loop-locked-takeover-no-clear", !locked.Clear && locked.Action == TickAction.None, locked.ToString());

var lockedDeleted = new AssistState("bomb", true, "bomb", null, 0);
var lockedDel = TickPlan.Step("bom", false, "Typing prohibited (3s).", [P("bomb")], lockedDeleted, 5000,
    TargetPriority.ShortestPrompt);
Check("loop-locked-backspace-no-clear", !lockedDel.Clear && lockedDel.Action == TickAction.None,
    lockedDel.ToString());

// F3: text the player put there is never cleared, even when a barrier word is waiting.
var replaced = new AssistState("b", true, "bomb", null, 0);
var kept = TickPlan.Step("my note", true, "Ready.", [P("bomb"), P("shield", null, 9, defense: true)],
    replaced, 5000, TargetPriority.ShortestPrompt);
Check("loop-player-text-kept", !kept.Clear && kept.Action != TickAction.Write, kept.ToString());

// F1's other half: the same barrier does take over a normal word that is only half typed.
var midWord = new AssistState("bo", true, "bomb", null, 0);
var preempt = TickPlan.Step("bo", true, "Ready.", [P("bomb"), P("shield", null, 9, defense: true)], midWord,
    5000, TargetPriority.ShortestPrompt);
Check("loop-barrier-preempts", preempt.Clear, preempt.ToString());

// ...but a barrier word the typed text can still lead into is continued, not thrown away.
var sharedPrefix = TickPlan.Step("s", true, "Ready.", [P("sundae"), P("shield", null, 9, defense: true)],
    new AssistState("s", true, "sundae", null, 0), 5000, TargetPriority.ShortestPrompt);
Check("loop-barrier-shares-prefix", !sharedPrefix.Clear && sharedPrefix.State.CurrentTarget == "shield",
    sharedPrefix.ToString());

// F5: deleting the last remaining character of a one-character prompt clears and skips it.
var oneChar = new AssistState("x", true, "x", null, 0);
var lastChar = TickPlan.Step("", true, "Ready.", [P("x")], oneChar, 5000, TargetPriority.ShortestPrompt);
Check("loop-last-char-delete-skips", lastChar.Clear && lastChar.State.SkipTarget == "x", lastChar.ToString());

// A submitted word empties the field too; that is not a deletion and must skip nothing.
var submitted = new AssistState("bomb", true, "bomb", null, 0);
var afterSubmit = TickPlan.Step("", true, "Ready.", [P("next")], submitted, 5000,
    TargetPriority.ShortestPrompt);
Check("loop-submission-no-skip",
    !afterSubmit.Clear && afterSubmit.Action == TickAction.Write && afterSubmit.State.SkipTarget == null,
    afterSubmit.ToString());

// A skipped word stays out of the pool for the window, then comes back.
var skipping = TickPlan.Step("", true, "Ready.", [P("bomb")], new AssistState(null, false, null, "bomb", 6000),
    5000, TargetPriority.ShortestPrompt);
Check("loop-skip-window", skipping.Action == TickAction.None, skipping.ToString());
var skipExpired = TickPlan.Step("", true, "Ready.", [P("bomb")], skipping.State, 7000,
    TargetPriority.ShortestPrompt);
Check("loop-skip-expires", skipExpired.Action == TickAction.Write, skipExpired.ToString());

// The enemy carrying a half-typed word dies: our partial text is cleared once it has stayed orphaned
// for OrphanMs, never sooner, and never while typing is locked.
var halfTyped = new AssistState("explo", true, "explosion", null, 0);
var orphan1 = TickPlan.Step("explo", true, "Ready.", [P("bomb")], halfTyped, 10_000, TargetPriority.ShortestPrompt);
Check("orphan-grace-no-clear", !orphan1.Clear && orphan1.State.OrphanSince == 10_000, orphan1.ToString());
var orphan2 = TickPlan.Step("explo", true, "Ready.", [P("bomb")], orphan1.State, 10_000 + TickPlan.OrphanMs - 1, TargetPriority.ShortestPrompt);
Check("orphan-grace-holds", !orphan2.Clear, orphan2.ToString());
var orphan3 = TickPlan.Step("explo", true, "Ready.", [P("bomb")], orphan2.State, 10_000 + TickPlan.OrphanMs, TargetPriority.ShortestPrompt);
Check("orphan-cleared", orphan3.Clear && orphan3.State.LastWritten == null && orphan3.State.OrphanSince == null, orphan3.ToString());
var orphanLocked = TickPlan.Step("explo", false, "Typing prohibited (2s).", [P("bomb")], orphan2.State, 20_000, TargetPriority.ShortestPrompt);
Check("orphan-locked-no-clear", !orphanLocked.Clear && orphanLocked.State.OrphanSince == 10_000, orphanLocked.ToString());
// A finished word whose enemy died is cleared too: Enter would only submit a dead word.
var deadWhole = TickPlan.Step("explosion", true, "Ready.", [], new AssistState("explosion", true, "explosion", null, 0, 1_000), 1_000 + TickPlan.OrphanMs, TargetPriority.ShortestPrompt);
Check("orphan-finished-cleared", deadWhole.Clear, deadWhole.ToString());
// A label that drops for a frame and comes back resets the grace, and typing carries on.
var flicker = TickPlan.Step("explo", true, "Ready.", [P("explosion")], orphan2.State, 10_300, TargetPriority.ShortestPrompt);
Check("orphan-flicker-resets", !flicker.Clear && flicker.Action == TickAction.Write && flicker.State.OrphanSince == null, flicker.ToString());
// Another prompt that the text still leads into is not an orphan: it is continued.
var shared = TickPlan.Step("bo", true, "Ready.", [P("bomber")], new AssistState("bo", true, "bomb", null, 0, 1_000), 5_000, TargetPriority.ShortestPrompt);
Check("orphan-shared-prefix-continues", !shared.Clear && shared.Action == TickAction.Write && shared.State.CurrentTarget == "bomber", shared.ToString());
// Text the player typed is never an orphan of ours.
var playerOrphan = TickPlan.Step("my note", true, "Ready.", [P("bomb")], new AssistState("expl", true, "explosion", null, 0, 1_000), 9_000, TargetPriority.ShortestPrompt);
Check("orphan-player-text-kept", !playerOrphan.Clear, playerOrphan.ToString());
// A just-submitted word whose prompt vanishes a frame before the field empties is not cleared.
var submitRace = TickPlan.Step("bomb", true, "Ready.", [], new AssistState("bomb", true, "bomb", null, 0), 30_000, TargetPriority.ShortestPrompt);
Check("orphan-submit-race-no-clear", !submitRace.Clear, submitRace.ToString());

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
        TargetPriority.ShortestPrompt);
    if (tick.Action == TickAction.None) break;
    liveState = TickPlan.Apply(tick, mirror, field, at, ref nextWrite, ref nextFocus, 0, 0, new Random(7), out _);
    mirror = tick.Clear ? "" : field.Writes[^1];
}
Check("handler-barrier-writes", string.Join(",", field.Writes) == "s,sh,shi,shie,shiel,shield",
    string.Join(",", field.Writes));

// Nothing is written while the duty prohibits typing.
var lockedField = new FakeField();
var lockedTick = TickPlan.Step("b", false, "Typing prohibited (3s).",
    [P("bomb"), P("shield", null, 9, defense: true)], lockedPartial, 5000, TargetPriority.ShortestPrompt);
TickPlan.Apply(lockedTick, "b", lockedField, 5000, ref nextWrite, ref nextFocus, 0, 0, new Random(7), out _);
Check("handler-locked-no-write", lockedField.Writes.Count == 0, string.Join(",", lockedField.Writes));

// Text the player replaced is never written over.
var playerField = new FakeField();
var playerTick = TickPlan.Step("my note", true, "Ready.", [P("bomb"), P("shield", null, 9, defense: true)],
    replaced, 5000, TargetPriority.ShortestPrompt);
TickPlan.Apply(playerTick, "my note", playerField, 5000, ref nextWrite, ref nextFocus, 0, 0, new Random(7), out _);
Check("handler-player-text-untouched", playerField.Writes.Count == 0, string.Join(",", playerField.Writes));

// Deleting the last character empties the field through the setter.
var deleteField = new FakeField();
var deleteTick = TickPlan.Step("", true, "Ready.", [P("x")], oneChar, 5000, TargetPriority.ShortestPrompt);
var afterDelete = TickPlan.Apply(deleteTick, "", deleteField, 5000, ref nextWrite, ref nextFocus, 0, 0,
    new Random(7), out _);
Check("handler-last-char-clears",
    deleteField.Writes.Count == 1 && deleteField.Writes[0] == "" && afterDelete.SkipTarget == "x",
    string.Join(",", deleteField.Writes));

// A submitted word leaves the field empty and the next word is typed, not skipped.
var submitField = new FakeField();
var submitTick = TickPlan.Step("", true, "Ready.", [P("next")], submitted, 5000,
    TargetPriority.ShortestPrompt);
TickPlan.Apply(submitTick, "", submitField, 5000, ref nextWrite, ref nextFocus, 0, 0, new Random(7), out _);
Check("handler-submission-types-next", submitField.Writes.Count == 1 && submitField.Writes[0] == "n",
    string.Join(",", submitField.Writes));

// An inactive field is focused first and gets no text on that frame.
var focusField = new FakeField { IsActive = false, FocusResult = true };
var focusTick = TickPlan.Step("", true, "Ready.", [P("bomb")], new AssistState(null, false, null, null, 0),
    5000, TargetPriority.ShortestPrompt);
long focusWrite = 0, focusNext = 0;
var focusedState = TickPlan.Apply(focusTick, "", focusField, 5000, ref focusWrite, ref focusNext, 0, 0,
    new Random(7), out _);
Check("handler-focus-before-write", focusField.Writes.Count == 0 && focusField.FocusCalls == 1,
    $"{focusField.Writes.Count} writes, {focusField.FocusCalls} focuses");

// Once focused, the same decision writes on the next frame.
var secondTick = TickPlan.Step("", true, "Ready.", [P("bomb")], focusedState, 5300, TargetPriority.ShortestPrompt);
TickPlan.Apply(secondTick, "", focusField, 5300, ref focusWrite, ref focusNext, 0, 0, new Random(7), out _);
Check("handler-writes-after-focus", focusField.Writes.Count == 1 && focusField.Writes[0] == "b",
    string.Join(",", focusField.Writes));

// ---- The gates and the whole frame, through Frame.Run: what Plugin.Tick now is without the native reads. ----
var gates = new FrameSettings(TargetPriority.ShortestPrompt, 0, 0);

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

// F4: the first character is taken from the prepared text; punctuation is typed as drawn.
var accentField = new FakeField();
RunFrame(new FrameContext(false, "", false, "", true, "Ready.", [P("éclair")]),
    new AssistState(null, false, null, null, 0), accentField, 5000);
Check("audit-F4-accent-first", accentField.Writes.Count == 1 && accentField.Writes[0] == "e",
    string.Join(",", accentField.Writes));

var punctField = new FakeField();
RunFrame(new FrameContext(false, "", false, "", true, "Ready.", [P("'bomb")]),
    new AssistState(null, false, null, null, 0), punctField, 5000);
Check("audit-F4-punct-first", punctField.Writes.Count == 1 && punctField.Writes[0] == "'",
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

// The barrier only drops text this plugin wrote: the player's own "b", "bo" towards a prompt stays.
var typedByPlayer = TickPlan.Step("b", true, "Ready.", [P("bomb")], new AssistState(null, false, null, null, 0),
    5000, TargetPriority.ShortestPrompt);
var playerBarrier = TickPlan.Step("bo", true, "Ready.", [P("bomb"), P("shield", null, 9, defense: true)],
    typedByPlayer.State, 5020, TargetPriority.ShortestPrompt);
Check("loop-barrier-keeps-player-prefix", !playerBarrier.Clear, playerBarrier.ToString());

// A submitted full word whose twin is still on another enemy is a submission, not a deletion.
var duplicate = TickPlan.Step("", true, "Ready.", [P("bomb", null, 2)], new AssistState("bomb", true, "bomb", null, 0),
    5000, TargetPriority.ShortestPrompt);
Check("loop-duplicate-submission-no-skip",
    !duplicate.Clear && duplicate.State.SkipTarget == null && duplicate.Action == TickAction.Write && duplicate.Next == 'b',
    duplicate.ToString());

// While the game's chat input has focus, a pending write never pulls focus back to the duty field.
var chatField = new FakeField { IsActive = false };
RunFrame(new FrameContext(false, "", false, "", true, "Ready.", [P("bomb")], ChatFocused: true),
    new AssistState(null, false, null, null, 0), chatField, 5000);
Check("gate-chat-focused-no-focus", chatField.FocusCalls == 0 && chatField.Writes.Count == 0,
    $"{chatField.FocusCalls} focuses, {chatField.Writes.Count} writes");

// End to end at the new pace: 60 fps frames through Frame.Run with the 150-300 ms default. A long word's
// enemy dies after a few characters; the leftover is cleared and the next word is typed from scratch.
{
    var pace = new FrameSettings(TargetPriority.ShortestPrompt, 150, 300);
    var simField = new FakeField();
    var simState = new AssistState(null, false, null, null, 0);
    long simWrite = 0, simFocus = 0;
    var simText = "";
    var simRng = new Random(42);
    var killedAt = -1L;
    var clearedAt = -1L;
    var gaps = new List<long>();
    var lastWriteAt = -1L;
    for (long at = 100_000; at < 112_000; at += 16)
    {
        // "constellation" is alive until 5 characters are in, then only "bomb" is left on screen.
        if (killedAt < 0 && simText.Length >= 5) killedAt = at;
        Prompt[] shown = killedAt < 0 ? [P("constellation", null, 1)] : [P("bomb", null, 3)];
        var before = simField.Writes.Count;
        simState = Frame.Run(new FrameContext(false, "", false, simText, true, "Ready.", shown), pace, simState,
            simField, at, ref simWrite, ref simFocus, simRng).State;
        if (simField.Writes.Count == before) continue;
        simText = simField.Writes[^1];
        if (simText.Length == 0 && clearedAt < 0) clearedAt = at;
        else if (simText.Length > 0) { if (lastWriteAt >= 0 && simText.Length > 1) gaps.Add(at - lastWriteAt); lastWriteAt = at; }
    }
    Check("sim-dead-word-cleared", clearedAt > 0 && clearedAt - killedAt >= TickPlan.OrphanMs && clearedAt - killedAt < TickPlan.OrphanMs + 50,
        $"killed {killedAt}, cleared {clearedAt}");
    Check("sim-next-word-typed", simText == "bomb", $"field ended as '{simText}'");
    Check("sim-pace-floor", gaps.Count > 0 && gaps.All(g => g >= TypingPlan.MinDelayMs), $"min gap {(gaps.Count > 0 ? gaps.Min() : -1)} ms");
}

// ---- Auto-send: only our finished word, once, never sooner than the drawn delay, and only through the gates. ----
{
    var on = new FrameSettings(TargetPriority.ShortestPrompt, 150, 300, AutoSubmit: true);
    var off = new FrameSettings(TargetPriority.ShortestPrompt, 150, 300);
    var ours = new AssistState("bomb", true, "bomb", null, 0);
    FrameContext Ctx(string field, Prompt[] shown, bool allowed = true, bool halted = false, bool chat = false) =>
        new(halted, "Holding.", false, field, allowed, allowed ? "Ready." : "Typing prohibited (2s).", shown, chat);
    (AssistState State, string Status) Run(FrameContext c, FrameSettings s, AssistState st, FakeField f, long at)
    {
        long w = 0, fo = 0;
        return Frame.Run(c, s, st, f, at, ref w, ref fo, new Random(3));
    }

    var offField = new FakeField();
    var offState = ours;
    for (long at = 1000; at < 4000; at += 16) offState = Run(Ctx("bomb", [P("bomb")]), off, offState, offField, at).State;
    Check("send-off-never", offField.Submits == 0, $"{offField.Submits} sends");

    var f1 = new FakeField();
    var armed = Run(Ctx("bomb", [P("bomb")]), on, ours, f1, 1000);
    Check("send-armed-not-sent", f1.Submits == 0 && armed.State.SubmitAt is >= 1300 and <= 1500 && armed.Status.Contains("Sending in"),
        $"{armed.State.SubmitAt} '{armed.Status}'");
    var due = armed.State.SubmitAt ?? 0;
    var waiting = Run(Ctx("bomb", [P("bomb")]), on, armed.State, f1, due - 1);
    Check("send-waits-for-delay", f1.Submits == 0, $"{f1.Submits} sends at {due - 1}");
    var fired = Run(Ctx("bomb", [P("bomb")]), on, waiting.State, f1, due);
    Check("send-fires-at-delay", f1.Submits == 1 && fired.State.Sent == "bomb", $"{f1.Submits} sends, sent '{fired.State.Sent}'");
    var stuck = fired.State;
    for (long at = due + 16; at < due + 3000; at += 16) stuck = Run(Ctx("bomb", [P("bomb")]), on, stuck, f1, at).State;
    Check("send-once-if-field-kept", f1.Submits == 1 && f1.Writes.Count == 0, $"{f1.Submits} sends, {f1.Writes.Count} writes");

    var fFloor = new FakeField();
    var floorArmed = Run(Ctx("bomb", [P("bomb")]), on with { SubmitMinMs = 0, SubmitMaxMs = 0 }, ours, fFloor, 1000);
    Check("send-floor", floorArmed.State.SubmitAt == 1000 + TypingPlan.SubmitFloorMs, $"{floorArmed.State.SubmitAt}");
    Check("send-cap", Run(Ctx("bomb", [P("bomb")]), on with { SubmitMinMs = 9000, SubmitMaxMs = 9000 }, ours, fFloor, 1000)
        .State.SubmitAt == 1000 + TypingPlan.SubmitCapMs, "expected the cap");

    var fPlayer = new FakeField();
    var playerState = new AssistState(null, false, null, null, 0);
    for (long at = 1000; at < 3000; at += 16) playerState = Run(Ctx("bomb", [P("bomb")]), on, playerState, fPlayer, at).State;
    Check("send-never-player-word", fPlayer.Submits == 0, $"{fPlayer.Submits} sends");
    var fUnseen = new FakeField();
    var unseen = Run(Ctx("bomb", [P("bomb")]), on, ours with { WriteSeen = false, LastWritten = "bom" }, fUnseen, 1000);
    Check("send-needs-read-back", unseen.State.SubmitAt == null, $"{unseen.State.SubmitAt}");

    var fLock = new FakeField();
    var sendLocked = Run(Ctx("bomb", [P("bomb")], allowed: false), on, armed.State, fLock, due + 10);
    Check("send-locked-none", fLock.Submits == 0 && sendLocked.State.SubmitAt == null, $"{fLock.Submits} {sendLocked.State.SubmitAt}");

    var fHalt = new FakeField();
    var halted = Run(Ctx("bomb", [P("bomb")], halted: true), on, armed.State, fHalt, due + 10);
    var rearmed = Run(Ctx("bomb", [P("bomb")]), on, halted.State, fHalt, due + 5000);
    Check("send-halt-rearms", fHalt.Submits == 0 && halted.State.SubmitAt == null && rearmed.State.SubmitAt >= due + 5300,
        $"{fHalt.Submits} {halted.State.SubmitAt} {rearmed.State.SubmitAt}");

    var fChat = new FakeField();
    Run(Ctx("bomb", [P("bomb")], chat: true), on, armed.State, fChat, due);
    var fIdle = new FakeField { IsActive = false, FocusResult = false };
    Run(Ctx("bomb", [P("bomb")]), on, armed.State, fIdle, due);
    Check("send-chat-or-inactive-waits", fChat.Submits == 0 && fIdle.Submits == 0, $"{fChat.Submits} {fIdle.Submits}");

    var fFail = new FakeField { SubmitResult = false };
    var failed = Run(Ctx("bomb", [P("bomb")]), on, armed.State, fFail, due);
    for (long at = due + 16; at < due + 2000; at += 16) failed = Run(Ctx("bomb", [P("bomb")]), on, failed.State, fFail, at);
    Check("send-failure-not-retried", fFail.Submits == 1 && failed.Status.Contains("Press Enter"), $"{fFail.Submits} '{failed.Status}'");

    // A one-letter word whose twin is still shown: the field emptied by our send is not a backspace.
    var fOne = new FakeField();
    var oneSent = Run(Ctx("x", [P("x")]), on, new AssistState("x", true, "x", null, 0, SubmitAt: 1000), fOne, 1000);
    var oneAfter = Run(Ctx("", [P("x", null, 4)]), on, oneSent.State, fOne, 1016);
    Check("send-one-letter-not-skipped", fOne.Submits == 1 && oneAfter.State.SkipTarget == null && !fOne.Writes.Contains(""),
        $"skip '{oneAfter.State.SkipTarget}', writes {string.Join(",", fOne.Writes)}");

    // End to end at 60 fps with the defaults: two words typed and sent, each send 300-500 ms after its last letter.
    var simField = new FakeField();
    var simState = new AssistState(null, false, null, null, 0);
    long simWrite = 0, simFocus = 0;
    var simText = "";
    var simRng = new Random(9);
    var words = new Queue<string>(["bomb", "grunt"]);
    var current = words.Dequeue();
    var lastLetterAt = -1L;
    var sendGaps = new List<long>();
    for (long at = 200_000; at < 206_000; at += 16)
    {
        var sendsBefore = simField.Submits;
        var writesBefore = simField.Writes.Count;
        simState = Frame.Run(new FrameContext(false, "", false, simText, true, "Ready.", current == "" ? [] : [P(current, null, 1)]),
            on, simState, simField, at, ref simWrite, ref simFocus, simRng).State;
        if (simField.Writes.Count > writesBefore) { simText = simField.Writes[^1]; lastLetterAt = at; }
        if (simField.Submits > sendsBefore)
        {
            sendGaps.Add(at - lastLetterAt);
            simText = "";
            current = words.Count > 0 ? words.Dequeue() : "";
        }
    }
    Check("sim-send-both-words", simField.Submits == 2 && current == "", $"{simField.Submits} sends, '{current}' left");
    Check("sim-send-delay", sendGaps.Count == 2 && sendGaps.All(g => g is >= 300 and <= 500 + 16),
        string.Join(",", sendGaps));
}

// ---- Greyed-out boxes: never a target, and our word is dropped the moment its box goes grey. ----
{
    Prompt G(string text, uint node = 0) => P(text, node: node) with { Grey = true };
    Check("grey-never-chosen", Next("", [G("bomb", 1), P("constellation", node: 2)]).Target == "constellation",
        Next("", [G("bomb", 1), P("constellation", node: 2)]).ToString());
    Check("grey-only-waits", Next("", [G("bomb", 1)]).Kind == StepKind.Waiting, Next("", [G("bomb", 1)]).ToString());
    var greyOnly = TickPlan.Step("", true, "Ready.", [G("bomb", 1)], new AssistState(null, false, null, null, 0), 1000, TargetPriority.ShortestPrompt);
    Check("grey-only-nothing-typed", greyOnly.Action == TickAction.None, greyOnly.ToString());

    var midBomb = new AssistState("bo", true, "bomb", null, 0);
    var dropped = TickPlan.Step("bo", true, "Ready.", [G("bomb", 1), P("grunt", node: 2), P("explosion", node: 3)], midBomb, 1000,
        TargetPriority.ShortestPrompt);
    Check("grey-drops-our-word-now", dropped.Clear && dropped.Status.Contains("grey"), dropped.ToString());
    var retarget = TickPlan.Step("", true, "Ready.", [G("bomb", 1), P("grunt", node: 2), P("explosion", node: 3)], dropped.State, 1016,
        TargetPriority.ShortestPrompt);
    Check("grey-retargets-by-order", retarget.Action == TickAction.Write && retarget.Next == 'g' && retarget.State.CurrentTarget == "grunt"
        && retarget.State.SkipTarget == null, retarget.ToString());
    var nearest = TickPlan.Step("", true, "Ready.", [G("bomb", 1), P("grunt", 30f, 2), P("explosion", 5f, 3)], dropped.State, 1016,
        TargetPriority.NearestEnemy);
    Check("grey-retargets-nearest", nearest.State.CurrentTarget == "explosion", nearest.ToString());

    var continued = TickPlan.Step("bo", true, "Ready.", [G("bomb", 1), P("bomber", node: 2)], midBomb, 1000, TargetPriority.ShortestPrompt);
    Check("grey-shared-prefix-continues", !continued.Clear && continued.Next == 'm' && continued.State.CurrentTarget == "bomber", continued.ToString());

    var playerText = TickPlan.Step("bo", true, "Ready.", [G("bomb", 1), P("grunt", node: 2)], new AssistState(null, false, null, null, 0), 1000,
        TargetPriority.ShortestPrompt);
    Check("grey-player-text-kept", !playerText.Clear, playerText.ToString());

    var greyLocked = TickPlan.Step("bo", false, "Typing prohibited (2s).", [G("bomb", 1), P("grunt", node: 2)], midBomb, 1000,
        TargetPriority.ShortestPrompt);
    Check("grey-locked-kept", !greyLocked.Clear, greyLocked.ToString());

    // A finished word waiting for its auto-send is dropped too, and never sent.
    var sendOn = new FrameSettings(TargetPriority.ShortestPrompt, 150, 300, AutoSubmit: true);
    var waitField = new FakeField();
    long w1 = 0, f1 = 0;
    var armedSend = Frame.Run(new FrameContext(false, "", false, "bomb", true, "Ready.", [P("bomb", node: 1), P("grunt", node: 2)]), sendOn,
        new AssistState("bomb", true, "bomb", null, 0), waitField, 1000, ref w1, ref f1, new Random(5)).State;
    var greyedSend = Frame.Run(new FrameContext(false, "", false, "bomb", true, "Ready.", [G("bomb", 1), P("grunt", node: 2)]), sendOn,
        armedSend, waitField, (armedSend.SubmitAt ?? 0) + 10, ref w1, ref f1, new Random(5));
    Check("grey-drops-pending-send", waitField.Submits == 0 && waitField.Writes.SequenceEqual([""]) && greyedSend.State.SubmitAt == null,
        $"{waitField.Submits} sends, writes {string.Join(",", waitField.Writes)}");

    // End to end at 60 fps: "explosion" goes grey after 3 letters; the field is cleared on that frame and the next word typed.
    var gField = new FakeField();
    var gState = new AssistState(null, false, null, null, 0);
    long gw = 0, gf = 0;
    var gText = "";
    long greyAt = -1, clearAt = -1;
    for (long at = 300_000; at < 306_000; at += 16)
    {
        if (greyAt < 0 && gText.Length >= 3) greyAt = at;
        Prompt[] shown = greyAt < 0 ? [P("explosion", node: 1), P("gruntingly", node: 2)] : [G("explosion", 1), P("gruntingly", node: 2)];
        var before = gField.Writes.Count;
        gState = Frame.Run(new FrameContext(false, "", false, gText, true, "Ready.", shown), new FrameSettings(TargetPriority.ShortestPrompt, 150, 300),
            gState, gField, at, ref gw, ref gf, new Random(11)).State;
        if (gField.Writes.Count == before) continue;
        gText = gField.Writes[^1];
        if (gText.Length == 0 && clearAt < 0) clearAt = at;
    }
    Check("sim-grey-cleared-same-frame", greyAt > 0 && clearAt == greyAt, $"grey {greyAt}, cleared {clearAt}");
    Check("sim-grey-next-word", gText == "gruntingly", $"field ended as '{gText}'");
}

// ---- Threat order (AutoKeyboundBrawler's tiers) and the cast-bar mapping that feeds it. ----
HashSet<string> cactuarSheet = new(StringComparer.OrdinalIgnoreCase) { "1,000 Needles" };
Prompt T(uint node, string text, float y) => new(node, text, false, 0, y, 1f, null);
var threatPool = PromptMap.WithCasts(
    [T(2, "airbuster", 300), T(5, "grunt", 400), T(6, "sweeper", 350), T(7, "cactuar", 200), T(8, "bomb", 450)],
    [(6u, "Burst"), (2u, "Big Bomber")], cactuarSheet);
Check("threat-boss-flag", threatPool[0].Casting && !threatPool[0].Cactuar && !threatPool[0].Needles, threatPool[0].ToString());
Check("threat-casting-add", threatPool[2].Casting && !threatPool[2].Needles, threatPool[2].ToString());
Check("threat-cactuar-idle", threatPool[3].Cactuar && !threatPool[3].Needles, threatPool[3].ToString());
Check("threat-casting-first", TypingPlan.Choose(threatPool, "", TargetPriority.Threat)?.Text == "sweeper",
    TypingPlan.Choose(threatPool, "", TargetPriority.Threat)?.Text ?? "null");
Check("threat-shortest-unchanged", TypingPlan.Choose(threatPool, "", TargetPriority.ShortestPrompt)?.Text == "bomb",
    TypingPlan.Choose(threatPool, "", TargetPriority.ShortestPrompt)?.Text ?? "null");
var noCasts = PromptMap.WithCasts([T(2, "airbuster", 300), T(5, "grunt", 400), T(8, "bomb", 450), T(7, "cactuar", 200)], [], cactuarSheet);
Check("threat-cactuar-before-adds", TypingPlan.Choose(noCasts, "", TargetPriority.Threat)?.Text == "cactuar",
    TypingPlan.Choose(noCasts, "", TargetPriority.Threat)?.Text ?? "null");
var addsOnly = PromptMap.WithCasts([T(2, "airbuster", 600), T(5, "grunt", 400), T(8, "bomb", 450)], [], cactuarSheet);
Check("threat-lowest-add-first", TypingPlan.Choose(addsOnly, "", TargetPriority.Threat)?.Text == "bomb",
    TypingPlan.Choose(addsOnly, "", TargetPriority.Threat)?.Text ?? "null");
var bossOnly = PromptMap.WithCasts([T(2, "airbuster", 600)], [], cactuarSheet);
Check("threat-boss-last-but-chosen", TypingPlan.Choose(bossOnly, "", TargetPriority.Threat)?.Text == "airbuster", "expected the boss");
// Needles on the Cactuar's own bar beats a casting add; a Needles bar on no word slot still marks Cactuars.
var needles = PromptMap.WithCasts([T(6, "sweeper", 350), T(7, "1,000 Needles", 200)], [(6u, "Burst"), (7u, "??? Needles")], cactuarSheet);
Check("threat-needles-slot", needles[1].Needles && TypingPlan.Choose(needles, "", TargetPriority.Threat)?.Text == "1,000 Needles",
    needles[1].ToString());
var unslotted = PromptMap.WithCasts([T(6, "sweeper", 350), T(7, "cactuar", 200)], [(6u, "Burst"), (40u, "??? Needles")], cactuarSheet);
Check("threat-needles-unslotted", unslotted[1].Needles && TypingPlan.Choose(unslotted, "", TargetPriority.Threat)?.Text == "cactuar",
    unslotted[1].ToString());
Check("threat-boss-never-cactuar", !PromptMap.WithCasts([T(2, "gigantender", 0)], [(9u, "Needles")], cactuarSheet)[0].Needles, "boss marked");
// A barrier word still outranks every threat tier.
var threatBarrier = PromptMap.WithCasts([T(7, "cactuar", 200), new(9, "shield me now", false, 0, 900, 1f, null, true)],
    [(7u, "??? Needles")], cactuarSheet);
Check("threat-barrier-first", TypingPlan.Choose(threatBarrier, "", TargetPriority.Threat)?.Text == "shield me now", "expected barrier");

// ---- Wins from achievement progress, and words seen. ----
Check("wins-iv-read", Stats.WinsFromProgress(4073, 37, 100) == 37, "expected 37");
Check("wins-iv-capped-read", Stats.WinsFromProgress(4073, 100, 100) == 100, "expected 100");
Check("wins-lower-tier-uncapped", Stats.WinsFromProgress(4071, 7, 10) == 7, "expected 7");
Check("wins-lower-tier-capped-ignored", Stats.WinsFromProgress(4071, 10, 10) == null, "expected null");
Check("wins-other-achievement-ignored", Stats.WinsFromProgress(1, 3, 10) == null, "expected null");
Check("wins-garbage-ignored", Stats.WinsFromProgress(4073, 500, 100) == null && Stats.WinsFromProgress(4073, 0, 0) == null, "expected null");

var seenWords = new Dictionary<string, int>();
var shownNow = new Dictionary<(uint, string), long>();
Check("words-first-counts", Stats.Observe(seenWords, shownNow, [P("bomb", node: 1), P("grunt", node: 2)], 0) &&
    seenWords["bomb"] == 1 && seenWords["grunt"] == 1, string.Join(",", seenWords));
for (var f = 1; f <= 60; f++) Stats.Observe(seenWords, shownNow, [P("bomb", node: 1)], f * 16);
Check("words-held-counts-once", seenWords["bomb"] == 1, seenWords["bomb"].ToString());
Stats.Observe(seenWords, shownNow, [], 1000);
Stats.Observe(seenWords, shownNow, [P("bomb", node: 1)], 1100);
Check("words-blink-not-recounted", seenWords["bomb"] == 1, seenWords["bomb"].ToString());
Stats.Observe(seenWords, shownNow, [P("bomb", node: 1)], 1100 + Stats.RecountMs + 1);
Check("words-reappearance-counts", seenWords["bomb"] == 2, seenWords["bomb"].ToString());
Stats.Observe(seenWords, shownNow, [P("bomb", node: 1), P("bomb", node: 3)], 2200);
Check("words-second-slot-counts", seenWords["bomb"] == 3, seenWords["bomb"].ToString());
Check("words-gone-pruned", !shownNow.ContainsKey((2u, "grunt")), "grunt still tracked");

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

    public bool SubmitResult { get; set; } = true;
    public int Submits;

    public bool Submit()
    {
        Submits++;
        return SubmitResult;
    }
}

sealed class Timers
{
    public long Write;
    public long Focus;
}

