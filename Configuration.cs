using Dalamud.Configuration;

namespace KeyboundAssist;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public int CharDelayMinMs { get; set; } = 30;

    public int CharDelayMaxMs { get; set; } = 60;

    public TargetPriority Priority { get; set; } = TargetPriority.ShortestPrompt;

    // Clears the game's own inactivity counters while the assist is filling prompts; off leaves the
    // duty's expulsion timer entirely to the player's real input.
    public bool KeepAlive { get; set; } = true;

    // The duty matches Latin prompts without their punctuation, so typing "dontstop" answers
    // "don't-stop!". Off types the prompt exactly as drawn.
    public bool IgnorePunctuation { get; set; } = true;

    public void Normalize()
    {
        // Equal bounds are a legal choice (fixed delay); a hand-edited file may hold min > max.
        if (CharDelayMinMs > CharDelayMaxMs) (CharDelayMinMs, CharDelayMaxMs) = (CharDelayMaxMs, CharDelayMinMs);
        CharDelayMinMs = Math.Clamp(CharDelayMinMs, 0, TypingPlan.MaxDelayMs);
        CharDelayMaxMs = Math.Clamp(CharDelayMaxMs, CharDelayMinMs, TypingPlan.MaxDelayMs);
        if (!Enum.IsDefined(Priority)) Priority = TargetPriority.ShortestPrompt;
    }
}
