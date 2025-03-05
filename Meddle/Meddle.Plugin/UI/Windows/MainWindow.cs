using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.UI.Layout;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI.Windows;

public sealed class MainWindow : MeddleWindowBase
{
    private readonly ILogger<MainWindow> log;
    private readonly DebugWindow debugWindow;
    private readonly LayoutWindow layoutWindow;
    private readonly ITab[] tabs;

    public MainWindow(IEnumerable<ITab> tabs, ILogger<MainWindow> log, DebugWindow debugWindow, LayoutWindow layoutWindow) : 
        base(log, "Meddle", ImGuiWindowFlags.MenuBar)
    {
        this.tabs = tabs.OrderBy(x => x.Order).Where(x => x.MenuType == MenuType.Default).ToArray();
        this.log = log;
        this.debugWindow = debugWindow;
        this.layoutWindow = layoutWindow;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 350),
            MaximumSize = new Vector2(1200, 1000)
        };
    }
    
    private void DrawMenuFont(FontAwesomeIcon icon, string tooltip, Action click)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.MenuItem(icon.ToIconString()))
            {
                click();
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }
    
    protected override void BeforeDraw()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.MenuItem("Debug"))
            {
                debugWindow.IsOpen = true;
            }
            
            if (ImGui.MenuItem("Blender Addon"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/PassiveModding/MeddleTools", UseShellExecute = true
                });
            }

            if (ImGui.MenuItem("Github"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/PassiveModding/Meddle", UseShellExecute = true
                });
            }
            
            if (ImGui.MenuItem("Bug Report"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/PassiveModding/Meddle/issues", UseShellExecute = true
                });
            }
            
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.MenuItem(FontAwesomeIcon.Heart.ToIconString()))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/sponsors/PassiveModding", UseShellExecute = true
                    });
                }
            }

            ImGui.EndMenuBar();
        }
        
        // warn color
        using (var colours = ImRaii.PushColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1)))
        {
            ImGui.Text("This is a TEST release of Meddle, many things have changed. Release notes may be found on the github release page.");
            ImGui.Text("Please report any issues you find on the github issue tracker.");
            ImGui.Text("For MeddleTools Blender Addon, version 0.0.23 is in pre-release, older versions may not support this version.");
        }
        if (ImGui.Button("MeddleTools Blender Addon v0.0.23"))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/PassiveModding/MeddleTools/releases/tag/0.0.23", UseShellExecute = true
            });
        }
    }
    
    protected override ITab[] GetTabs()
    {
        return tabs;
    }

    public void Dispose()
    {
        foreach (var tab in tabs)
        {
            tab.Dispose();
        }
    }
}
