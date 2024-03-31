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
    public static bool CsResolved { get; private set; } = false;
    private MainWindow? mainWindow = null;
    private readonly ICommandManager commandManager;
    private readonly DalamudPluginInterface pluginInterface;
    private readonly IGameInteropProvider gameInteropProvider;

    [Signature("40 53 48 83 EC 20 FF 81 ?? ?? ?? ?? 48 8B D9 48 8D 4C 24", DetourName = nameof(PostTickDetour))]
    private Hook<PostTickDelegate> postTickHook = null!;
    private delegate bool PostTickDelegate(nint a1);

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
            .AddSingleton<ExportManager>()
            .AddSingleton<ModelBuilder>()
            .BuildServiceProvider();
        
        commandManager = services.GetRequiredService<ICommandManager>();
        commandManager.AddHandler("/meddle", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the menu"
        });
        
        this.pluginInterface = services.GetRequiredService<DalamudPluginInterface>();
        this.pluginInterface.UiBuilder.Draw += DrawUi;
        this.pluginInterface.UiBuilder.OpenMainUi += OpenUi;
        this.pluginInterface.UiBuilder.OpenConfigUi += OpenUi;
        this.pluginInterface.UiBuilder.DisableGposeUiHide = true;

        gameInteropProvider = services.GetRequiredService<IGameInteropProvider>();
        mainWindow = services.GetRequiredService<MainWindow>();
        WindowSystem.AddWindow(mainWindow);
        
        // https://github.com/Caraxi/SimpleTweaksPlugin/blob/2b7c105d1671fd6a344edb5c621632b8825a81c5/SimpleTweaksPlugin.cs#L101C13-L103C75
        Task.Run(() =>
        {
            FFXIVClientStructs.Interop.Resolver.GetInstance.SetupSearchSpace(services.GetRequiredService<ISigScanner>().SearchBase);
            FFXIVClientStructs.Interop.Resolver.GetInstance.Resolve();
            
            services.GetRequiredService<IPluginLog>().Information("Resolved FFXIVClientStructs");
            CsResolved = true;
        });
    }

    private bool PostTickDetour(nint a1)
    {
        var ret = postTickHook.Original(a1);
        return ret;
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
    
    private void DrawUi()
    {
        WindowSystem.Draw();
    }

    public void Dispose()
    {
        postTickHook?.Dispose();
        mainWindow?.Dispose();
        WindowSystem.RemoveAllWindows();
        commandManager.RemoveHandler("/meddle");

        pluginInterface.UiBuilder.Draw -= DrawUi;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
    }
}
