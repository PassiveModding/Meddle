using System.Diagnostics;
using ImGuiNET;

namespace Meddle.UI.Windows;

public class SettingsWindow
{
    private bool showSettings;
    private string gameDir;
    private int interopPort;
    private bool assetCCResolve;
    private readonly Configuration configuration1;

    public SettingsWindow(Configuration configuration)
    {
        configuration1 = configuration;
        gameDir = configuration.GameDirectory;
        interopPort = configuration.InteropPort;
        assetCCResolve = configuration.AssetCcResolve;
    }

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
                    Process.Start("explorer.exe", configuration1.GameDirectory);
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
                ImGui.Checkbox("Use AssetCC Resolve", ref assetCCResolve);
                
                
                if (ImGui.Button("OK"))
                {
                    OnRedrawBrowser?.Invoke();
                    showSettings = false;
                    configuration1.GameDirectory = gameDir;
                    configuration1.InteropPort = interopPort;
                    configuration1.AssetCcResolve = assetCCResolve;
                    configuration1.Save();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
    }
}
