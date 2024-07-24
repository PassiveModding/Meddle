using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public sealed class MainWindow : Window, IDisposable
{
    private readonly ILogger<MainWindow> log;
    private readonly ITab[] tabs;

    private string? lastError;

    public MainWindow(IEnumerable<ITab> tabs, ILogger<MainWindow> log) : base("Meddle")
    {
        this.tabs = tabs.OrderBy(x => x.Order).ToArray();
        this.log = log;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 350),
            MaximumSize = new Vector2(1200, 1000)
        };
    }

    public void Dispose()
    {
        foreach (var tab in tabs)
        {
            tab.Dispose();
        }
    }

    public override void Draw()
    {
        using var tabBar = ImRaii.TabBar("##meddleTabs");
        foreach (var tab in tabs)
        {
            if (!tab.DisplayTab)
                continue;
            using var tabItem = ImRaii.TabItem(tab.Name);
            try
            {
                if (tabItem)
                {
                    tab.Draw();
                }
            }
            catch (Exception e)
            {
                var errStr = e.ToString();
                if (errStr != lastError)
                {
                    lastError = errStr;
                    log.LogError(e, "Failed to draw {TabName} tab", tab.Name);
                }

                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Failed to draw {tab.Name} tab");
                ImGui.TextWrapped(e.ToString());
            }
        }
    }
}
