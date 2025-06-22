using System.Numerics;
using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.UI.Windows;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OtterTex;

namespace Meddle.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    public static readonly string DefaultExportDirectory = Path.Combine(Path.GetTempPath(), "Meddle.Export");
    private readonly IHost? app;
    private readonly ILogger pluginLog;
    public static ILogger<Plugin>? Logger;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        var service = new Service();
        pluginInterface.Inject(service);
        
        var dLogger = service.GetLog() ?? throw new InvalidOperationException("Service log is null");
        pluginLog = new PluginSerilogWrapper(dLogger.Logger);
        pluginLog.LogDebug("Meddle Plugin initializing...");
        Meddle.Utils.Global.Logger = pluginLog;
        
        try
        {
#if HAS_LOCAL_CS
            FFXIVClientStructs.Interop.Generated.Addresses.Register();
            InteropGenerator.Runtime.Resolver.GetInstance.Setup();
            InteropGenerator.Runtime.Resolver.GetInstance.Resolve();
#endif
            
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
                service.RegisterServices(services);
                services
                    .AddServices(pluginInterface)    
                    .AddSingleton(config)
                    .AddUi()
                    .AddSingleton(new SqPack(Environment.CurrentDirectory));
            });

            app = host.Build();
            Logger = app.Services.GetRequiredService<ILogger<Plugin>>();
            Meddle.Utils.Global.Logger = app.Services.GetRequiredService<ILogger<Meddle.Utils.Global>>();
            NativeDll.Initialize(app.Services.GetRequiredService<IDalamudPluginInterface>().AssemblyLocation.DirectoryName);

            app.Start();
        }
        catch (Exception e)
        {
            pluginLog.LogError(e, "Failed to initialize plugin");
            Dispose();
        }
    }

    public void Dispose()
    {
        app?.StopAsync();
        app?.WaitForShutdown();
        app?.Dispose();
        pluginLog.LogDebug("Plugin disposed");
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
        
        if (Version == 2)
        {
            Plugin.Logger?.LogInformation("Migrating configuration from version 2 to 3");
            
            LayoutConfig.DrawTypes = LayoutWindow.DefaultDrawTypes;
            
            Version = 3;
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
    
    public string ExportDirectory { get; set; } = Plugin.DefaultExportDirectory;
    
    /// <summary>
    /// Used to hide names in the UI
    /// </summary>
    public string PlayerNameOverride { get; set; } = string.Empty;
    
    /// <summary>
    /// If enabled, pose will be included at 0 on the timeline under the 'pose' track.
    /// </summary>
    [Obsolete("Use ExportConfig.ExportPose", true)]
    public bool IncludePose { get; set; } = true;

    [Obsolete("Use ExportConfig.TextureMode", true)]
    public TextureMode TextureMode { get; set; } = TextureMode.Bake;
    
    /// <summary>
    /// GLTF = GLTF JSON
    /// GLB = GLTF Binary
    /// OBJ = Wavefront OBJ
    /// </summary>
    [Obsolete("Use ExportConfig.ExportType", true)]
    public ExportType ExportType { get; set; } = DefaultExportType;
    
    public int Version { get; set; } = 3;
    
    public LayoutWindow.LayoutConfig LayoutConfig { get; set; } = new();
    public ExportConfiguration ExportConfig { get; set; } = new();
    public UpdateWindow.UpdateConfig UpdateConfig { get; set; } = new();
    
    public class ExportConfiguration
    {
        public CacheFileType CacheFileTypes { get; set; }
        public ExportType ExportType { get; set; } = DefaultExportType;
        
        [Obsolete("TextureMode is only ever Raw as baking is no longer supported", true)]
        public TextureMode TextureMode { get; set; } = TextureMode.Raw;
        public SkeletonUtils.PoseMode PoseMode { get; set; } = SkeletonUtils.PoseMode.Local;
        
        [Obsolete("Use PoseMode instead", true)]
        public bool ExportPose { get; set; } = true;
        public bool RemoveAttributeDisabledSubmeshes { get; set; } = true;
        public bool SkipHiddenBgParts { get; set; }
        public bool UseDeformer { get; set; } = true;
        
        public bool LimitTerrainExportRange { get; set; }
        public float TerrainExportDistance { get; set; } = 500f;

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
                ExportType = ExportType,
                // ExportPose = ExportPose,
                // TextureMode = TextureMode,
                PoseMode = PoseMode,
                RemoveAttributeDisabledSubmeshes = RemoveAttributeDisabledSubmeshes,
                SkipHiddenBgParts = SkipHiddenBgParts,
                // RootAttachHandling = RootAttachHandling
                UseDeformer = UseDeformer,
                LimitTerrainExportRange = LimitTerrainExportRange,
                TerrainExportDistance = TerrainExportDistance
            };
        }

        public void SetDefaultCloneOptions()
        {
            RemoveAttributeDisabledSubmeshes = true;
            SkipHiddenBgParts = true;
            UseDeformer = true;
        }
        
        public void Apply(ExportConfiguration other)
        {
            CacheFileTypes = other.CacheFileTypes;
            ExportType = other.ExportType;
            // ExportPose = other.ExportPose;
            // TextureMode = other.TextureMode;
            PoseMode = other.PoseMode;
            RemoveAttributeDisabledSubmeshes = other.RemoveAttributeDisabledSubmeshes;
            SkipHiddenBgParts = other.SkipHiddenBgParts;
            // RootAttachHandling = other.RootAttachHandling;
            UseDeformer = other.UseDeformer;
            LimitTerrainExportRange = other.LimitTerrainExportRange;
            TerrainExportDistance = other.TerrainExportDistance;
        }
    }

    public event Action? OnConfigurationSaved;

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
        OnConfigurationSaved?.Invoke();
    }
}
