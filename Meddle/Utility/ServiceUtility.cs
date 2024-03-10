using System.Reflection;
using Dalamud.Plugin;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Meddle.Plugin.Utility;

public static class ServiceUtility
{
    public static IServiceCollection AddDalamud(this IServiceCollection services, DalamudPluginInterface pi)
    {
        var dalamud = new DalamudServices(pi);
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
        {
            services.AddSingleton(typeof(ITab), tab);
        }
        
        return services
            .AddSingleton<MainWindow>();
    }

    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        return services
            .AddSingleton<ExportService>()
            .AddSingleton<FileService>()
            .AddSingleton<SklbService>()
            .AddSingleton<ImageService>()
            .AddSingleton<ModelService>()
            .AddSingleton<MaterialService>()
            .AddSingleton<StainService>();
    }
}
