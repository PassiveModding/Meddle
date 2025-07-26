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
    private readonly UpdateWindow updateWindow;
    private readonly ITab[] tabs;

    public MainWindow(IEnumerable<ITab> tabs, ILogger<MainWindow> log, DebugWindow debugWindow, LayoutWindow layoutWindow, UpdateWindow updateWindow) : 
        base(log, "Meddle", ImGuiWindowFlags.MenuBar)
    {
        this.tabs = tabs.OrderBy(x => x.Order).Where(x => x.MenuType == MenuType.Default).ToArray();
        this.log = log;
        this.debugWindow = debugWindow;
        this.layoutWindow = layoutWindow;
        this.updateWindow = updateWindow;
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
                    FileName = Constants.MeddleToolsUrl, UseShellExecute = true
                });
            }            
            
            if (ImGui.MenuItem("Discord"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.DiscordUrl, UseShellExecute = true
                });
            }

            if (ImGui.MenuItem("Github"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.MeddleUrl, UseShellExecute = true
                });
            }
            
            if (ImGui.MenuItem("Bug Report"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.MeddleBugReportUrl, UseShellExecute = true
                });
            }
            
            if (ImGui.MenuItem("Updates"))
            {
                updateWindow.IsOpen = true;
            }
            
            using (ImRaii.PushFont(UiBuilder.IconFont))
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.8f, 0, 0, 1)))
            {
                if (ImGui.MenuItem(FontAwesomeIcon.Heart.ToIconString()))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Constants.KoFiUrl, UseShellExecute = true
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
