using System.Numerics;
using ImGuiNET;
using Meddle.Utils.Files;

namespace Meddle.UI.Windows.Views;

public class PbdView : IView
{
    private readonly PbdFile file;
    private readonly HexView hexView;

    public PbdView(PbdFile file)
    {
        this.file = file;
        this.hexView = new HexView(file.RawData);
    }
    
    public void Draw()
    {
        if (ImGui.CollapsingHeader("Headers"))
        {
            foreach (var header in file.Headers)
            {
                ImGui.Separator();
                ImGui.Text($"Id: {header.Id:D4}");
                var parentChain = new List<string>();
                parentChain.Add($"{header.Id:D4}");
                if (file.Deformers.TryGetValue(header.Offset, out var deformer))
                {
                    ImGui.Text($"DeformerId: {header.DeformerId}");

                    var link = file.Links[header.DeformerId];
                    if (link.ParentLinkIdx == ushort.MaxValue)
                    {
                        ImGui.Text("Root");
                    }
                    else
                    {

                        do
                        {
                            var parentLink = file.Links[link.ParentLinkIdx];
                            var parentHeader = file.Headers[parentLink.HeaderIdx];
                            parentChain.Add($"{parentHeader.Id:D4}");
                            link = parentLink;
                        }
                        while (link.ParentLinkIdx != ushort.MaxValue);

                        ImGui.Text($"Parent Chain: {string.Join(" -> ", parentChain)}");
                    }
                }
                else
                {
                    ImGui.Text("No deformer found.");
                }
            }
        }
        
        if (ImGui.CollapsingHeader("Deformers"))
        {
            foreach (var deformer in file.Deformers)
            {
                var deformerParent = file.Headers.First(h => h.Offset == deformer.Key);
                if (ImGui.CollapsingHeader($"{deformerParent.Id:D4} - Bone Count: {deformer.Value.BoneCount}"))
                {
                    for (var i = 0; i < deformer.Value.BoneCount; i++)
                    {
                        ImGui.Text($"Bone Name: {deformer.Value.BoneNames[i]}");
                        var deformMatrix = deformer.Value.DeformMatrices[i];
                        if (deformMatrix != null)
                        {
                            // draw as 3x4 matrix
                            for (var j = 0; j < 3; j++)
                            {
                                var vec4 = new Vector4(deformMatrix.Value[j * 4], 
                                                       deformMatrix.Value[j * 4 + 1], 
                                                       deformMatrix.Value[j * 4 + 2], 
                                                       deformMatrix.Value[j * 4 + 3]);
                                ImGui.SameLine();
                                ImGui.Text($"[{vec4.X:F4}, {vec4.Y:F4}, {vec4.Z:F4}, {vec4.W:F4}]");
                            }
                        }
                    }
                }
            }
        }
        
        if (ImGui.CollapsingHeader("Links"))
        {
            foreach (var link in file.Links)
            {
                ImGui.Text($"ParentLinkIdx: {link.ParentLinkIdx}");
                ImGui.Text($"HeaderIdx: {link.HeaderIdx}");
            }
        }
        
        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
    }
}
