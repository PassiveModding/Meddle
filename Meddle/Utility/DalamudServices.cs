using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin.Utility;

public class DalamudServices {
    public DalamudServices(DalamudPluginInterface pluginInterface) {
        pluginInterface.Inject(this);
    }
    
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton(PluginInterface);
        services.AddSingleton(Framework);
        services.AddSingleton(CommandManager);
        services.AddSingleton(Log);
        services.AddSingleton(ObjectTable);
        services.AddSingleton(GameData);
    }
    
    [PluginService] private DalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService] private IFramework Framework { get; set; } = null!;

    [PluginService] private ICommandManager CommandManager { get; set; } = null!;

    [PluginService] private IPluginLog Log { get; set; } = null!;
    
    [PluginService] private IDataManager GameData { get; set; } = null!;
    [PluginService] private IObjectTable ObjectTable { get; set; } = null!;
}