using System.Numerics;
using ImGuiNET;
using Meddle.Plugin.Models;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI.Windows;

public sealed class TestingWindow : MeddleWindowBase
{
    private readonly ILogger<TestingWindow> log;
    private readonly ITab[] tabs;

    public TestingWindow(IEnumerable<ITab> tabs, ILogger<TestingWindow> log) : base(log,"MeddleTesting")
    {
        this.tabs = tabs.OrderBy(x => x.Order).Where(x => x.MenuType == MenuType.Testing).ToArray();
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

    protected override void PreDraw()
    {
        ImGui.TextColored(new Vector4(1, 0, 0, 1), 
                          "This is a testing window. " +
                            "Expect bugs.");
    }

    public void Dispose()
    {
        foreach (var tab in tabs)
        {
            tab.Dispose();
        }
    }
}
