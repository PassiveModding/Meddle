using System.Diagnostics;
using ImGuiNET;

namespace Meddle.Plugin.UI;

public class OptionsTab : ITab
{
    
    
    public void Dispose()
    {
    }

    public string Name => "Options";
    public int Order => 2;
    public void Draw()
    {
        if (ImGui.Button("Open output folder"))
        {
            Process.Start("explorer.exe", Plugin.TempDirectory);
        }
    }
}
