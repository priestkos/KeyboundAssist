using System.Numerics;

namespace KeyboundAssist;

internal enum TargetPriority { ShortestPrompt, NearestEnemy }

// One enemy prompt. No Dalamud types, so tests/ compiles this file with the decision logic.
internal readonly record struct Prompt(uint NodeId, string Text, bool Masked,
    float ScreenX, float ScreenY, float Scale, float? Distance);

internal readonly record struct ActorScreen(uint Entity, Vector2 Screen, Vector3 World);

internal static class PromptMap
{
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
        for (var i = 0; i < prompts.Length; i++)
        {
            var prompt = prompts[i];
            var limit = MaxOffset * Math.Max(prompt.Scale, 0.5f);
            var candidates = actors.Where(a => a.Screen.Y > prompt.ScreenY)
                .Select(a => (Actor: a, Offset: Math.Abs(a.Screen.X - prompt.ScreenX)))
                .OrderBy(c => c.Offset).Take(2).ToArray();
            if (candidates.Length == 0 || candidates[0].Offset > limit) continue;
            if (candidates.Length > 1 && candidates[1].Offset - candidates[0].Offset < AmbiguityMargin) continue;
            pairs.Add((i, candidates[0].Actor, candidates[0].Offset));
        }

        // An enemy carries one prompt; an enemy two labels claim resolves neither of them.
        foreach (var pair in pairs.GroupBy(p => p.Actor.Entity).Where(g => g.Count() == 1).Select(g => g.Single()))
        {
            var distance = Vector3.Distance(origin, pair.Actor.World);
            if (float.IsFinite(distance))
                result[pair.Prompt] = result[pair.Prompt] with { Distance = distance };
        }
        return result;
    }
}
