using ImGuiNET;

namespace Meddle.Plugin.UI;

public class ConfigTab : ITab
{
    public void Dispose()
    {
        //
    }

    public string Name => "Config";
    public void Draw()
    {
        var autoOpen = Plugin.Configuration.AutoOpen;
        if (ImGui.Checkbox("Auto-open", ref autoOpen))
        {
            Plugin.Configuration.AutoOpen = autoOpen;
            Plugin.Configuration.Save();
        }
    }
}