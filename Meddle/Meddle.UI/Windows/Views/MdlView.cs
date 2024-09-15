using ImGuiNET;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Model;
using Meddle.Utils.Helpers;

namespace Meddle.UI.Windows.Views;

public class MdlView(MdlFile mdlFile, string? path, SqPack sqPack, ImageHandler imageHandler) : IView
{
    private readonly SqPack sqPack = sqPack;
    private readonly ImageHandler imageHandler = imageHandler;
    private readonly HexView hexView = new(mdlFile.RawData);
    private Dictionary<string, (MtrlFile, MtrlView)> mtrlFiles = new();
    public void Draw()
    {
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
        
        if (ImGui.CollapsingHeader("Strings"))
        {
            var strings = mdlFile.GetStrings();
            foreach (var (key, value) in strings)
            {
                ImGui.Text($"[{key:X4}] {value}");
            }
        }
        
        if (ImGui.CollapsingHeader("Materials"))
        {
            var materialNames = mdlFile.GetMaterialNames();
            
            for (var i = 0; i < mdlFile.MaterialNameOffsets.Length; i++)
            {
                ImGui.PushID(i);
                var material = materialNames[(int)mdlFile.MaterialNameOffsets[i]];
                if (ImGui.CollapsingHeader($"Material {i}: {material}"))
                {
                    if (!mtrlFiles.ContainsKey(material))
                    {
                        if (!material.StartsWith("/"))
                        {
                            var data = sqPack.GetFile(material);
                            var mtrlFile = new MtrlFile(data!.Value.file.RawData);
                            mtrlFiles[material] = (mtrlFile, new MtrlView(mtrlFile, sqPack, imageHandler));
                        }
                        else
                        {
                            continue;
                        }
                    }
                    mtrlFiles[material].Item2.Draw();
                }
                ImGui.PopID();
            }
        }

        if (ImGui.CollapsingHeader("Bone tables"))
        {
            var boneTables = mdlFile.GetBoneTables();
            ImGui.Columns(boneTables.Length);
            foreach (var boneTable in boneTables)
            {
                ImGui.Text($"Bone Count: {boneTable.Length}");
                foreach (var t in boneTable)
                {
                    ImGui.BulletText($"{t}");
                }

                ImGui.NextColumn();
            }

            ImGui.Columns(1);
        }

        if (ImGui.CollapsingHeader("Bounding boxes"))
        {
            ImGui.Text("Model");
            DrawBoundingBox(mdlFile.ModelBoundingBoxes);
            ImGui.Text("Water");
            DrawBoundingBox(mdlFile.WaterBoundingBoxes);
            ImGui.Text("Vertical Fog");
            DrawBoundingBox(mdlFile.VerticalFogBoundingBoxes);
            for (var i = 0; i < mdlFile.BoneBoundingBoxes.Length; i++)
            {
                var boneBoundingBox = mdlFile.BoneBoundingBoxes[i];
                ImGui.Text($"Bone {i}");
                DrawBoundingBox(boneBoundingBox);
            }
        }

        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
    }
    
    private unsafe void DrawBoundingBox(BoundingBox bb)
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
