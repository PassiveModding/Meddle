using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin;

public class Service
{
    [PluginService]
    private IDalamudPluginInterface PrivPluginInterface { get; set; } = null!;

    [PluginService]
    private IFramework Framework { get; set; } = null!;

    [PluginService]
    private ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private IPluginLog PrivLog { get; set; } = null!;

    [PluginService]
    private IObjectTable ObjectTable { get; set; } = null!;

    [PluginService]
    private IClientState ClientState { get; set; } = null!;

    [PluginService]
    private ISigScanner SigScanner { get; set; } = null!;

    [PluginService]
    private IGameInteropProvider GameInteropProvider { get; set; } = null!;

    [PluginService]
    private ITextureProvider TextureProvider { get; set; } = null!;

    [PluginService]
    private IDataManager DataManager { get; set; } = null!;
    
    [PluginService]
    private INotificationManager NotificationManager { get; set; } = null!;

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton(PrivPluginInterface);
        services.AddSingleton(Framework);
        services.AddSingleton(CommandManager);
        services.AddSingleton(PrivLog);
        services.AddSingleton(ObjectTable);
        services.AddSingleton(ClientState);
        services.AddSingleton(SigScanner);
        services.AddSingleton(GameInteropProvider);
        services.AddSingleton(DataManager);
        services.AddSingleton(TextureProvider);
        services.AddSingleton(NotificationManager);
        PoseUtil.SigScanner = SigScanner;
    }
}
