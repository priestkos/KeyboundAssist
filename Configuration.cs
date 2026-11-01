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

    public void Normalize()
    {
        // Equal bounds are a legal choice (fixed delay); a hand-edited file may hold min > max.
        if (CharDelayMinMs > CharDelayMaxMs) (CharDelayMinMs, CharDelayMaxMs) = (CharDelayMaxMs, CharDelayMinMs);
        CharDelayMinMs = Math.Clamp(CharDelayMinMs, 0, 300);
        CharDelayMaxMs = Math.Clamp(CharDelayMaxMs, CharDelayMinMs, 300);
        if (!Enum.IsDefined(Priority)) Priority = TargetPriority.ShortestPrompt;
    }
}
