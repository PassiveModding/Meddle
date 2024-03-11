using System.Runtime.InteropServices;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Lumina;
using Meddle.Plugin.Xande.Utility;
using Xande.Models.Export;

namespace Meddle.Plugin.Xande.Models;

public unsafe class Model
{
    public string HandlePath { get; set; }
    public ushort? RaceCode { get; set; }

    public List<Material> Materials { get; set; }

    public List<Mesh> Meshes { get; set; }
    public List<ModelShape> Shapes { get; set; }

    public uint ShapesMask { get; set; }
    public uint AttributesMask { get; set; }
    public string[] EnabledShapes { get; set; }
    public string[] EnabledAttributes { get; set; }

    public Model(Lumina.Models.Models.Model model, GameData gameData)
    {
        model.Update(gameData);

        HandlePath = model.File?.FilePath.Path ?? "Lumina Model";
        RaceCode = (ushort)RaceDeformer.ParseRaceCode(HandlePath);

        Materials = new();
        foreach (var material in model.Materials)
            Materials.Add(new(material, gameData));

        Meshes = new();
        foreach (var mesh in model.Meshes)
            Meshes.Add(new(mesh));

        Shapes = new();
        foreach (var shape in model.Shapes.Values)
            Shapes.Add(new(shape));

        ShapesMask = 0;
        AttributesMask = 0;

        EnabledShapes = Array.Empty<string>();
        EnabledAttributes = Array.Empty<string>();
    }

    public Model(Pointer<FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model> model, Pointer<Pointer<FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture>> colorTable) : this(model.Value, (FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture**)colorTable.Value)
    {

    }

    public Model(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model* model, FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture** colorTable)
    {
        HandlePath = model->ModelResourceHandle->ResourceHandle.FileName.ToString();
        RaceCode = (ushort)RaceDeformer.ParseRaceCode(HandlePath);

        Materials = new();
        for (var i = 0; i < model->MaterialCount; ++i)
            Materials.Add(new(model->Materials[i], colorTable == null ? null : colorTable[i]));

        Meshes = new();
        Shapes = new();
        LoadMeshesAndShapes(model->ModelResourceHandle);

        ShapesMask = model->EnabledShapeKeyIndexMask;
        AttributesMask = model->EnabledAttributeIndexMask;

        EnabledShapes = model->ModelResourceHandle->Shapes
                        .Where(kv => ((1 << kv.Item2) & ShapesMask) != 0)
                        .Select(kv => MemoryHelper.ReadStringNullTerminated((nint)kv.Item1.Value))
                        .ToArray();
        EnabledAttributes = model->ModelResourceHandle->Attributes
                            .Where(kv => ((1 << kv.Item2) & AttributesMask) != 0)
                            .Select(kv => MemoryHelper.ReadStringNullTerminated((nint)kv.Item1.Value))
                            .ToArray();
    }

    private void LoadMeshesAndShapes(ModelResourceHandle* hnd)
    {
        if (hnd->KernelVertexDeclarations == null)
            throw new ArgumentException("No KernelVertexDeclarations exist");

        const int LodIdx = 0;

        var lod = &hnd->Lods[LodIdx];
        var vertexBuffer = DXHelper.ExportVertexBuffer(hnd->VertexBuffersSpan[LodIdx]);
        var indexBuffer = MemoryMarshal.Cast<byte, ushort>(hnd->IndexBuffersSpan[LodIdx].Value->AsSpan());

        var meshRanges = new[] {
            lod->MeshIndex..(lod->MeshIndex + lod->MeshCount),
            lod->WaterMeshIndex..(lod->WaterMeshIndex + lod->WaterMeshCount),
            lod->ShadowMeshIndex..(lod->ShadowMeshIndex + lod->ShadowMeshCount),
            lod->TerrainShadowMeshIndex..(lod->TerrainShadowMeshIndex + lod->TerrainShadowMeshCount),
            lod->VerticalFogMeshIndex..(lod->VerticalFogMeshIndex + lod->VerticalFogMeshCount),
        };
        if (hnd->ExtraLods != null)
        {
            var extraLod = &hnd->ExtraLods[LodIdx];
            meshRanges = meshRanges.Concat(new[]
            {
                extraLod->LightShaftMeshIndex..(extraLod->LightShaftMeshIndex + extraLod->LightShaftMeshCount),
                extraLod->GlassMeshIndex..(extraLod->GlassMeshIndex + extraLod->GlassMeshCount),
                extraLod->MaterialChangeMeshIndex..(extraLod->MaterialChangeMeshIndex + extraLod->MaterialChangeMeshCount),
                extraLod->CrestChangeMeshIndex..(extraLod->CrestChangeMeshIndex + extraLod->CrestChangeMeshCount),
            }).ToArray();
        }

        foreach (var range in meshRanges.AsConsolidated())
        {
            foreach (var meshIdx in range.GetEnumerator())
            {
                var mesh = &hnd->Meshes[meshIdx];
                var meshVertexDecls = hnd->KernelVertexDeclarations[meshIdx];
                var meshVertices = new Vertex[mesh->VertexCount];
                var meshIndices = indexBuffer.Slice((int)mesh->StartIndex, (int)mesh->IndexCount);

                foreach (var element in meshVertexDecls->ElementsSpan)
                {
                    if (element.Stream == 255)
                        break;

                    var streamIdx = element.Stream;
                    var vertexStreamStride = mesh->VertexBufferStride[streamIdx];
                    var vertexStreamOffset = mesh->VertexBufferOffset[streamIdx];
                    var vertexStreamBuffer = vertexBuffer.AsSpan((int)vertexStreamOffset, vertexStreamStride * mesh->VertexCount);

                    Vertex.Apply(meshVertices, vertexStreamBuffer, element, vertexStreamStride);
                }

                foreach (var index in meshIndices)
                {
                    if (index < 0)
                        throw new ArgumentException($"Mesh {meshIdx} has index {index}, which is negative");
                    if (index >= meshVertices.Length)
                        throw new ArgumentException($"Mesh {meshIdx} has index {index}, but only {meshVertices.Length} vertices exist");
                }

                if (meshIndices.Length != mesh->IndexCount)
                    throw new ArgumentException($"Mesh {meshIdx} has {meshIndices.Length} indices, but {mesh->IndexCount} were expected");

                Meshes.Add(new(hnd, meshIdx, meshVertices, mesh->StartIndex, meshIndices));
            }
        }
    }
}
