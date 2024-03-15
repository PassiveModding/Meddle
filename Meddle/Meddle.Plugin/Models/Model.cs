using System.Runtime.InteropServices;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Meddle.Plugin.Utility;
using Meddle.Plugin.Xande;

namespace Meddle.Plugin.Models;

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

                Meshes.Add(new Mesh(hnd, meshIdx, meshVertices, mesh->StartIndex, meshIndices));
            }
        }
        
        var shapeMeshes = new ShapeMesh[hnd->Header->ShapeMeshCount];
        var meshDict = Meshes.ToDictionary(x => hnd->Meshes[x.MeshIdx].StartIndex, x => x);
        for (var i = 0; i < shapeMeshes.Length; i++)
        {
            var shapeMesh = hnd->ShapeMeshes[i];
            if (!meshDict.TryGetValue(shapeMesh.MeshIndexOffset, out var meshMatch)) continue;
            
            var values = new List<(ushort BaseIndicesIndex, ushort ReplacingVertexIndex)>();
            var range = Enumerable.Range((int)shapeMesh.ShapeValueOffset, (int)shapeMesh.ShapeValueCount);
            foreach (var idx in range)
            {
                var baseIndicesIndex = hnd->ShapeValues[idx].BaseIndicesIndex;
                var replacingVertexIndex = hnd->ShapeValues[idx].ReplacingVertexIndex;
                values.Add((baseIndicesIndex, replacingVertexIndex));
            }
            
            shapeMeshes[i] = new ShapeMesh(meshMatch, values.ToArray());
        }

        var shapes = new ModelShape[hnd->Header->ShapeCount];
        for (var i = 0; i < hnd->Header->ShapeCount; i++)
        {
            var shape = hnd->ShapesPtr[i];
            var nameIdx = hnd->StringTable + shape.StringOffset + 8;
            var name = MemoryHelper.ReadStringNullTerminated((nint)nameIdx);

            var shapeMeshCount = shape.ShapeMeshCount[lodIdx];
            var meshesForShape = new ShapeMesh[shapeMeshCount];
            var offset = shape.ShapeMeshStartIndex[lodIdx];
            for (var j = 0; j < shapeMeshCount; ++j)
            {
                meshesForShape[j] = shapeMeshes[j + offset];
            }
            
            shapes[i] = new ModelShape(name, meshesForShape);
        }
        
        Shapes = shapes.ToList();
    }
}

public class ModelShape(string name, IReadOnlyList<ShapeMesh> meshes)
{
    public string Name { get; } = name;
    public IReadOnlyList<ShapeMesh> Meshes { get; } = meshes;
}
    
public class ShapeMesh(Mesh mesh, IReadOnlyList<(ushort BaseIndicesIndex, ushort ReplacingVertexIndex)> values)
{
    public Mesh Mesh { get; } = mesh;
    public IReadOnlyList<(ushort BaseIndicesIndex, ushort ReplacedVertexIndex)> Values { get; } = values;
}
