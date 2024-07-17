using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Meddle.UI.Util;
using Meddle.Utils.Files;
using Meddle.Utils.Skeletons.Havok;
using Meddle.Utils.Skeletons.Havok.Models;

namespace Meddle.UI.Windows.Views;

public class SklbView : IView
{
    private readonly SklbFile file;
    private readonly HexView hexView;
    private readonly Configuration configuration;

    public SklbView(SklbFile file, Configuration configuration)
    {
        this.file = file;
        this.hexView = new(file.RawData);
        this.configuration = configuration;
    }
    
    private (string, HavokSkeleton)? parseResult;
    public void Draw()
    {
        ImGui.Text($"Version: {file.Header.Version} [{(uint)file.Header.Version:X8}]");
        ImGui.Text($"Old Header: {file.Header.OldHeader}");
        
        if (ImGui.Button("Parse"))
        {
            parseResult = SkeletonUtil.ProcessHavokInput(file.Skeleton.ToArray());
        }

        if (ImGui.CollapsingHeader("Havok XML") && parseResult != null)
        {
            ImGui.TextUnformatted(parseResult.Value.Item1);
        }
        
        if (ImGui.CollapsingHeader("Parsed XML") && parseResult != null)
        {
            ImGui.SeparatorText("Skeletons");
            for (var i = 0; i < parseResult.Value.Item2.Skeletons.Length; i++)
            {
                var skeleton = parseResult.Value.Item2.Skeletons[i];
                ImGui.BulletText($"Bone Count: {skeleton.BoneNames.Length}");
                // scroll box
                ImGui.BeginChild($"Skeleton {i}", new Vector2(0, 200), ImGuiChildFlags.Border);
                for (var j = 0; j < skeleton.BoneNames.Length; j++)
                {
                    ImGui.Text($"Bone {j}");
                    ImGui.BulletText($"Name: {skeleton.BoneNames[j]}");
                    ImGui.BulletText($"Parent Index: {skeleton.ParentIndices[j]}");
                    ImGui.BulletText($"Reference Pose: {string.Join(", ", skeleton.ReferencePose[j])}");
                }
                ImGui.EndChild();
                
            }
            
            ImGui.SeparatorText("Mappings");
            for (var i = 0; i < parseResult.Value.Item2.Mappings.Length; i++)
            {
                var mapping = parseResult.Value.Item2.Mappings[i];
                ImGui.Text($"Mapping {i}");
                ImGui.BulletText($"Id: {mapping.Id}");
                ImGui.BulletText($"Bone Mappings: {mapping.BoneMappings.Length}");
                ImGui.BulletText($"Skeleton A: {mapping.SkeletonA}");
                ImGui.BulletText($"Skeleton B: {mapping.SkeletonB}");
            }
        }
        
        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
    }
}
