using Dalamud.Configuration;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI;
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
            pluginInterface.Inject(service);
            var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            pluginInterface.Inject(config);
            var loggerProvider = new PluginLoggerProvider(config);
            pluginInterface.Inject(loggerProvider);

            var host = Host.CreateDefaultBuilder();
            host.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(loggerProvider);
            });

            host.ConfigureServices(services =>
            {
                services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
                service.AddServices(services);
                services.AddSingleton(config)
                        .AddUi()
                        .AddSingleton<TextureCache>()
                        .AddSingleton(pluginInterface)
                        .AddSingleton<ExportUtil>()
                        .AddSingleton<ParseUtil>()
                        .AddSingleton<DXHelper>()
                        .AddSingleton<PbdHooks>()
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
        log?.LogDebug("Plugin disposed");
    }
}

public class Configuration : IPluginConfiguration
{
    [PluginService]
    [JsonIgnore]
    private IDalamudPluginInterface PluginInterface { get; set; } = null!;

    public event Action? OnConfigurationSaved;
    
    public bool ShowDebug { get; set; }
    public bool ShowTesting { get; set; }
    public LogLevel MinimumNotificationLogLevel { get; set; } = LogLevel.Warning;
    public bool OpenOnLoad { get; set; }
    public bool DisableUserUiHide { get; set; }
    public bool DisableAutomaticUiHide { get; set; }
    public bool DisableCutsceneUiHide { get; set; }
    public bool DisableGposeUiHide { get; set; }
    public string PlayerNameOverride { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
        OnConfigurationSaved?.Invoke();
    }
}
