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

Console.WriteLine($"{checks - failures}/{checks} checks passed.");
return failures == 0 ? 0 : 1;
