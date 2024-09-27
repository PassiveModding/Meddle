using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.IoC;
using Dalamud.Plugin;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OtterTex;

namespace Meddle.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    public static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "Meddle.Export");
    private readonly IHost? app;
    private readonly ILogger<Plugin>? log;
    public static ILogger<Plugin>? Logger;

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
                        .WithTracing(x =>
                        {
                            x.AddSource("Meddle.*");
                        })
                        .WithMetrics(x =>
                        {
                            x.AddProcessInstrumentation();
                            x.AddRuntimeInstrumentation();
                            x.AddMeter("Meddle.*");
                        })
                        .WithLogging()
                        .UseOtlpExporter();
#endif
            });

            app = host.Build();
            log = app.Services.GetRequiredService<ILogger<Plugin>>();
            Logger = log;
            Meddle.Utils.Global.Logger = log;
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
    public const ExportType DefaultExportType = ExportType.GLTF;
    public const SkeletonUtils.PoseMode DefaultPoseMode = SkeletonUtils.PoseMode.Model;
    
    [PluginService]
    [JsonIgnore]
    private IDalamudPluginInterface PluginInterface { get; set; } = null!;

    public bool OpenDebugMenuOnLoad { get; set; }
    public bool OpenLayoutMenuOnLoad { get; set; }
    public LogLevel MinimumNotificationLogLevel { get; set; } = LogLevel.Warning;
    public bool OpenOnLoad { get; set; }
    public bool DisableUserUiHide { get; set; }
    public bool DisableAutomaticUiHide { get; set; }
    public bool DisableCutsceneUiHide { get; set; } = true;
    public bool DisableGposeUiHide { get; set; } = true;
    public float WorldCutoffDistance { get; set; } = 100;
    public Vector4 WorldDotColor { get; set; } = new(1f, 1f, 1f, 0.5f);
    
    /// <summary>
    /// Used to hide names in the UI
    /// </summary>
    public string PlayerNameOverride { get; set; } = string.Empty;
    
    /// <summary>
    /// If enabled, pose will be included at 0 on the timeline under the 'pose' track.
    /// </summary>
    public bool IncludePose { get; set; } = true;
    
    /// <summary>
    /// Indicates whether scaling should be taken from the model pose rather than the local pose.
    /// </summary>
    public SkeletonUtils.PoseMode PoseMode { get; set; } = DefaultPoseMode;
    
    /// <summary>
    /// GLTF = GLTF JSON
    /// GLB = GLTF Binary
    /// OBJ = Wavefront OBJ
    /// </summary>
    public ExportType ExportType { get; set; } = DefaultExportType;
    
    public int Version { get; set; } = 1;
    
    public LayoutWindow.LayoutConfig LayoutConfig { get; set; } = new();

    public event Action? OnConfigurationSaved;

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
        OnConfigurationSaved?.Invoke();
    }
}
