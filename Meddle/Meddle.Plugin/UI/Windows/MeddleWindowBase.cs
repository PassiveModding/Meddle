using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI.Windows;


public abstract class MeddleWindowBase : Window
{
    private readonly ILogger log;
    private string? lastError;

    protected MeddleWindowBase(ILogger log, string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None) : base(name, flags)
    {
        this.log = log;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 350),
            MaximumSize = new Vector2(1200, 1000)
        };
    }
    
    protected abstract ITab[] GetTabs();
    
    protected virtual void BeforeDraw() { }

    public override void Draw()
    {
        BeforeDraw();
        using var tabBar = ImRaii.TabBar($"##{WindowName}tabs", ImGuiTabBarFlags.Reorderable);
        foreach (var tab in GetTabs())
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
                    log.LogError(e, "Failed to draw {TabName} tab", tab.Name);
                }

                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Failed to draw {tab.Name} tab");
                ImGui.TextWrapped(e.ToString());
            }
        }
    }
}
