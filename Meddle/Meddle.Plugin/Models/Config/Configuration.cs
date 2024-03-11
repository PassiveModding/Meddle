using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Meddle.Plugin.Models.Config;

[Serializable]
public class Configuration : IPluginConfiguration
{
    private DalamudPluginInterface PluginInterface { get; set; } = null!;

    public int Version { get; set; }

    public bool AutoOpen { get; set; }

    public void Initialize(DalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
    }
}
