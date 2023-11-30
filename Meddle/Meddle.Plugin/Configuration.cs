using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Meddle.Plugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private DalamudPluginInterface _pluginInterface = null!;
    public void Initialize(DalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
    }

    public int Version { get; set; } = 0;

    public bool AutoOpen { get; set; } = false;
    public void Save()
    {
        _pluginInterface.SavePluginConfig(this);
    }
}