using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using ImGuiNET;
using Meddle.Utils.Files;
using Meddle.Utils.Models;

namespace Meddle.UI.Windows.Views;

public class MdlView : IView
{
    public readonly Model Model;
    public MdlView(MdlFile mdlFile)
    {
        this.Model = new Model(mdlFile);
    }

    public void Draw()
    {
        var mdlFile = Model.File;
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
        foreach (var (key, value) in Model.Strings)
        {
            ImGui.Text($"[{key:X4}] {value}");
        }
        
        ImGui.Text("BoneTables");
        ImGui.Columns(Model.BoneTables.Length);
        foreach (var boneTable in Model.BoneTables)
        {
            ImGui.Text($"Bone Count: {boneTable.Length}");
            foreach (var t in boneTable)
            {
                ImGui.BulletText($"{t}");
            }
            ImGui.NextColumn();
        }
        ImGui.Columns(1);
        
        ImGui.Text("Bounding boxes");
        ImGui.Text("Model");
        DrawBoundingBox(Model.File.ModelBoundingBoxes);
        ImGui.Text("Water");
        DrawBoundingBox(Model.File.WaterBoundingBoxes);
        ImGui.Text("Vertical Fog");
        DrawBoundingBox(Model.File.VerticalFogBoundingBoxes);
        for (var i = 0; i < Model.File.BoneBoundingBoxes.Length; i++)
        {
            var boneBoundingBox = Model.File.BoneBoundingBoxes[i];
            ImGui.Text($"Bone {i}");
            DrawBoundingBox(boneBoundingBox);
        }
    }
    
    private unsafe void DrawBoundingBox(ModelResourceHandle.BoundingBox bb)
    {
        ImGui.Columns(4);
        for (int i = 0; i < 4; i++)
        {
            ImGui.Text($"Min: {bb.Min[i]}");
            ImGui.Text($"Max: {bb.Max[i]}");
            ImGui.NextColumn();
        }
        ImGui.Columns(1);
    }
}
