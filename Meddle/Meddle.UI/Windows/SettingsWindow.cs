using System.Diagnostics;
using ImGuiNET;

namespace Meddle.UI.Windows;

public class SettingsWindow(Configuration configuration)
{
    private bool showSettings;
    private string gameDir = configuration.GameDirectory;
    private int interopPort = configuration.InteropPort;
    
    public event RedrawBrowserEventHandler? OnRedrawBrowser;
    public delegate void RedrawBrowserEventHandler();

    public void Draw()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("Settings"))
            {
                if (ImGui.MenuItem("Open"))
                {
                    showSettings = true;
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Tools"))
            {
                if (ImGui.MenuItem("Open in Explorer"))
                {
                    Process.Start("explorer.exe", Program.DataDirectory);
                }

                if (ImGui.MenuItem("Open in Explorer (Game Directory)"))
                {
                    Process.Start("explorer.exe", configuration.GameDirectory);
                }

                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }

        if (showSettings)
        {
            ImGui.OpenPopup("Settings");
            if (ImGui.BeginPopupModal("Settings"))
            {
                ImGui.Text("Game Directory");
                ImGui.SameLine();
                ImGui.InputText("##GameDirectory", ref gameDir, 1024);
                ImGui.Text("Interop Plugin Port");
                ImGui.SameLine();
                ImGui.InputInt("##InteropPort", ref interopPort);
                
                if (ImGui.Button("OK"))
                {
                    OnRedrawBrowser?.Invoke();
                    showSettings = false;
                    configuration.GameDirectory = gameDir;
                    configuration.InteropPort = interopPort;
                    configuration.Save();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
    }
}
