using System.Runtime.InteropServices;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Utility;
using Meddle.Plugin.Xande;
using CSModel = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model;
using CSTexture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Models;

public unsafe class Model
{
    public string HandlePath { get; private set; }
    public ushort? RaceCode { get; private set; }

    public IReadOnlyList<Material> Materials { get; private set; }
    public IReadOnlyList<Mesh> Meshes { get; private set; }
    public IReadOnlyList<ModelShape> Shapes { get; private set; }
    public uint ShapesMask { get; private set; }
    public uint AttributesMask { get; private set; }
    public IReadOnlyList<string> EnabledShapes { get; private set; }
    public IReadOnlyList<string> EnabledAttributes { get; private set; }

    public Model(Pointer<CSModel> model, Pointer<Pointer<CSTexture>> colorTable) : this(model.Value, (CSTexture**)colorTable.Value)
    {

    }
    
    public Model(CSModel* model, CSTexture** colorTable)
    {
        HandlePath = model->ModelResourceHandle->ResourceHandle.FileName.ToString();
        RaceCode = (ushort)RaceDeformer.ParseRaceCode(HandlePath);

        var materials = new List<Material>();
        for (var i = 0; i < model->MaterialCount; ++i)
            materials.Add(new Material(model->Materials[i], colorTable == null ? null : colorTable[i]));

        Materials = materials;
        Meshes = new List<Mesh>();
        Shapes = new List<ModelShape>();
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

        const int lodIdx = 0;

        var lod = &hnd->Lods[lodIdx];
        var vertexBuffer = DXHelper.ExportVertexBuffer(hnd->VertexBuffersSpan[lodIdx]);
        var indexBuffer = MemoryMarshal.Cast<byte, ushort>(hnd->IndexBuffersSpan[lodIdx].Value->AsSpan());

        var meshRanges = new[] {
            lod->MeshIndex..(lod->MeshIndex + lod->MeshCount),
            lod->WaterMeshIndex..(lod->WaterMeshIndex + lod->WaterMeshCount),
            lod->ShadowMeshIndex..(lod->ShadowMeshIndex + lod->ShadowMeshCount),
            lod->TerrainShadowMeshIndex..(lod->TerrainShadowMeshIndex + lod->TerrainShadowMeshCount),
            lod->VerticalFogMeshIndex..(lod->VerticalFogMeshIndex + lod->VerticalFogMeshCount),
        };
        if (hnd->ExtraLods != null)
        {
            var extraLod = &hnd->ExtraLods[lodIdx];
            meshRanges = meshRanges.Concat(new[]
            {
                extraLod->LightShaftMeshIndex..(extraLod->LightShaftMeshIndex + extraLod->LightShaftMeshCount),
                extraLod->GlassMeshIndex..(extraLod->GlassMeshIndex + extraLod->GlassMeshCount),
                extraLod->MaterialChangeMeshIndex..(extraLod->MaterialChangeMeshIndex + extraLod->MaterialChangeMeshCount),
                extraLod->CrestChangeMeshIndex..(extraLod->CrestChangeMeshIndex + extraLod->CrestChangeMeshCount),
            }).ToArray();
        }

        var meshes = new List<Mesh>();
        foreach (var range in meshRanges.AsConsolidated())
        {
            for (var i = range.Start.Value; i < range.End.Value; i++)
            {
                var mesh = &hnd->Meshes[i];
                var meshVertexDecls = hnd->KernelVertexDeclarations[i];
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
                        throw new ArgumentException($"Mesh {i} has index {index}, which is negative");
                    if (index >= meshVertices.Length)
                        throw new ArgumentException($"Mesh {i} has index {index}, but only {meshVertices.Length} vertices exist");
                }

                if (meshIndices.Length != mesh->IndexCount)
                    throw new ArgumentException($"Mesh {i} has {meshIndices.Length} indices, but {mesh->IndexCount} were expected");

                meshes.Add(new Mesh(hnd, i, meshVertices, mesh->StartIndex, meshIndices));
            }
        }
        
        Meshes = meshes;
        
        var shapeMeshes = new ShapeMesh[hnd->Header->ShapeMeshCount];
        var meshDict = Meshes.ToDictionary(x => hnd->Meshes[x.MeshIdx].StartIndex, x => x);
        for (var i = 0; i < shapeMeshes.Length; i++)
        {
            var shapeMesh = hnd->ShapeMeshes[i];
            if (!meshDict.TryGetValue(shapeMesh.MeshIndexOffset, out var meshMatch)) continue;
            shapeMeshes[i] = new ShapeMesh(hnd->ShapeValues, shapeMesh, meshMatch, i);
        }

        var shapes = new ModelShape[hnd->Header->ShapeCount];
        for (var i = 0; i < hnd->Header->ShapeCount; i++)
        {
            var shape = hnd->ShapesPtr[i];
            var nameIdx = hnd->StringTable + shape.StringOffset + 8;
            var name = MemoryHelper.ReadStringNullTerminated((nint)nameIdx);
            shapes[i] = new ModelShape(shape, name, lodIdx, shapeMeshes);
        }
        
        Shapes = shapes;
    }
}
