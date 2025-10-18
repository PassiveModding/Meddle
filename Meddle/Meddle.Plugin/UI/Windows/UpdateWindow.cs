using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Meddle.Plugin.UI.Windows;

public class UpdateWindow : Window
{
    private readonly Configuration config;
    public UpdateWindow(Configuration config) : base("Meddle Updates")
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(800, 600)
        };
    }

    public static readonly List<UpdateLog> UpdateLogs =
    [
        new()
        {
            Tag = "Camera Exports Support",
            Date = "2025-06-05",
            Changes =
            [
                new TextUpdateLine(" + Added support for exporting cameras from the layout tab."),
                new TextUpdateLine(" + Added this update window (Can be disabled from Options)."),
                new TextUpdateLine(" + Fix obj export failing in come circumstances."),
                new TextUpdateLine(" + Fix name override being ignored in some cases."),
                new TextUpdateLine(" + Improve naming of Animation exports to include track names."),
                new TextUpdateLine(" + Added a warning message in options if the export directory is set to the temp directory.")
            ]
        },
        new()
        {
            Tag = "Decal Support, MultiTrack recording and Housing Improvements",
            Date = "2025-07-27",
            Changes =
            [
                new TextUpdateLine(" + Added support for exporting decal textures for characters"),
                new TextUpdateLine(" + Added support for parsing and exporting world decal info"),
                new TextUpdateLine(" + Added support for multi-actor recording in the animation tab"),
                new TextUpdateLine(" + Added option to specify keyframe interval for animation recording"),
                new TextUpdateLine(" + Improved animation export file names to include actor names"),
                new TextUpdateLine(" + Added support for exporting environment ambient, sun and moon lighting from layout tab"),
                new TextUpdateLine(" + Added option to include shared groups when only a subset of the group is within range in the layout tab"),
                new TextUpdateLine(" + Added support for many missed texture types which are less frequently used but still supported by the game"),
                new TextUpdateLine(" + Fixed issues with staining on certain housing items"),
                new TextUpdateLine(" + Added support for BGChange objects (housing wall and floor customization) exports"),
                new TextUpdateLine(" + Exported housing objects should now be named after their in-game names instead of their paths"),
            ]
        },
        new()
        {
            Tag = "Patch 7.3 Support",
            Date = "2025-08-08",
            Changes =
            [
                new TextUpdateLine(" + Support for patch 7.3 changes."),
                new TextUpdateLine(" + Fixes for offscreen characters not being recorded in the animation tab."),
                new TextUpdateLine(" + Fixes decal parsing during zone changes."),
                new TextUpdateLine(" + Updated vertex handling for better support of multiple usage indexes."),
                new TextUpdateLine(" + Improved progress drawing for layout exports."),
                new TextUpdateLine(" + Improved responsiveness of cancel button during exports."),
                new TextUpdateLine(" + Added option to disable automatic opening of folder after export."),
                new TextUpdateLine(" + Clean up info in character selector to avoid clutter. (This can be re-enabled in options with the Debug Info option.)"),
                new TextUpdateLine(" + Added option for relative position export in the animation tab"),
            ]
        },
        new()
        {
            Tag = "Demihuman Fixes and Housing Improvements",
            Date = "2025-09-17",
            Changes =
            [
                new TextUpdateLine(" + Fix attached ornaments being exported as duplicates from the layout view."),
                new TextUpdateLine(" + Added support for dyed housing exteriors."),
                new TextUpdateLine(" + Added fixes for demihuman exports including invalid values for customizations."),
                new TextUpdateLine(" + Added proper instancing for bg meshes."),
                new TextUpdateLine(" + Fix deformations being applied to demihumans."),
            ]
        },
        new()
        {
            Tag = "CRC Cracking and Misc Improvements",
            Date = "2025-10-18",
            Changes =
            [
                new WarningUpdateLine(" ! WARNING: Please update MeddleTools Blender Addon to the latest version to ensure compatibility with these changes."),
                new TextUpdateLine(" + Cracked over a dozen more CRCs (these translate material and shader key names for exports from their hashed values)."),
                new TextUpdateLine(" + Fix material util not opening in come cases."),
                new TextUpdateLine(" + Add buttons to swap known key values for material util."),
                new TextUpdateLine(" + Fix exports failing when characters were named with invalid path characters."),
                new TextUpdateLine(" + Fix fps drops when using character tab. (Caused by resolving skin shader slot values.)"),
                new TextUpdateLine(" + Fix selecting moving characters from dropdowns not working sometimes."),
            ]
        },
    ];
    
    public class UpdateLog
    {
        public string Tag { get; init; } = string.Empty;
        public string Date { get; init; } = string.Empty;
        public IUpdateLine[] Changes { get; init; } = [];
    }

    public interface IUpdateLine
    {
        public void Draw();
    }

    public record TextUpdateLine(string Text) : IUpdateLine
    {
        public void Draw()
        {
            ImGui.Text(Text);
        }
    }
    
    public record WarningUpdateLine(string Text) : IUpdateLine
    {
        public void Draw()
        {
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 0.5f, 0.5f, 1f)))
            {
                ImGui.Text(Text);
            }
        }
    }

    public class UpdateConfig
    {
        public string? LastSeenUpdateTag { get; set; }
        public bool ShowUpdateWindow { get; set; } = true;
    }

    public override void OnOpen()
    {
        if (config.UpdateConfig.LastSeenUpdateTag != UpdateLogs.LastOrDefault()?.Tag)
        {
            config.UpdateConfig.LastSeenUpdateTag = UpdateLogs.LastOrDefault()?.Tag;
            config.Save();
        }
        
        base.OnOpen();
    }

    public override void Draw()
    {
        ImGui.Text("Meddle Version: " + Assembly.GetExecutingAssembly().GetName().Version);

        // option to disable this window from opening automatically
        var showUpdateWindow = config.UpdateConfig.ShowUpdateWindow;
        if (ImGui.Checkbox("Automatically open this window when there are new release notes", ref showUpdateWindow))
        {
            config.UpdateConfig.ShowUpdateWindow = showUpdateWindow;
            config.Save();
        }
        
        ImGui.Separator();
        
        Vector2 mainButtonSize = new(150, 0);
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.5f, 0.2f, 0.6f, 1)))
        {
            if (ImGui.Button("Carrd", mainButtonSize))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.CarrdUrl,
                    UseShellExecute = true
                });
            }
        }
        
        ImGui.SameLine();
        
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(1f, 0.2f, 0.19f, 1)))
        {
            if (ImGui.Button("Donate", mainButtonSize))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.KoFiUrl,
                    UseShellExecute = true
                });
            }
        }
        
        ImGui.SameLine();

        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 1f, 1)))
        {
            if (ImGui.Button("Discord", mainButtonSize))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.DiscordUrl,
                    UseShellExecute = true
                });
            }
        }
        
        ImGui.SameLine();
        
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.95f, 0.47f, 0.17f, 1)))
        {
            if (ImGui.Button("MeddleTools Blender Addon", new (200, 0)))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.MeddleToolsUrl,
                    UseShellExecute = true
                });
            }
        }

        ImGui.Separator();
        
        for (var i = UpdateLogs.Count - 1; i >= 0; i--)
        {
            var update = UpdateLogs[i];
            var flags = ImGuiTreeNodeFlags.None;
            if (i == UpdateLogs.Count - 1)
            {
                flags |= ImGuiTreeNodeFlags.DefaultOpen;
            }
            if (ImGui.CollapsingHeader($"{update.Tag} - {update.Date}", flags))
            {
                foreach (var change in update.Changes)
                {
                    ImGui.Indent();
                    change.Draw();
                    ImGui.Unindent();
                }
            }
        }
    }
}
