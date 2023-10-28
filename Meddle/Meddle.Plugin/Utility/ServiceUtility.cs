using Dalamud.Plugin;
using Meddle.Plugin.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin.Utility;

public static class ServiceUtility
{
    public static IServiceCollection AddDalamud(this IServiceCollection services, DalamudPluginInterface pi)
    {
        var dalamud = new Service(pi);
        dalamud.AddServices(services);
        return services;
    }
    
    public static IServiceCollection AddUi(this IServiceCollection services)
    {
        return services
            .AddSingleton<ITab, ResourceTab>()
            .AddSingleton<ITab, ConfigTab>()
            .AddSingleton<MainWindow>();
    }
}
