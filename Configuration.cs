using Dalamud.Configuration;
using Newtonsoft.Json;

namespace KeyboundAssist;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public int CharDelayMinMs { get; set; } = 150;

    public int CharDelayMaxMs { get; set; } = 300;

    public TargetPriority Priority { get; set; } = TargetPriority.ShortestPrompt;

    // Clears read from the Masterstroke achievements; null until one has been read.
    public int? AchievementWins { get; set; }

    // The player's own count, shown instead of the achievement one when it diverges.
    public bool UseManualWins { get; set; }

    public int ManualWins { get; set; }

    // Replace, not merge: Json.NET otherwise reuses the initialised collection on load.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, int> WordsSeen { get; set; } = [];

    public void Normalize()
    {
        // Equal bounds are a legal choice (fixed delay); a hand-edited file may hold min > max.
        if (CharDelayMinMs > CharDelayMaxMs) (CharDelayMinMs, CharDelayMaxMs) = (CharDelayMaxMs, CharDelayMinMs);
        CharDelayMinMs = Math.Clamp(CharDelayMinMs, TypingPlan.MinDelayMs, TypingPlan.MaxDelayMs);
        CharDelayMaxMs = Math.Clamp(CharDelayMaxMs, CharDelayMinMs, TypingPlan.MaxDelayMs);
        if (!Enum.IsDefined(Priority)) Priority = TargetPriority.ShortestPrompt;
        ManualWins = Math.Max(0, ManualWins);
        WordsSeen ??= [];
    }
}
