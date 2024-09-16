using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Plugin.Models;

namespace Meddle.Plugin.UI;

public class AboutTab : ITab
{
    public void Dispose() { }

    public string Name => "About";
    public MenuType MenuType => MenuType.Default;
    public int Order => int.MaxValue;

    public void Draw()
    {
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(); 
        ImGui.Text($"Version: {assemblyVersion}");
        
        ImGui.TextWrapped("Meddle is a tool that allows you to export models and animations directly from the game.");
        ImGui.TextWrapped("It is still in development and may not work as expected.");
        ImGui.TextWrapped("Please report any issues on the GitHub repository.");

        Vector2 mainButtonSize = new(150, 0);
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 1f, 1)))
        {
            if (ImGui.Button("GitHub", mainButtonSize))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/PassiveModding/Meddle/", UseShellExecute = true
                });
            }
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(1f, 0.25f, 0.25f, 1)))
        {
            if (ImGui.Button("Report an Issue", mainButtonSize))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/PassiveModding/Meddle/issues", UseShellExecute = true
                });
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Please be sure to include any relevant logs from /xllog in your report.");
            }
        }

        if (ImGui.CollapsingHeader("How does meddle read this data?"))
        {
            ImGui.TextWrapped("Meddle uses a combination of the following:");
            ImGui.BulletText("Reading the sqpack files packaged with the game.");
            ImGui.BulletText("Reading on-disk files from paths that are overwritten by plugins like Penumbra.");
            ImGui.BulletText("Reading the game's memory to retrieve paths and other information about the game-state.");
            ImGui.BulletText("Reading data directly from the GPU to retrieve texture information.");
        }

        ImGui.Text("Credits");
        const int nameWidth = 130;
        Vector2 buttonSize = new(130, 0);
        using (ImRaii.Table("Credits", 2, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, nameWidth);
            ImGui.TableSetupColumn("Contribution", ImGuiTableColumnFlags.None);
            ImGui.TableHeadersRow();

            foreach (var (name, role, link) in UserCredits)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                if (ImGui.Button(name, buttonSize))
                {
                    Process.Start(new ProcessStartInfo {FileName = link, UseShellExecute = true});
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(role);
            }
        }

        ImGui.TextWrapped("Special Thanks to the following projects and their respective developers:");
        using (ImRaii.Table("Project Credits", 2, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Project", ImGuiTableColumnFlags.WidthFixed, nameWidth);
            ImGui.TableSetupColumn("Usage", ImGuiTableColumnFlags.None);
            ImGui.TableHeadersRow();

            foreach (var (name, role, link) in ProjectCredits)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                if (ImGui.Button(name, buttonSize))
                {
                    Process.Start(new ProcessStartInfo {FileName = link, UseShellExecute = true});
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(role);
            }
        }
    }

    private static List<(string, string, string)> UserCredits =>
    [
        ("PassiveModding", "Developer", "https://github.com/PassiveModding/Meddle"),
        ("Asriel", "GPU/DX11 data handling, shape and attribute logic, attach work, skeleton traversal",
            "https://github.com/WorkingRobot")
    ];

    private static List<(string, string, string)> ProjectCredits =>
    [
        ("Xande", "Base for the plugin, PBD file structure, meshbuilder, racedeformer, havok research",
            "https://github.com/xivdev/Xande"),

        ("Penumbra", "Shader logic, vertex info, spanbinaryreader impl.", "https://github.com/xivdev/Penumbra"),
        ("Lumina", "File structures", "https://github.com/NotAdam/Lumina/"),
        ("Pathfinder", "World overlay reference design.", "https://github.com/chirpxiv/ffxiv-pathfinder")
    ];
}
