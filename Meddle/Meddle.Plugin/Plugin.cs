using System.Numerics;
using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.IoC;
using Dalamud.Plugin;
using Meddle.Plugin.Services;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            pluginInterface.Inject(config);

            var host = Host.CreateDefaultBuilder();
            host.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                var loggerProvider = new PluginLoggerProvider(config);
                pluginInterface.Inject(loggerProvider);
                logging.AddProvider(loggerProvider);
            });

            host.ConfigureServices(services =>
            {
                services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
                services
                    .AddServices(pluginInterface)    
                    .AddSingleton(config)
                    .AddUi()
                    .AddSingleton(new SqPack(Environment.CurrentDirectory));

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

    public bool OpenDebugMenuOnLoad { get; set; }
    public bool OpenLayoutMenuOnLoad { get; set; }
    public LogLevel MinimumNotificationLogLevel { get; set; } = LogLevel.Warning;
    public bool OpenOnLoad { get; set; }
    public bool DisableUserUiHide { get; set; }
    public bool DisableAutomaticUiHide { get; set; }
    public bool DisableCutsceneUiHide { get; set; }
    public bool DisableGposeUiHide { get; set; }
    public float WorldCutoffDistance { get; set; } = 100;
    public Vector4 WorldDotColor { get; set; } = new(1f, 1f, 1f, 0.5f);
    public string PlayerNameOverride { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public event Action? OnConfigurationSaved;

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
        OnConfigurationSaved?.Invoke();
    }
}
