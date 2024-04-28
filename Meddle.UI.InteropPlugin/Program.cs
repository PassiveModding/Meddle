using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Meddle.UI.InteropPlugin;

public sealed class Plugin : IDalamudPlugin
{
    public static void Main(string[] args) {}
    private static readonly WindowSystem WindowSystem = new("Meddle.UI.InteropPlugin");
    private readonly MainWindow mainWindow;
    public static Service Services { get; set; }
    public InteropService InteropService { get; private set; }
    public class Service
    {
        [PluginService] public IFramework Framework { get; set; } = null!;
        [PluginService] public DalamudPluginInterface PluginInterface { get; set; } = null!;
        [PluginService] public ICommandManager CommandManager { get; set; } = null!;
        [PluginService] public ISigScanner SigScanner { get; set; } = null!;
        [PluginService] public IPluginLog Log { get; set; } = null!;
    }

    public Plugin(DalamudPluginInterface pluginInterface)
    {
        Services = new Service();
        pluginInterface.Inject(Services);
        Services.CommandManager.AddHandler("/ioptest", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the menu"
        });
        
        mainWindow = new MainWindow("Meddle.UI.InteropPlugin");
        WindowSystem.AddWindow(mainWindow);
        mainWindow.IsOpen = true;
        
        InteropService = new InteropService(Services.SigScanner);
        Services.PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi += OpenUi;
        
        Task.Run(() =>
        {
            InteropService.Initialize();
        });
    }

    private void OnCommand(string command, string args)
    {
        OpenUi();
    }
    
    private void OpenUi()
    {
        mainWindow.IsOpen = true;
        mainWindow.BringToFront();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        Services.CommandManager.RemoveHandler("/ioptest");

        Services.PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenUi;
        
        mainWindow?.Dispose();
    }
}
