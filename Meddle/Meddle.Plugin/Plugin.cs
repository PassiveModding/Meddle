using Dalamud.Configuration;
using Dalamud.IoC;
using Dalamud.Plugin;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OtterTex;

namespace Meddle.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    public static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "Meddle.Export");
    private readonly IHost? app;
    private readonly ILogger<Plugin>? log;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        try
        {
            var service = new Service();
            var loggerProvider = new PluginLoggerProvider();
            pluginInterface.Inject(service);
            pluginInterface.Inject(loggerProvider);
            var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            pluginInterface.Inject(config);

            var host = Host.CreateDefaultBuilder();
            host.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(loggerProvider);
            });

            host.ConfigureServices(services =>
            {
                services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
                service.AddServices(services);
                services.AddSingleton(config)
                        .AddUi()
                        .AddSingleton(pluginInterface)
                        .AddSingleton<ExportUtil>()
                        .AddSingleton<ParseUtil>()
                        .AddSingleton<DXHelper>()
                        .AddSingleton(new SqPack(Environment.CurrentDirectory))
                        .AddSingleton<PluginState>()
                        .AddHostedService<InteropService>();

#if DEBUG
                services.AddOpenTelemetry()
                        .ConfigureResource(x => { x.AddService("Meddle"); })
                        .WithTracing(x => { x.AddSource("Meddle.*"); })
                        .WithMetrics(x =>
                        {
                            x.AddProcessInstrumentation();
                            x.AddRuntimeInstrumentation();
                        })
                        .WithLogging()
                        .UseOtlpExporter();
#endif
            });

            app = host.Build();
            log = app.Services.GetRequiredService<ILogger<Plugin>>();
            NativeDll.Initialize(app.Services.GetRequiredService<IDalamudPluginInterface>().AssemblyLocation
                                    .DirectoryName);

            app.Start();
        }
        catch (Exception e)
        {
            log?.LogError(e, "Failed to initialize plugin");
            Dispose();
        }
    }

    public void Dispose()
    {
        app?.StopAsync();
        app?.WaitForShutdown();
        app?.Dispose();
        log?.LogInformation("Plugin disposed");
    }
}

public class Configuration : IPluginConfiguration
{
    [PluginService]
    [JsonIgnore]
    private IDalamudPluginInterface PluginInterface { get; set; } = null!;

    public bool ShowAdvanced { get; set; }
    public bool OpenOnLoad { get; set; }
    public string PlayerNameOverride { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
    }
}
