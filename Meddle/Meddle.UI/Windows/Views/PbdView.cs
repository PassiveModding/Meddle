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
                        /*var deformMatrix = deformer.Value.DeformMatrices[i];
                        if (deformMatrix != null)
                        {
                            ImGui.Text($"Translation: {deformMatrix.Value.Translation}");
                            ImGui.Text($"Rotation: {deformMatrix.Value.Rotation}");
                            ImGui.Text($"Scale: {deformMatrix.Value.Scale}");
                        }*/
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
        
        // follow deform path for each header
        if (ImGui.CollapsingHeader("Deform Path"))
        {
            var deformerLists = new List<List<(PbdFile.Header, PbdFile.Deformer)>>();
            foreach (var header in file.Headers)
            {
                var deformerList = new List<(PbdFile.Header, PbdFile.Deformer)>();
                var currentRaceCode = header.Id;
                do
                {
                    var hdr = file.Headers.First(h => h.Id == currentRaceCode);
                    if (!file.Deformers.TryGetValue(hdr.Offset, out var deformer))
                    {
                        break;
                    }
                    
                    deformerList.Add((hdr, deformer));
                    var link = file.Links[hdr.DeformerId];
                    if (link.ParentLinkIdx == ushort.MaxValue)
                    {
                        break;
                    }
                    
                    var parentLink = file.Links[link.ParentLinkIdx];
                    var parentHeader = file.Headers[parentLink.HeaderIdx];
                    currentRaceCode = parentHeader.Id;
                }
                while (currentRaceCode != 0);
                
                deformerLists.Add(deformerList);
            }

            foreach (var deformerList in deformerLists.Where(x => x.Count > 0).OrderBy(x => x.First().Item1.Id))
            {
                var path = string.Join(" -> ", deformerList.Select(x => x.Item1.Id.ToString()));
                ImGui.Text(path);
            }
        }
        
        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
    }
}
