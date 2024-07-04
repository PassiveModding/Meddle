using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Meddle.Plugin.UI;
using Meddle.Utils;
//using Meddle.Plugin.UI.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private static readonly WindowSystem WindowSystem = new("Meddle");
    public static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "Meddle.Export");
    private readonly MainWindow? mainWindow;
    private readonly ICommandManager commandManager;
    private readonly IDalamudPluginInterface pluginInterface;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        var services = new ServiceCollection()
            .AddDalamud(pluginInterface)
            .AddUi()
            .AddSingleton(pluginInterface)
            .AddSingleton<ModelBuilder>()
            .AddSingleton<InteropService>()
            .BuildServiceProvider();
        
        commandManager = services.GetRequiredService<ICommandManager>();
        commandManager.AddHandler("/meddle", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the menu"
        });
        
        this.pluginInterface = services.GetRequiredService<IDalamudPluginInterface>();
        this.pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        this.pluginInterface.UiBuilder.OpenMainUi += OpenUi;
        this.pluginInterface.UiBuilder.OpenConfigUi += OpenUi;
        mainWindow = services.GetRequiredService<MainWindow>();
        WindowSystem.AddWindow(mainWindow);
        
        Task.Run(() =>
        {
            var interop = services.GetRequiredService<InteropService>();
            interop.Initialize();
        });
    }


    private void OnCommand(string command, string args)
    {
        OpenUi();
    }
    
    private void OpenUi()
    {
        if (mainWindow == null)
            return;
        mainWindow.IsOpen = true;
        mainWindow.BringToFront();
    }

    public void Dispose()
    {
        mainWindow?.Dispose();
        WindowSystem.RemoveAllWindows();
        commandManager.RemoveHandler("/meddle");

        pluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
        pluginInterface.UiBuilder.OpenMainUi -= OpenUi;
    }
}
