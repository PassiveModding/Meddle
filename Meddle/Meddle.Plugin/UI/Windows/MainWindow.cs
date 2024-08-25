using System.Numerics;
using Meddle.Plugin.Models;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI.Windows;

public sealed class MainWindow : MeddleWindowBase
{
    private readonly ILogger<MainWindow> log;
    private readonly ITab[] tabs;

    public MainWindow(IEnumerable<ITab> tabs, ILogger<MainWindow> log) : base(log, "Meddle")
    {
        this.tabs = tabs.OrderBy(x => x.Order).Where(x => x.MenuType == MenuType.Default).ToArray();
        this.log = log;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 350),
            MaximumSize = new Vector2(1200, 1000)
        };
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
