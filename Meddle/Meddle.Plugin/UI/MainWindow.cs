using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace Meddle.Plugin.UI;

public sealed class MainWindow : Window, IDisposable
{
    private ITab[] Tabs { get; }
    private IPluginLog Log { get; }

    public MainWindow(IEnumerable<ITab> tabs, Configuration config, IPluginLog log) : base("Meddle")
    {
        Tabs = tabs.OrderBy(x => x.Order).ToArray();
        Log = log;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 350),
            MaximumSize = new Vector2(1200, 1000),
        };
        
        IsOpen = config.AutoOpen;
    }

    private Dictionary<string, DateTime> ErrorLog { get; } = new();
    public override void Draw()
    {
        using var tabBar = ImRaii.TabBar("##meddleTabs");
        foreach (var tab in Tabs)
        {
            using var tabItem = ImRaii.TabItem(tab.Name);
            try
            {
                if (tabItem)
                    tab.Draw();
            }
            catch (Exception e)
            {
                var errorString = e.ToString();
                // compare error string to last error
                if (ErrorLog.TryGetValue(errorString, out var lastError) && (DateTime.Now - lastError < TimeSpan.FromSeconds(5)))
                    return; // Don't spam the log

                Log.Error(e, $"Error in {tab.Name}");
                ErrorLog[errorString] = DateTime.Now;
            }
        }
    }

    public void Dispose()
    {
        foreach (var tab in Tabs)
            tab.Dispose();
    }
}
