using Dalamud.Configuration;

namespace Kupopop;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string ServerUrl { get; set; } = "https://localhost:7443";
}
