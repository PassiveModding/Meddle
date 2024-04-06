using ImGuiNET;
using Meddle.Plugin.Models.Config;

namespace Meddle.Plugin.UI;

public sealed class ConfigTab : ITab
{
    private readonly Configuration config;

    public ConfigTab(Configuration configuration)
    {
        config = configuration;
    }

    public string Name => "Config";

    public int Order => int.MaxValue;

    public void Draw()
    {
        var autoOpen = config.AutoOpen;
        if (ImGui.Checkbox("Auto-open", ref autoOpen))
        {
            config.AutoOpen = autoOpen;
            config.Save();
        }

        var parallelBuild = config.ParallelBuild;
        if (ImGui.Checkbox("Parallel build", ref parallelBuild))
        {
            config.ParallelBuild = parallelBuild;
            config.Save();
        }

        var disableCutsceneUiHide = config.DisableCutsceneUiHide;
        if (ImGui.Checkbox("Show UI during cutscenes", ref disableCutsceneUiHide))
        {
            config.DisableCutsceneUiHide = disableCutsceneUiHide;
            config.Save();
        }

        var disableGposeUiHide = config.DisableGposeUiHide;
        if (ImGui.Checkbox("Show UI during gpose", ref disableGposeUiHide))
        {
            config.DisableGposeUiHide = disableGposeUiHide;
            config.Save();
        }

        var openFolderAfterExport = config.OpenFolderAfterExport;
        if (ImGui.Checkbox("Open folder after export", ref openFolderAfterExport))
        {
            config.OpenFolderAfterExport = openFolderAfterExport;
            config.Save();
        }

        var includeReaperEyeInExport = config.IncludeReaperEyeInExport;
        if (ImGui.Checkbox("Include Reaper eye in export", ref includeReaperEyeInExport))
        {
            config.IncludeReaperEyeInExport = includeReaperEyeInExport;
            config.Save();
        }
    }

    public void Dispose() { }
}
