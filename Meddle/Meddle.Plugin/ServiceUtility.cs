using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.UI.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin;

public static class ServiceUtility
{
    public static IServiceCollection AddUi(this IServiceCollection services)
    {
        var tabTypes = Assembly.GetExecutingAssembly().DefinedTypes
                               .Where(t => t.ImplementedInterfaces.Contains(typeof(ITab)))
                               .ToList();
        
        foreach (var tabType in tabTypes)
        {
            services.AddSingleton(typeof(ITab), tabType);
        }

        return services
               .AddSingleton<MainWindow>()
               .AddSingleton<DebugWindow>()
               .AddSingleton<LayoutWindow>()
               .AddSingleton<UpdateWindow>()
               .AddSingleton<MdlMaterialWindowManager>()
               .AddSingleton(new WindowSystem("Meddle"))
               .AddHostedService<WindowManager>();
    }

    public static IServiceCollection AddServices(this IServiceCollection services, IDalamudPluginInterface pluginInterface)
    {
        var serviceTypes = Assembly.GetExecutingAssembly().GetTypes()
                                   .Where(t => t is {IsClass: true, IsAbstract: false} && typeof(IService).IsAssignableFrom(t));
        foreach (var serviceType in serviceTypes)
        {
            services.AddSingleton(serviceType);
        }
        
        return services;
    }
}
