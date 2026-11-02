using System.Numerics;

namespace KeyboundAssist;

internal enum TargetPriority { ShortestPrompt, NearestEnemy }

// One enemy prompt. No Dalamud types, so tests/ compiles this file with the decision logic.
internal readonly record struct Prompt(uint NodeId, string Text, bool Masked,
    float ScreenX, float ScreenY, float Scale, float? Distance, bool Defense = false);

internal readonly record struct ActorScreen(uint Entity, Vector2 Screen, Vector3 World);

internal static class PromptMap
{
    // A player defence prompt is drawn at the barrier line at the bottom of the screen (live geometry
    // from AutoKeyboundBrawler: ScreenY ~734 of 1080 there, marching adds stop around 680), expressed as
    // a fraction so it holds at any window size rather than only the 1080p it was measured at.
    internal const float DefenseLineFraction = 700f / 1080f;

    internal static bool IsDefensePrompt(float screenY, float screenHeight) =>
        screenHeight > 0 && screenY >= screenHeight * DefenseLineFraction;
    // Labels float above their enemy, so matching is by projected screen position. Calibrated live on
    // this client: a label sits within 48 px * scale of its enemy, and two enemies closer than 12 px
    // in screen X cannot be told apart.
    private const float MaxOffset = 48;
    private const float AmbiguityMargin = 12;

    // Live 2026-10-28: label scale falls linearly from 1.50 at 12.7 yalms to 1.14 at about 24 yalms and
    // stays at 1.14 beyond that. A saturated label carries no distance.
    internal static float? EstimateDistance(float scale) =>
        scale > 1.15f && float.IsFinite(scale) ? Math.Max(0, 24 + (1.14f - scale) * 31.3f) : null;

    internal static Prompt[] WithDistances(Prompt[] prompts, IReadOnlyList<ActorScreen> actors, Vector3 origin)
    {
        var result = prompts.ToArray();
        var pairs = new List<(int Prompt, ActorScreen Actor, float Offset)>();
        // Outcomes where the match was offered and deliberately refused: these keep no distance at all,
        // not even the scale proxy, because the proxy is a guess and these are the cases the match
        // already decided it must not guess.
        var rejected = new HashSet<int>();
        for (var i = 0; i < prompts.Length; i++)
        {
            var prompt = prompts[i];
            var limit = MaxOffset * Math.Max(prompt.Scale, 0.5f);
            var candidates = actors.Where(a => a.Screen.Y > prompt.ScreenY)
                .Select(a => (Actor: a, Offset: Math.Abs(a.Screen.X - prompt.ScreenX)))
                .OrderBy(c => c.Offset).Take(2).ToArray();
            if (candidates.Length == 0 || candidates[0].Offset > limit) continue;
            if (candidates.Length > 1 && candidates[1].Offset - candidates[0].Offset < AmbiguityMargin)
            {
                rejected.Add(i);
                continue;
            }
            pairs.Add((i, candidates[0].Actor, candidates[0].Offset));
        }

        // An enemy carries one prompt; an enemy two labels claim resolves neither of them.
        foreach (var group in pairs.GroupBy(p => p.Actor.Entity))
        {
            if (group.Count() != 1)
            {
                foreach (var contested in group) rejected.Add(contested.Prompt);
                continue;
            }
            var pair = group.Single();
            var distance = Vector3.Distance(origin, pair.Actor.World);
            if (float.IsFinite(distance))
                result[pair.Prompt] = result[pair.Prompt] with { Distance = distance };
        }
        // A label that matched no enemy still carries a distance proxy: a bigger label is nearer. This is
        // the case XivType added the estimate for - a bound far boss otherwise outranks a close unbound
        // enemy. Saturated labels (> ~24 yalms) give none and sort last.
        for (var i = 0; i < result.Length; i++)
            if (!rejected.Contains(i) && result[i].Distance == null &&
                EstimateDistance(result[i].Scale) is { } estimate)
                result[i] = result[i] with { Distance = estimate };
        return result;
    }
}
