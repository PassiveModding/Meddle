using Dalamud.Configuration;
using Dalamud.Plugin;
using Meddle.Plugin.Enums;

namespace Meddle.Plugin.Models.Config;

[Serializable]
public class Configuration : IPluginConfiguration
{
    private DalamudPluginInterface PluginInterface { get; set; } = null!;

    public int Version { get; set; }

    public bool AutoOpen { get; set; }
    public bool ParallelBuild { get; set; }
    public bool DisableCutsceneUiHide { get; set; }
    public bool DisableGposeUiHide { get; set; } = true;
    public bool OpenFolderAfterExport { get; set; } = true;
    public bool IncludeReaperEyeInExport { get; set; }

    public void Initialize(DalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
        OnChange?.Invoke();
    }
    
    public ExportConfig GetExportConfig() => new()
    {
        OpenFolderWhenComplete = OpenFolderAfterExport,
        IncludeReaperEye = IncludeReaperEyeInExport,
        ParallelBuild = ParallelBuild,
        ExportType = ExportType.Gltf
    };

    public event Action? OnChange;
}
