using ImGuiNET;

namespace Meddle.Plugin.UI;

public sealed class ConfigTab : ITab
{
    public string Name => "Config";

    public int Order => int.MaxValue;

    private Configuration Config { get; }

    public ConfigTab(Configuration configuration)
    {
        Config = configuration;
    }

    public void Draw()
    {
        var autoOpen = Config.AutoOpen;
        if (ImGui.Checkbox("Auto-open", ref autoOpen))
        {
            Config.AutoOpen = autoOpen;
            Config.Save();
        }
    }

    public void Dispose()
    {
        
    }
}
