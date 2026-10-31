using Dalamud.Configuration;

namespace KeyboundAssist;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public int CharDelayMs { get; set; } = 40;

    public void Normalize() => CharDelayMs = Math.Clamp(CharDelayMs, 0, 300);
}
