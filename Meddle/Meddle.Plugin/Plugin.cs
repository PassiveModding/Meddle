using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI;
//using Meddle.Plugin.UI.Shared;
using Meddle.Plugin.Utility;
using Microsoft.Extensions.DependencyInjection;
using Meddle.Plugin.Xande;

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
        var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(pluginInterface);
        config.Save();

        var services = new ServiceCollection()
            .AddDalamud(pluginInterface)
            .AddUi()
            .AddSingleton(pluginInterface)
            .AddSingleton(config)
            .AddSingleton<ExportManager>()
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
        this.pluginInterface.UiBuilder.DisableGposeUiHide = config.DisableGposeUiHide;
        this.pluginInterface.UiBuilder.DisableCutsceneUiHide = config.DisableCutsceneUiHide;
        config.OnChange += () =>
        {
            this.pluginInterface.UiBuilder.DisableGposeUiHide = config.DisableGposeUiHide;
            this.pluginInterface.UiBuilder.DisableCutsceneUiHide = config.DisableCutsceneUiHide;
        };

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
