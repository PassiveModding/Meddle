using System.Diagnostics;
using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace Meddle.Plugin.UI.Windows;

public class UpdateWindow : Window
{
    private readonly Configuration config;
    public UpdateWindow(Configuration config) : base("Updates", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new System.Numerics.Vector2(400, 300),
            MaximumSize = new System.Numerics.Vector2(800, 600)
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
        }
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
        ImGui.Separator();

        if (ImGui.Button("Carrd"))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Constants.CarrdUrl,
                UseShellExecute = true
            });
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Ko-fi"))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Constants.KoFiUrl,
                UseShellExecute = true
            });
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Discord"))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Constants.DiscordUrl,
                UseShellExecute = true
            });
        }
        
        ImGui.Separator();
        
        for (var i = UpdateLogs.Count - 1; i >= 0; i--)
        {
            var update = UpdateLogs[i];
            if (ImGui.CollapsingHeader($"{update.Tag} - {update.Date}", ImGuiTreeNodeFlags.DefaultOpen))
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
