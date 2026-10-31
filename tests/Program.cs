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

string[] Words(params string[] words) => words;

Step Next(string field, string[] words, bool allowed = true, string reason = "prohibited") =>
    TypingPlan.Next(field, words, allowed, reason);

// Empty field: pick the shortest prompt and start it.
var step = Next("", Words("nidhogg", "ifrit", "bomb"));
Check("shortest-first", step.Kind == StepKind.Typing && step.Target == "bomb" && step.Next == 'b', step.ToString());

// Empty field, no prompts: wait.
step = Next("", Words());
Check("no-prompts", step.Kind == StepKind.Waiting, step.ToString());

// Prefix already typed: continue at the right character, case-insensitively.
step = Next("nid", Words("Nidhogg", "ifrit"));
Check("prefix-advance", step.Kind == StepKind.Typing && step.Target == "Nidhogg" && step.Next == 'h', step.ToString());

// Complete word: stop and hand the Enter to the player.
step = Next("nidhogg", Words("nidhogg", "ifrit"));
Check("complete", step.Kind == StepKind.Ready && step.Target == "nidhogg", step.ToString());

// A longer field matches nothing: never touch text the plugin did not type.
step = Next("nidhogging", Words("nidhogg"));
Check("stranger-text", step.Kind == StepKind.Waiting, step.ToString());

// Text that is a prefix of no visible prompt waits for the field to clear.
step = Next("zzz", Words("nidhogg", "ifrit"));
Check("no-match", step.Kind == StepKind.Waiting, step.ToString());

// Accents are typed as their base letter, and the base-letter field counts as complete.
step = Next("", Words("café", "bomb"));
Check("accent-first", step.Kind == StepKind.Typing && step.Next == 'c' && step.Target == "café", step.ToString());
step = Next("cafe", Words("café"));
Check("accent-complete", step.Kind == StepKind.Ready, step.ToString());
step = Next("caf", Words("café"));
Check("accent-advance", step.Kind == StepKind.Typing && step.Next == 'e', step.ToString());

// A prompt that vanished mid-word leaves the field alone.
step = Next("nid", Words("ifrit"));
Check("vanished", step.Kind == StepKind.Waiting, step.ToString());

// The duty's own lock wins over everything, including a complete word.
step = Next("nidhogg", Words("nidhogg"), false, "Typing prohibited (3s).");
Check("lock", step.Kind == StepKind.Unavailable && step.Status == "Typing prohibited (3s).", step.ToString());

// Case-only difference between the field and the prompt still counts as typed.
step = Next("NIDHOGG", Words("nidhogg"));
Check("case-insensitive", step.Kind == StepKind.Ready, step.ToString());

Console.WriteLine($"{checks - failures}/{checks} checks passed.");
return failures == 0 ? 0 : 1;
