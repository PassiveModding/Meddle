using ImGuiNET;
using Meddle.Utils.Files;
using Meddle.Utils.Models;

namespace Meddle.UI.Windows.Views;

public class MdlView : IView
{
    private readonly Model model;
    public MdlView(MdlFile mdlFile)
    {
        this.model = new Model(mdlFile);
    }

    public void Draw()
    {
        var mdlFile = model.File;
        ImGui.Text($"Version: {mdlFile.FileHeader.Version}");
        ImGui.Text($"Vertex Declarations: {mdlFile.FileHeader.VertexDeclarationCount}");
        ImGui.Text($"Lods: {mdlFile.FileHeader.LodCount}");
        ImGui.Text($"Enable Index Buffer Streaming: {mdlFile.FileHeader.EnableIndexBufferStreaming}");
        ImGui.Text($"Enable Edge Geometry: {mdlFile.FileHeader.EnableEdgeGeometry}");
        ImGui.Text($"String Count: {mdlFile.StringCount}");
        ImGui.Text($"Submesh Bone Map Byte Size: {mdlFile.SubmeshBoneMapByteSize}");
        ImGui.Text($"Attribute Name Offsets: {mdlFile.AttributeNameOffsets.Length}");
        ImGui.Text($"Material Name Offsets: {mdlFile.MaterialNameOffsets.Length}");
        ImGui.Text($"Bone Name Offsets: {mdlFile.BoneNameOffsets.Length}");
        ImGui.Text($"Bone Tables: {mdlFile.BoneTables.Length}");
        ImGui.Text($"Shapes: {mdlFile.Shapes.Length}");
        ImGui.Text($"Shape Meshes: {mdlFile.ShapeMeshes.Length}");
        ImGui.Text($"Shape Values: {mdlFile.ShapeValues.Length}");
        ImGui.Text($"Meshes: {mdlFile.Meshes.Length}");
        ImGui.Text($"Submeshes: {mdlFile.Submeshes.Length}");
        ImGui.Text($"Terrain Shadow Meshes: {mdlFile.TerrainShadowMeshes.Length}");
        ImGui.Text($"Terrain Shadow Submeshes: {mdlFile.TerrainShadowSubmeshes.Length}");
        ImGui.Text($"Element Ids: {mdlFile.ElementIds.Length}");
        ImGui.Text($"Lods: {mdlFile.Lods.Length}");
        ImGui.Text($"Extra Lods: {mdlFile.ExtraLods.Length}");
        
        ImGui.Text("Strings");
        foreach (var (key, value) in model.Strings)
        {
            ImGui.Text($"[{key:X4}] {value}");
        }
    }
}
