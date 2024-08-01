using System.Diagnostics;
using ImGuiNET;

namespace Meddle.Plugin.UI;

public class OptionsTab : ITab
{
    private readonly Configuration config;

    public OptionsTab(Configuration config)
    {
        this.config = config;
    }

    public bool DisplayTab => true;

    public void Dispose() { }

    public string Name => "Options";
    public int Order => 2;

    public void Draw()
    {
        if (ImGui.Button("Open output folder"))
        {
            Process.Start("explorer.exe", Plugin.TempDirectory);
        }

        var advanced = config.ShowAdvanced;
        if (ImGui.Checkbox("Show Advanced", ref advanced))
        {
            config.ShowAdvanced = advanced;
            config.Save();
        }

        var openOnLoad = config.OpenOnLoad;
        if (ImGui.Checkbox("Open on load", ref openOnLoad))
        {
            config.OpenOnLoad = openOnLoad;
            config.Save();
        }
        
        var disableUserUiHide = config.DisableUserUiHide;
        if (ImGui.Checkbox("Disable User UI Hide", ref disableUserUiHide))
        {
            config.DisableUserUiHide = disableUserUiHide;
            config.Save();
        }
        
        var disableAutomaticUiHide = config.DisableAutomaticUiHide;
        if (ImGui.Checkbox("Disable Automatic UI Hide", ref disableAutomaticUiHide))
        {
            config.DisableAutomaticUiHide = disableAutomaticUiHide;
            config.Save();
        }
        
        var disableCutsceneUiHide = config.DisableCutsceneUiHide;
        if (ImGui.Checkbox("Disable Cutscene UI Hide", ref disableCutsceneUiHide))
        {
            config.DisableCutsceneUiHide = disableCutsceneUiHide;
            config.Save();
        }
        
        var disableGposeUiHide = config.DisableGposeUiHide;
        if (ImGui.Checkbox("Disable Gpose UI Hide", ref disableGposeUiHide))
        {
            config.DisableGposeUiHide = disableGposeUiHide;
            config.Save();
        }
        
        var playerNameOverride = config.PlayerNameOverride;
        if (ImGui.InputText("Player Name Override", ref playerNameOverride, 64))
        {
            config.PlayerNameOverride = playerNameOverride;
            config.Save();
        }
    }
}
