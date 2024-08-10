using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Meddle.Plugin.Services;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin;

public static class ServiceUtility
{
    public static IServiceCollection AddUi(this IServiceCollection services)
    {
        // reflection get all tab types
        var tabTypes = Assembly.GetExecutingAssembly().DefinedTypes
                               .Where(t => t.ImplementedInterfaces.Contains(typeof(ITab)))
                               .ToList();

        foreach (var tab in tabTypes)
        {
            services.AddSingleton(typeof(ITab), tab);
        }

        return services
               .AddSingleton<MainWindow>()
               .AddSingleton(new WindowSystem("Meddle"))
               .AddHostedService<WindowManager>();
    }

    public static IServiceCollection AddServices(this IServiceCollection services, IDalamudPluginInterface pluginInterface)
    {
        var service = new Service();
        pluginInterface.Inject(service);
        service.RegisterServices(services);
                
        var serviceTypes = Assembly.GetExecutingAssembly().GetTypes()
                                   .Where(t => t is {IsClass: true, IsAbstract: false} && typeof(IService).IsAssignableFrom(t));
        foreach (var serviceType in serviceTypes)
        {
            services.AddSingleton(serviceType);
        }
        
        return services;
    }
}
