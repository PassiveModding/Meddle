using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Meddle.UI.Util;
using Meddle.Utils.Files;
using Meddle.Utils.Skeletons.Havok;

namespace Meddle.UI.Windows.Views;

public class PapView : IView
{
    private readonly PapFile papFile;
    private readonly HexView havokHexView;
    private readonly HexView footerHexView;
    private readonly HexView hexView;

    public PapView(PapFile papFile)
    {
        this.papFile = papFile;
        this.havokHexView = new HexView(papFile.HavokData);
        this.footerHexView = new HexView(papFile.FooterData);
        this.hexView = new HexView(papFile.RawData);
    }

    private string? parsed;
    private HavokXml? havokXml;
    public void Draw()
    {
        ImGui.Text($"Variant: {papFile.FileHeader.Variant}");
        ImGui.Text($"Model ID: {papFile.FileHeader.ModelId}");
        ImGui.Text($"Model Type: {papFile.FileHeader.ModelType}");
        
        ImGui.Text($"Loaded {papFile.Animations.Length} animations");
        foreach (var anim in papFile.Animations)
        {
            if (ImGui.TreeNode($"{anim.GetName}##{anim.GetHashCode()}"))
            {
                ImGui.Text($"Type: {anim.Type}");
                ImGui.Text($"Havok Index: {anim.HavokIndex}");
                ImGui.Text($"Is Face: {anim.IsFace}");
                ImGui.TreePop();
            }
        }

        if (ImGui.Button("Parse"))
        {
            parsed = SkeletonUtil.ParseHavokInput(papFile.HavokData.ToArray());
            havokXml = new HavokXml(parsed);
        }
        
        if (ImGui.CollapsingHeader("XML") && parsed != null)
        {
            ImGui.TextUnformatted(parsed);
        }
        
        if (ImGui.CollapsingHeader("Parsed XML") && havokXml != null)
        {
            ImGui.SeparatorText("Skeletons");
            for (var i = 0; i < havokXml.Skeletons.Length; i++)
            {
                var skeleton = havokXml.Skeletons[i];
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
            for (var i = 0; i < havokXml.Mappings.Length; i++)
            {
                var mapping = havokXml.Mappings[i];
                ImGui.Text($"Mapping {i}");
                ImGui.BulletText($"Id: {mapping.Id}");
                ImGui.BulletText($"Bone Mappings: {mapping.BoneMappings.Length}");
                ImGui.BulletText($"Skeleton A: {mapping.SkeletonA}");
                ImGui.BulletText($"Skeleton B: {mapping.SkeletonB}");
            }
        }

        if (ImGui.CollapsingHeader("Havok Data"))
        {
            havokHexView.DrawHexDump();
        }
        
        if (ImGui.CollapsingHeader("Footer Data"))
        {
            footerHexView.DrawHexDump();
        }
        
        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
    }
}
