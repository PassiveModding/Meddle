using System.Reflection;
using Dalamud.Plugin;
using Meddle.Plugin.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin;

public static class ServiceUtility
{
    public static IServiceCollection AddDalamud(this IServiceCollection services, IDalamudPluginInterface pi)
    {
        var dalamud = new Service(pi);
        dalamud.AddServices(services);
        return services;
    }

    public static IServiceCollection AddUi(this IServiceCollection services)
    {
        // reflection get all tab types
        var tabTypes = Assembly.GetExecutingAssembly().DefinedTypes
            .Where(t => t.ImplementedInterfaces.Contains(typeof(ITab)))
            .ToList();

        foreach (var tab in tabTypes)
            services.AddSingleton(typeof(ITab), tab);

        return services
            .AddSingleton<MainWindow>();
    }
}
