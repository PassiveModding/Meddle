using Dalamud.Configuration;

namespace Meddle.Plugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool AutoOpen { get; set; } = false;
    public void Save()
    {
        Service.PluginInterface.SavePluginConfig(this);
    }
}