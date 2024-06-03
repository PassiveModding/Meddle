using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.UI.Windows.Views;

public class MdlView(MdlFile mdlFile) : IView
{
    private readonly Model model = new(mdlFile);
    private Utils.Export.Model? fmodel;
    private readonly HexView hexView = new(mdlFile.RawData);
    private readonly HexView remainingView = new(mdlFile.RemainingData);

    public void Draw()
    {
        if (ImGui.Button("Test"))
        {
            fmodel = new Utils.Export.Model(this.model.File, "");
            var materialCount = model.File.MaterialNameOffsets.Length; 
            var materials = new MaterialBuilder[materialCount];
            for (var i = 0; i < materialCount; i++)
            {
                var name = model.Strings[(int)model.File.MaterialNameOffsets[i]];
                materials[i] = new MaterialBuilder(name);
            }

            var scene = new SceneBuilder();
            var meshes = ModelBuilder.BuildMeshes(fmodel, materials, Array.Empty<BoneNodeBuilder>(), null);
            foreach (var mesh in meshes)
            {
                scene.AddRigidMesh(mesh.Mesh, Matrix4x4.Identity);
            }
            
            var sceneGraph = scene.ToGltf2();
            sceneGraph.SaveGLB("test.glb");
        }
        
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

        if (ImGui.CollapsingHeader("Strings"))
        {
            foreach (var (key, value) in model.Strings)
            {
                ImGui.Text($"[{key:X4}] {value}");
            }
        }

        if (ImGui.CollapsingHeader("Bone tables"))
        {
            ImGui.Columns(model.BoneTables.Length);
            foreach (var boneTable in model.BoneTables)
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
            DrawBoundingBox(model.File.ModelBoundingBoxes);
            ImGui.Text("Water");
            DrawBoundingBox(model.File.WaterBoundingBoxes);
            ImGui.Text("Vertical Fog");
            DrawBoundingBox(model.File.VerticalFogBoundingBoxes);
            for (var i = 0; i < model.File.BoneBoundingBoxes.Length; i++)
            {
                var boneBoundingBox = model.File.BoneBoundingBoxes[i];
                ImGui.Text($"Bone {i}");
                DrawBoundingBox(boneBoundingBox);
            }
        }

        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
        
        if (ImGui.CollapsingHeader("Remaining Data"))
        {
            remainingView.DrawHexDump();
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
