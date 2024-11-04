using System.Diagnostics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public class OptionsTab : ITab
{
    public MenuType MenuType => MenuType.Default;
    private readonly Configuration config;

    public OptionsTab(Configuration config)
    {
        this.config = config;
    }

    public void Dispose() { }

    public string Name => "Options";
    public int Order => (int) WindowOrder.Options;

    public void Draw()
    {
        var exportDirectory = config.ExportDirectory;
        if (ImGui.InputText("Default Export Directory", ref exportDirectory, 256))
        {
            config.ExportDirectory = exportDirectory;
            config.Save();
        }
        
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.Redo.ToIconString()))
            {
                config.ExportDirectory = Plugin.TempDirectory;
                config.Save();
            }
        }
        
        if (ImGui.Button("Open output folder"))
        {
            Process.Start("explorer.exe", config.ExportDirectory);
        }
        
        var openOnLoad = config.OpenOnLoad;
        if (ImGui.Checkbox("Open Main Window on load", ref openOnLoad))
        {
            config.OpenOnLoad = openOnLoad;
            config.Save();
        }

        var debug = config.OpenDebugMenuOnLoad;
        if (ImGui.Checkbox("Open Debug Window on Load", ref debug))
        {
            config.OpenDebugMenuOnLoad = debug;
            config.Save();
        }

        // var test = config.OpenLayoutMenuOnLoad;
        // if (ImGui.Checkbox("Open Layout Window on Load", ref test))
        // {
        //     config.OpenLayoutMenuOnLoad = test;
        //     config.Save();
        // }
        
        ImGui.Separator();

        DrawExportType();

        var includePose = config.IncludePose;
        if (ImGui.Checkbox("Include Pose", ref includePose))
        {
            config.IncludePose = includePose;
            config.Save();
        }
        
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.Text(FontAwesomeIcon.QuestionCircle.ToIconString());
        }
        
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Includes pose as a track on exported models with frame 0 as the currently applied pose.");
            ImGui.EndTooltip();
        }
        
        // DrawPoseMode();
        
        DrawCharacterTextureMode();
        
        ImGui.Separator();
        
        var playerNameOverride = config.PlayerNameOverride;
        if (ImGui.InputText("Player Name Override", ref playerNameOverride, 64))
        {
            config.PlayerNameOverride = playerNameOverride;
            config.Save();
        }
        
        ImGui.Separator();
        
        var minimumNotificationLogLevel = config.MinimumNotificationLogLevel;
        if (ImGui.BeginCombo("Minimum Notification Log Level", minimumNotificationLogLevel.ToString()))
        {
            foreach (var level in (LogLevel[])Enum.GetValues(typeof(LogLevel)))
            {
                if (ImGui.Selectable(level.ToString(), level == minimumNotificationLogLevel))
                {
                    config.MinimumNotificationLogLevel = level;
                    config.Save();
                }
            }

            ImGui.EndCombo();
        }

        var disableUserUiHide = config.DisableUserUiHide;
        if (ImGui.Checkbox("Disable User UI Hide", ref disableUserUiHide))
        {
            config.DisableUserUiHide = disableUserUiHide;
            config.Save();
        }

        var disableAutomaticUiHide = config.DisableAutomaticUiHide;
        if (ImGui.Checkbox("Disable Automatic UI Hide", ref disableAutomaticUiHide))
        {
            config.DisableAutomaticUiHide = disableAutomaticUiHide;
            config.Save();
        }

        var disableCutsceneUiHide = config.DisableCutsceneUiHide;
        if (ImGui.Checkbox("Disable Cutscene UI Hide", ref disableCutsceneUiHide))
        {
            config.DisableCutsceneUiHide = disableCutsceneUiHide;
            config.Save();
        }

        var disableGposeUiHide = config.DisableGposeUiHide;
        if (ImGui.Checkbox("Disable Gpose UI Hide", ref disableGposeUiHide))
        {
            config.DisableGposeUiHide = disableGposeUiHide;
            config.Save();
        }
    }

    // private void DrawPoseMode()
    // {
    //     var poseMode = config.PoseMode;
    //     if (ImGui.BeginCombo("Pose Mode", poseMode.ToString()))
    //     {
    //         foreach (var mode in Enum.GetValues<SkeletonUtils.PoseMode>())
    //         {
    //             if (ImGui.Selectable(mode.ToString(), mode == poseMode))
    //             {
    //                 config.PoseMode = mode;
    //                 config.Save();
    //             }
    //         }
    //
    //         ImGui.EndCombo();
    //     }
    //     
    //     if (!Enum.IsDefined(typeof(SkeletonUtils.PoseMode), config.PoseMode))
    //     {
    //         config.PoseMode = Configuration.DefaultPoseMode;
    //         config.Save();
    //     }
    // }
    
    private void DrawCharacterTextureMode()
    {
        var characterTexture = config.TextureMode;
        if (ImGui.BeginCombo("Character Texture Mode", characterTexture.ToString()))
        {
            foreach (var mode in Enum.GetValues<TextureMode>())
            {
                if (ImGui.Selectable(mode.ToString(), mode == characterTexture))
                {
                    config.TextureMode = mode;
                    config.Save();
                }
            }

            ImGui.EndCombo();
        }
        
        if (!Enum.IsDefined(typeof(TextureMode), config.TextureMode))
        {
            config.TextureMode = TextureMode.Bake;
            config.Save();
        }
    }
    
    private void DrawExportType()
    {
        var exportType = config.ExportType;
        if (ImGui.BeginCombo("Export Type", exportType.ToString()))
        {
            foreach (var type in Enum.GetValues<ExportType>())
            {
                var selected = exportType.HasFlag(type);
                using var style = ImRaii.PushStyle(ImGuiStyleVar.Alpha, selected ? 1 : 0.5f);
                if (ImGui.Selectable(type.ToString(), selected, ImGuiSelectableFlags.DontClosePopups))
                {
                    if (selected)
                    {
                        exportType &= ~type;
                    }
                    else
                    {
                        exportType |= type;
                    }
                }
            }

            ImGui.EndCombo();
        }
        
        if (exportType == 0)
        {
            exportType = Configuration.DefaultExportType;
        }
        
        if (exportType != config.ExportType)
        {
            config.ExportType = exportType;
            config.Save();
        }
    }
}
