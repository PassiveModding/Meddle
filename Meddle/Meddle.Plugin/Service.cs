using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin;

public class Service
{
    public Service(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Inject(this);
        Log = PrivLog;
    }

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton(PluginInterface);
        services.AddSingleton(Framework);
        services.AddSingleton(CommandManager);
        services.AddSingleton(PrivLog);
        services.AddSingleton(ObjectTable);
        services.AddSingleton(ClientState);
        services.AddSingleton(SigScanner);
        services.AddSingleton(GameInteropProvider);
        services.AddSingleton(DataManager);
        services.AddSingleton(TextureProvider);
    }

    [PluginService] private IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService] private IFramework Framework { get; set; } = null!;

    [PluginService] private ICommandManager CommandManager { get; set; } = null!;

    public static IPluginLog Log { get; private set; } = null!;
    
    [PluginService] private IPluginLog PrivLog { get; set; } = null!;
    [PluginService] private IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private IClientState ClientState { get; set; } = null!;
    [PluginService] private ISigScanner SigScanner { get; set; } = null!;
    [PluginService] private IGameInteropProvider GameInteropProvider { get; set; } = null!;
    [PluginService] private ITextureProvider TextureProvider { get; set; } = null!;
    [PluginService] private IDataManager DataManager { get; set; } = null!;
}
