using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Meddle.Plugin.UI;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Files.SqPack;
//using Meddle.Plugin.UI.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private static readonly WindowSystem WindowSystem = new("Meddle");
    public static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "Meddle.Export");
    private readonly MainWindow? mainWindow;
    private readonly ICommandManager? commandManager;
    private readonly IDalamudPluginInterface? pluginInterface;
    private readonly IPluginLog? log;
    private readonly ServiceProvider? services;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        try
        {
            var serviceCollection = new ServiceCollection()
                            .AddDalamud(pluginInterface)
                            .AddUi()
                            .AddSingleton(pluginInterface)
                            .AddSingleton<InteropService>()
                            .AddSingleton<ExportUtil>();
            
            var gameDir = Environment.CurrentDirectory;
            var sqPack = new SqPack(gameDir);
            serviceCollection.AddSingleton(sqPack);
            
            services = serviceCollection
                           .BuildServiceProvider();
            
            log = services.GetRequiredService<IPluginLog>();

            log.Info($"Game directory: {gameDir}");
            
            commandManager = services.GetRequiredService<ICommandManager>();
            this.pluginInterface = services.GetRequiredService<IDalamudPluginInterface>();

            commandManager.AddHandler("/meddle", new CommandInfo(OnCommand)
            {
                HelpMessage = "Open the menu"
            });

            this.pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
            this.pluginInterface.UiBuilder.OpenMainUi += OpenUi;
            this.pluginInterface.UiBuilder.OpenConfigUi += OpenUi;
            this.pluginInterface.UiBuilder.DisableGposeUiHide = true;
            this.pluginInterface.UiBuilder.DisableCutsceneUiHide = true;
            mainWindow = services.GetRequiredService<MainWindow>();
            WindowSystem.AddWindow(mainWindow);

            OtterTex.NativeDll.Initialize(this.pluginInterface.AssemblyLocation.DirectoryName);

            #if DEBUG
                OpenUi();
            #endif
            
            Task.Run(() =>
            {
                var interop = services.GetRequiredService<InteropService>();
                interop.Initialize();
            });
        }
        catch (Exception e)
        {
            log?.Error(e, "Failed to initialize plugin");
            Dispose();
        }
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
        commandManager?.RemoveHandler("/meddle");

        if (pluginInterface != null)
        {
            pluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
            pluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
            pluginInterface.UiBuilder.OpenMainUi -= OpenUi;
        }
        
        services?.Dispose();
    }
}
