using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Meddle.Plugin.UI;

namespace Meddle.Plugin;

public class Plugin : IDalamudPlugin
{
    public string Name => "Meddle";

    public static Configuration Configuration { get; private set; } = null!;
    private static readonly WindowSystem WindowSystem = new("Meddle");
    private readonly MainWindow _mainWindow;

    public Plugin(DalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Save();

        var resourceTab = new ResourceTab();
        _mainWindow = new MainWindow(new ITab[]
        {
            resourceTab,
            new ConfigTab()
        });
        WindowSystem.AddWindow(_mainWindow);
        
        Service.CommandManager.AddHandler( "/meddle", new CommandInfo( OnCommand ) {
            HelpMessage = "Open the menu"
        } );
        
        Service.PluginInterface.UiBuilder.Draw         += DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi += OpenUi;
    }
    
    private void OnCommand( string command, string args ) {
        OpenUi();
    }

    private void OpenUi() {
        _mainWindow.IsOpen = true;
    }

    private void DrawUi() {
        WindowSystem.Draw();
    }

    public void Dispose()
    {        
        _mainWindow.Dispose();
        WindowSystem.RemoveAllWindows();

        Service.CommandManager.RemoveHandler( "/meddle" );

        Service.PluginInterface.UiBuilder.Draw         -= DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
    }
}