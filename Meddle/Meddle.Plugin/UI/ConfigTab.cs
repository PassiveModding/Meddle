using ImGuiNET;

namespace Meddle.Plugin.UI;

public class ConfigTab : ITab
{
    private readonly Configuration _configuration;

    public void Dispose()
    {
        //
    }

    public ConfigTab(Configuration configuration)
    {
        _configuration = configuration;
    }

    public string Name => "Config";
    public void Draw()
    {
        var autoOpen = _configuration.AutoOpen;
        if (ImGui.Checkbox("Auto-open", ref autoOpen))
        {
            _configuration.AutoOpen = autoOpen;
            _configuration.Save();
        }
    }
}