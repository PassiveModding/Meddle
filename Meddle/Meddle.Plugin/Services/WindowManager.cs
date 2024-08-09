using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Meddle.Plugin.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class WindowManager : IHostedService, IDisposable
{
    private const string Command = "/meddle";
    private readonly ICommandManager commandManager;
    private readonly Configuration config;
    private readonly ILogger<WindowManager> log;
    private readonly IDalamudPluginInterface pluginInterface;

    private bool disposed;

    public WindowManager(
        MainWindow mainWindow,
        WindowSystem windowSystem,
        IDalamudPluginInterface pluginInterface,
        ILogger<WindowManager> log,
        Configuration config,
        ICommandManager commandManager)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.config = config;
        this.commandManager = commandManager;
        MainWindow = mainWindow;
        WindowSystem = windowSystem;
        config.OnConfigurationSaved += OnSave;
    }

    public WindowSystem WindowSystem { get; set; }
    public MainWindow MainWindow { get; set; }


    public void Dispose()
    {
        if (!disposed)
        {
            log.LogDebug("Disposing window manager");
            commandManager.RemoveHandler(Command);
            config.OnConfigurationSaved -= OnSave;
            pluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
            pluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
            pluginInterface.UiBuilder.OpenMainUi -= OpenUi;
            MainWindow.Dispose();
            WindowSystem.RemoveAllWindows();
            disposed = true;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        WindowSystem.AddWindow(MainWindow);

        pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
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
        MainWindow.IsOpen = true;
        MainWindow.BringToFront();
    }

    private void OnCommand(string command, string args)
    {
        OpenUi();
    }
}
