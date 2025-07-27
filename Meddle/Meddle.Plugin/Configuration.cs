using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.IoC;
using Dalamud.Plugin;
using Meddle.Plugin.Models;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.UI.Windows;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin;

public partial class Configuration : IPluginConfiguration
{
    public const ExportType DefaultExportType = ExportType.GLTF;
    
    [PluginService]
    [JsonIgnore]
    private IDalamudPluginInterface PluginInterface { get; set; } = null!;

    public int Version { get; set; } = 4;
    
    public bool OpenDebugMenuOnLoad { get; set; }
    public LogLevel MinimumNotificationLogLevel { get; set; } = LogLevel.Warning;
    public bool OpenOnLoad { get; set; }
    public bool DisableUserUiHide { get; set; }
    public bool DisableAutomaticUiHide { get; set; }
    public bool DisableCutsceneUiHide { get; set; } = true;
    public bool DisableGposeUiHide { get; set; } = true;
    public string ExportDirectory { get; set; } = Plugin.DefaultExportDirectory;
    public string SecretConfig { get; set; } = string.Empty;
    
    /// <summary>
    /// Used to hide names in the UI
    /// </summary>
    public string PlayerNameOverride { get; set; } = string.Empty;

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
