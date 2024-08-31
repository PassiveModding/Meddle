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
            DrawMenuFont(FontAwesomeIcon.ProjectDiagram, "Layout Window", () => layoutWindow.IsOpen = true);
            DrawMenuFont(FontAwesomeIcon.Bug, "Debug", () => debugWindow.IsOpen = true);
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
