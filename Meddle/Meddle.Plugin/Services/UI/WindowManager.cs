using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Meddle.Plugin.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services.UI;

public class WindowManager : IHostedService, IDisposable
{
    private const string Command = "/meddle";
    private readonly ICommandManager commandManager;
    private readonly Configuration config;
    private readonly ILogger<WindowManager> log;
    private readonly OverlayWindow overlayWindow;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly MainWindow mainWindow;
    private readonly WindowSystem windowSystem;
    
    private bool disposed;

    public WindowManager(
        MainWindow mainWindow,
        OverlayWindow overlayWindow,
        WindowSystem windowSystem,
        IDalamudPluginInterface pluginInterface,
        ILogger<WindowManager> log,
        Configuration config,
        ICommandManager commandManager)
    {
        this.overlayWindow = overlayWindow;
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.config = config;
        this.commandManager = commandManager;
        this.mainWindow = mainWindow;
        this.windowSystem = windowSystem;
    }



    public void Dispose()
    {
        if (!disposed)
        {
            log.LogDebug("Disposing window manager");
            commandManager.RemoveHandler(Command);
            config.OnConfigurationSaved -= OnSave;
            pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
            pluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
            pluginInterface.UiBuilder.OpenMainUi -= OpenUi;
            mainWindow.Dispose();
            windowSystem.RemoveAllWindows();
            disposed = true;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(overlayWindow);

        config.OnConfigurationSaved += OnSave;
        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        
        pluginInterface.UiBuilder.OpenMainUi += OpenUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenUi;
        pluginInterface.UiBuilder.DisableGposeUiHide = config.DisableGposeUiHide;
        pluginInterface.UiBuilder.DisableCutsceneUiHide = config.DisableCutsceneUiHide;
        pluginInterface.UiBuilder.DisableAutomaticUiHide = config.DisableAutomaticUiHide;
        pluginInterface.UiBuilder.DisableUserUiHide = config.DisableUserUiHide;

        if (config.OpenOnLoad)
        {
            OpenUi();
        }

        commandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the menu"
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private void OnSave()
    {
        pluginInterface.UiBuilder.DisableGposeUiHide = config.DisableGposeUiHide;
        pluginInterface.UiBuilder.DisableCutsceneUiHide = config.DisableCutsceneUiHide;
        pluginInterface.UiBuilder.DisableAutomaticUiHide = config.DisableAutomaticUiHide;
        pluginInterface.UiBuilder.DisableUserUiHide = config.DisableUserUiHide;
    }

    public void OpenUi()
    {
        mainWindow.IsOpen = true;
        mainWindow.BringToFront();
    }

    private void OnCommand(string command, string args)
    {
        OpenUi();
    }
}
