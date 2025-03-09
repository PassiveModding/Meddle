using System.ComponentModel;
using System.Numerics;
using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.IoC;
using Dalamud.Plugin;
//using InteropGenerator.Runtime;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            // FFXIVClientStructs.Interop.Generated.Addresses.Register();
            // Resolver.GetInstance.Setup();
            // Resolver.GetInstance.Resolve();
            
            var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            pluginInterface.Inject(config);
            config.Migrate();
            
            Alloc.Init();

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
            });

            app = host.Build();
            log = app.Services.GetRequiredService<ILogger<Plugin>>();
            Logger = log;
            Meddle.Utils.Global.Logger = log;
            NativeDll.Initialize(app.Services.GetRequiredService<IDalamudPluginInterface>().AssemblyLocation.DirectoryName);

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
        Alloc.Dispose();
    }
}

public class Configuration : IPluginConfiguration
{
    public const ExportType DefaultExportType = ExportType.GLTF;

    public void Migrate()
    {
        if (Version == 1)
        {
            Plugin.Logger?.LogInformation("Migrating configuration from version 1 to 2");
            if (DisableAutomaticUiHide == false)
            {
                DisableAutomaticUiHide = true;
            }
            
            if (DisableCutsceneUiHide == false)
            {
                DisableCutsceneUiHide = true;
            }
            
            Version = 2;
            Save();
        }
    }
    
    [PluginService]
    [JsonIgnore]
    private IDalamudPluginInterface PluginInterface { get; set; } = null!;

    public bool OpenDebugMenuOnLoad { get; set; }
    public LogLevel MinimumNotificationLogLevel { get; set; } = LogLevel.Warning;
    public bool OpenOnLoad { get; set; }
    public bool DisableUserUiHide { get; set; }
    public bool DisableAutomaticUiHide { get; set; }
    public bool DisableCutsceneUiHide { get; set; } = true;
    public bool DisableGposeUiHide { get; set; } = true;
    public float WorldCutoffDistance { get; set; } = 100;
    public Vector4 WorldDotColor { get; set; } = new(1f, 1f, 1f, 0.5f);
    
    public string ExportDirectory { get; set; } = Plugin.TempDirectory;
    
    /// <summary>
    /// Used to hide names in the UI
    /// </summary>
    public string PlayerNameOverride { get; set; } = string.Empty;
    
    /// <summary>
    /// If enabled, pose will be included at 0 on the timeline under the 'pose' track.
    /// </summary>
    [Obsolete("Use ExportConfig.ExportPose")]
    public bool IncludePose { get; set; } = true;

    [Obsolete("Use ExportConfig.TextureMode")]
    public TextureMode TextureMode { get; set; } = TextureMode.Bake;
    
    /// <summary>
    /// GLTF = GLTF JSON
    /// GLB = GLTF Binary
    /// OBJ = Wavefront OBJ
    /// </summary>
    [Obsolete("Use ExportConfig.ExportType")]
    public ExportType ExportType { get; set; } = DefaultExportType;
    
    public int Version { get; set; } = 2;
    
    public LayoutWindow.LayoutConfig LayoutConfig { get; set; } = new();
    public ExportConfiguration ExportConfig { get; set; } = new();
    
    public class ExportConfiguration
    {
        public CacheFileType CacheFileTypes { get; set; }
        public ExportType ExportType { get; set; } = DefaultExportType;
        public TextureMode TextureMode { get; set; } = TextureMode.Raw;
        public bool ExportPose { get; set; } = true;
        public bool RemoveAttributeDisabledSubmeshes { get; set; } = true;

        // public enum ExportRootAttachHandling
        // {
        //     PlayerAsAttachChild,
        //     Exclude,
        // }
        
        // public ExportRootAttachHandling RootAttachHandling { get; set; } = ExportRootAttachHandling.Exclude;
        
        public ExportConfiguration Clone()
        {
            return new ExportConfiguration
            {
                CacheFileTypes = CacheFileTypes,
                ExportPose = ExportPose,
                ExportType = ExportType,
                TextureMode = TextureMode,
                RemoveAttributeDisabledSubmeshes = RemoveAttributeDisabledSubmeshes,
                // RootAttachHandling = RootAttachHandling
            };
        }
    }

    public event Action? OnConfigurationSaved;

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
        OnConfigurationSaved?.Invoke();
    }
}
