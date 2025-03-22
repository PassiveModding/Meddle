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
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.8f, 0, 0, 1)))
            {
                if (ImGui.MenuItem(FontAwesomeIcon.Heart.ToIconString()))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://ko-fi.com/ramen_au", UseShellExecute = true
                    });
                }
            }

            ImGui.EndMenuBar();
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
