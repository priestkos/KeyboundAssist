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

    // Sends the finished word itself after a fresh delay in this range. Off unless the player turns it on.
    public bool AutoSubmit { get; set; }

    public int SubmitDelayMinMs { get; set; } = 300;

    public int SubmitDelayMaxMs { get; set; } = 500;

    // Clears read from the Masterstroke achievements; null until one has been read.
    public int? AchievementWins { get; set; }

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
        if (SubmitDelayMinMs > SubmitDelayMaxMs) (SubmitDelayMinMs, SubmitDelayMaxMs) = (SubmitDelayMaxMs, SubmitDelayMinMs);
        SubmitDelayMinMs = Math.Clamp(SubmitDelayMinMs, TypingPlan.SubmitFloorMs, TypingPlan.SubmitCapMs);
        SubmitDelayMaxMs = Math.Clamp(SubmitDelayMaxMs, SubmitDelayMinMs, TypingPlan.SubmitCapMs);
        WordsSeen ??= [];
    }
}
