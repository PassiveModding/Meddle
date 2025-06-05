using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Meddle.Plugin.UI.Layout;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI.Windows;

public class WindowManager : IHostedService, IDisposable
{
    private const string Command = "/meddle";
    private readonly ICommandManager commandManager;
    private readonly Configuration config;
    private readonly ILogger<WindowManager> log;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly MainWindow mainWindow;
    private readonly DebugWindow debugWindow;
    private readonly LayoutWindow layoutWindow;
    private readonly WindowSystem windowSystem;
    private readonly UpdateWindow updateWindow;

    private bool disposed;

    public WindowManager(
        MainWindow mainWindow,
        DebugWindow debugWindow,
        LayoutWindow layoutWindow,
        WindowSystem windowSystem,
        UpdateWindow updateWindow,
        IDalamudPluginInterface pluginInterface,
        ILogger<WindowManager> log,
        Configuration config,
        ICommandManager commandManager)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.config = config;
        this.commandManager = commandManager;
        this.mainWindow = mainWindow;
        this.debugWindow = debugWindow;
        this.layoutWindow = layoutWindow;
        this.windowSystem = windowSystem;
        this.updateWindow = updateWindow;
    }
    
    public void Dispose()
    {
        if (!disposed)
        {
            log.LogDebug("Disposing window manager");
            commandManager.RemoveHandler(Command);
            config.OnConfigurationSaved -= OnSave;
            pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
            pluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi;
            pluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
            mainWindow.Dispose();
            windowSystem.RemoveAllWindows();
            disposed = true;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(debugWindow);
        windowSystem.AddWindow(updateWindow);

        config.OnConfigurationSaved += OnSave;
        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        
        pluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenMainUi;
        pluginInterface.UiBuilder.DisableGposeUiHide = config.DisableGposeUiHide;
        pluginInterface.UiBuilder.DisableCutsceneUiHide = config.DisableCutsceneUiHide;
        pluginInterface.UiBuilder.DisableAutomaticUiHide = config.DisableAutomaticUiHide;
        pluginInterface.UiBuilder.DisableUserUiHide = config.DisableUserUiHide;

        if (config.OpenOnLoad)
        {
            OpenMainUi();
        }

        if (config.OpenDebugMenuOnLoad)
        {
            OpenDebugUi();
        }
        
        if (config.UpdateConfig.ShowUpdateWindow && 
            config.UpdateConfig.LastSeenUpdateTag != UpdateWindow.UpdateLogs.LastOrDefault()?.Tag)
        {
            updateWindow.IsOpen = true;
            updateWindow.BringToFront();
        }
        
        // if (config.OpenLayoutMenuOnLoad)
        // {
        //     OpenLayoutUi();
        // }

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

    public void OpenMainUi()
    {
        mainWindow.IsOpen = true;
        mainWindow.BringToFront();
    }

    public void OpenDebugUi()
    {
        debugWindow.IsOpen = true;
        debugWindow.BringToFront();
    }
    
    // public void OpenLayoutUi()
    // {
    //     layoutWindow.IsOpen = true;
    //     layoutWindow.BringToFront();
    // }
    
    private void OnCommand(string command, string args)
    {
        if (!string.IsNullOrEmpty(args))
        {
            log.LogDebug("Received command with args: {Args}", args);
            if (args.Equals("debug", StringComparison.OrdinalIgnoreCase))
            {
                OpenDebugUi();
                return;
            }
            
            // if (args.Equals("layout", StringComparison.OrdinalIgnoreCase))
            // {
            //     OpenLayoutUi();
            //     return;
            // }
        }
        
        OpenMainUi();
    }
}
