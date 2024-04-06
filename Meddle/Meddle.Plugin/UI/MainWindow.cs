using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System.Numerics;
using ImGuiNET;
using Meddle.Plugin.Models.Config;

namespace Meddle.Plugin.UI;

public sealed class MainWindow : Window, IDisposable
{
    private readonly ITab[] tabs;
    private readonly IPluginLog log;

    public MainWindow(IEnumerable<ITab> tabs, Configuration config, IPluginLog log) : base("Meddle")
    {
        this.tabs = tabs.OrderBy(x => x.Order).ToArray();
        this.log = log;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 350),
            MaximumSize = new Vector2(1200, 1000),
        };

        IsOpen = config.AutoOpen;
    }

    private string? lastError;

    public override void Draw()
    {
        using var tabBar = ImRaii.TabBar("##meddleTabs");
        foreach (var tab in tabs)
        {
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
                    log.Error(e, $"Failed to draw {tab.Name}");
                }

                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Failed to draw {tab.Name} tab");
                ImGui.TextWrapped(e.ToString());
            }
        }
    }

    public void Dispose()
    {
        foreach (var tab in tabs)
            tab.Dispose();
    }
}
