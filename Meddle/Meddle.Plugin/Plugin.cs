using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Meddle.Plugin.UI;
using Meddle.Plugin.UI.Shared;
using Meddle.Plugin.Utility;
using Meddle.Xande;
using Microsoft.Extensions.DependencyInjection;
using Xande;
using Xande.Havok;

namespace Meddle.Plugin;

public class Plugin : IDalamudPlugin
{
    public string Name => "Meddle";
    private static readonly WindowSystem WindowSystem = new("Meddle");
    public static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "Meddle.Export");
    private readonly MainWindow _mainWindow;
    private readonly ICommandManager _commandManager;
    private readonly DalamudPluginInterface _pluginInterface;

    public Plugin(DalamudPluginInterface pluginInterface)
    {
        var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(pluginInterface);
        config.Save();

        var services = new ServiceCollection()
            .AddDalamud(pluginInterface)
            .AddUi()
            .AddSingleton(pluginInterface)
            .AddSingleton(config)
            .AddSingleton<HavokConverter>()
            .AddSingleton<ModelConverter>()
            .AddSingleton<LuminaManager>()
            .AddSingleton<ResourceTreeRenderer>()
            .BuildServiceProvider();
        
        _mainWindow = services.GetRequiredService<MainWindow>();
        WindowSystem.AddWindow(_mainWindow);
        
        _commandManager = services.GetRequiredService<ICommandManager>();
        _commandManager.AddHandler( "/meddle", new CommandInfo( OnCommand ) {
            HelpMessage = "Open the menu"
        } );
        
        _pluginInterface = services.GetRequiredService<DalamudPluginInterface>();
        _pluginInterface.UiBuilder.Draw         += DrawUi;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenUi;
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
        _commandManager.RemoveHandler( "/meddle" );

        _pluginInterface.UiBuilder.Draw         -= DrawUi;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
    }
}