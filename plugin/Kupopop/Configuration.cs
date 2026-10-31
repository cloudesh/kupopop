using Dalamud.Configuration;

namespace Kupopop;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool ReduceMotion { get; set; }
    public bool UseStandardFrame { get; set; }
    public bool? PaperPinned { get; set; }
}
