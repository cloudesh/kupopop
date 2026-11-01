using Dalamud.Configuration;

namespace Kupopop;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool ReduceMotion { get; set; }
    public bool DarkMode { get; set; }
    public bool CanvasToolsCollapsed { get; set; }
    public bool BackgroundInvitations { get; set; }
    public string? DismissedUpdate { get; set; }
}
