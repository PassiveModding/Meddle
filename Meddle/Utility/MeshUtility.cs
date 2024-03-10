using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumina.Data.Parsing;
using Lumina.Extensions;
using Meddle.Plugin.Models;
using OtterGui;
using Penumbra.GameData.Files;
using SharpGLTF.Geometry;
using SharpGLTF.IO;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Utility;

public class MeshExport
{
    private readonly MdlFile _mdl;
    private readonly byte _lod;
    private readonly ushort _meshIndex;
    private readonly RaceDeformer? _raceDeformer;
    private readonly MaterialBuilder _material;
    private readonly IReadOnlyDictionary<ushort, int>? _boneIndexMap;
    private readonly MeshUsages _meshUsages;

    private MdlStructs.MeshStruct Mesh
        => _mdl.Meshes[_meshIndex];

    public MeshExport(MdlFile mdl,
        byte lod,
        ushort meshIndex,
        MaterialBuilder[] materials,
        GltfSkeleton? skeleton,
        RaceDeformer? raceDeformer)
    {
        _mdl = mdl;
        _lod = lod;
        _meshIndex = meshIndex;
        _raceDeformer = raceDeformer;

        _material = materials[Mesh.MaterialIndex];
        if (skeleton != null)
            _boneIndexMap = BuildBoneIndexMap(skeleton.Value);

        _meshUsages = new MeshUsages(mdl.VertexDeclarations, meshIndex);
    }

    /// <summary> Build a mapping between indices in this mesh's bone table (if any), and the glTF joint indices provided. </summary>
    private Dictionary<ushort, int>? BuildBoneIndexMap(GltfSkeleton skeleton)
    {
        // A BoneTableIndex of 255 means that this mesh is not skinned.
        if (Mesh.BoneTableIndex == 255)
            return null;

        var xivBoneTable = _mdl.BoneTables[Mesh.BoneTableIndex];

        var indexMap = new Dictionary<ushort, int>();

        foreach (var (xivBoneIndex, tableIndex) in xivBoneTable.BoneIndex.Take(xivBoneTable.BoneCount).WithIndex())
        {
            var boneName = _mdl.Bones[xivBoneIndex];
            if (!skeleton.Names.TryGetValue(boneName, out var gltfBoneIndex))
            {
                /*if (!_config.GenerateMissingBones)
                    throw _notifier.Exception(
                        $@"Armature does not contain bone ""{boneName}"".
                    Ensure all dependencies are enabled in the current collection, and EST entries (if required) are configured.
                    If this is a known issue with this model and you would like to export anyway, enable the ""Generate missing bones"" option."
                    );

                (_, gltfBoneIndex) = skeleton.GenerateBone(boneName);
                _notifier.Warning(
                    $"Generated missing bone \"{boneName}\". Vertices weighted to this bone will not move with the rest of the armature.");*/
                throw new Exception(
                    $@"Armature does not contain bone ""{boneName}"".
                        Ensure all dependencies are enabled in the current collection, and EST entries (if required) are configured.
                        If this is a known issue with this model and you would like to export anyway, enable the ""Generate missing bones"" option."
                );
            }

            indexMap.Add((ushort) tableIndex, gltfBoneIndex);
        }

        return indexMap;
    }

    /// <summary> Build glTF meshes for this XIV mesh. </summary>
    public MeshData[] BuildMeshes()
    {
        var indices = BuildIndices();
        var vertices = BuildVertices();

        // NOTE: Index indices are specified relative to the LOD's 0, but we're reading chunks for each mesh, so we're specifying the index base relative to the mesh's base.
        if (Mesh.SubMeshCount == 0)
            return [BuildMesh($"mesh {_meshIndex}", indices, vertices, 0, (int) Mesh.IndexCount, 0)];

        return _mdl.SubMeshes
            .Skip(Mesh.SubMeshIndex)
            .Take(Mesh.SubMeshCount)
            .WithIndex()
            .Select(subMesh => BuildMesh($"mesh {_meshIndex}.{subMesh.Index}", indices, vertices,
                (int) (subMesh.Value.IndexOffset - Mesh.StartIndex), (int) subMesh.Value.IndexCount,
                subMesh.Value.AttributeIndexMask))
            .ToArray();
    }

    private MeshData BuildMesh(
        string name,
        IReadOnlyList<ushort> indices,
        IReadOnlyList<IVertexBuilder> vertices,
        int indexBase,
        int indexCount,
        uint attributeMask
    )
    {
        var meshBuilderType = typeof(MeshBuilder<,,,>).MakeGenericType(
            typeof(MaterialBuilder),
            _meshUsages.GeometryType,
            _meshUsages.MaterialType,
            _meshUsages.SkinningType
        );
        var meshBuilder = (IMeshBuilder<MaterialBuilder>) Activator.CreateInstance(meshBuilderType, name)!;

        var primitiveBuilder = meshBuilder.UsePrimitive(_material);

        // Store a list of the glTF indices. The list index will be equivalent to the xiv (submesh) index.
        var gltfIndices = new List<int>();

        // All XIV meshes use triangle lists.
        for (var indexOffset = 0; indexOffset < indexCount; indexOffset += 3)
        {
            var (a, b, c) = primitiveBuilder.AddTriangle(
                vertices[indices[indexBase + indexOffset + 0]],
                vertices[indices[indexBase + indexOffset + 1]],
                vertices[indices[indexBase + indexOffset + 2]]
            );
            gltfIndices.AddRange([a, b, c]);
        }

        var primitiveVertices = meshBuilder.Primitives.First().Vertices;
        var shapeNames = new List<string>();

        foreach (var shape in _mdl.Shapes)
        {
            // Filter down to shape values for the current mesh that sit within the bounds of the current submesh.
            var shapeValues = _mdl.ShapeMeshes
                .Skip(shape.ShapeMeshStartIndex[_lod])
                .Take(shape.ShapeMeshCount[_lod])
                .Where(shapeMesh => shapeMesh.MeshIndexOffset == Mesh.StartIndex)
                .SelectMany(shapeMesh =>
                    _mdl.ShapeValues
                        .Skip((int) shapeMesh.ShapeValueOffset)
                        .Take((int) shapeMesh.ShapeValueCount)
                )
                .Where(shapeValue =>
                    shapeValue.BaseIndicesIndex >= indexBase
                    && shapeValue.BaseIndicesIndex < indexBase + indexCount
                )
                .ToList();

            if (shapeValues.Count == 0)
                continue;

            var morphBuilder = meshBuilder.UseMorphTarget(shapeNames.Count);
            shapeNames.Add(shape.ShapeName);

            foreach (var (shapeValue, shapeValueIndex) in shapeValues.WithIndex())
            {
                var gltfIndex = gltfIndices[shapeValue.BaseIndicesIndex - indexBase];

                if (gltfIndex == -1)
                {
                    //_notifier.Warning($"{name}: Shape {shape.ShapeName} mapping {shapeValueIndex} targets a degenerate triangle, ignoring.");
                    continue;
                }

                morphBuilder.SetVertex(
                    primitiveVertices[gltfIndex].GetGeometry(),
                    vertices[shapeValue.ReplacingVertexIndex].GetGeometry()
                );
            }
        }

        // Named morph targets aren't part of the specification, however `MESH.extras.targetNames`
        // is a commonly-accepted means of providing the data.
        meshBuilder.Extras = JsonNode.Parse(JsonSerializer.Serialize(new Dictionary<string, object>()
        {
            {"targetNames", shapeNames}
        }));

        string[] attributes = [];
        var maxAttribute = 31 - BitOperations.LeadingZeroCount(attributeMask);
        if (maxAttribute < _mdl.Attributes.Length)
        {
            attributes = Enumerable.Range(0, 32)
                .Where(index => ((attributeMask >> index) & 1) == 1)
                .Select(index => _mdl.Attributes[index])
                .ToArray();
        }
        else
        {
            //_notifier.Warning("Invalid attribute data, ignoring.");
        }

        return new MeshData
        {
            Mesh = meshBuilder,
            Attributes = attributes,
        };
    }

    private IReadOnlyList<ushort> BuildIndices()
    {
        var reader = new BinaryReader(new MemoryStream(_mdl.RemainingData));
        reader.Seek(_mdl.IndexOffset[_lod] + Mesh.StartIndex * sizeof(ushort));
        return reader.ReadStructuresAsArray<ushort>((int) Mesh.IndexCount);
    }

    private IReadOnlyList<IVertexBuilder> BuildVertices()
    {
        var vertexBuilderType = typeof(VertexBuilder<,,>)
            .MakeGenericType(_meshUsages.GeometryType, _meshUsages.MaterialType, _meshUsages.SkinningType);

        const int MaximumMeshBufferStreams = 3;
        // NOTE: This assumes that buffer streams are tightly packed, which has proven safe across tested files. If this assumption is broken, seeks will need to be moved into the vertex element loop.
        var streams = new BinaryReader[MaximumMeshBufferStreams];
        for (var streamIndex = 0; streamIndex < MaximumMeshBufferStreams; streamIndex++)
        {
            streams[streamIndex] = new BinaryReader(new MemoryStream(_mdl.RemainingData));
            streams[streamIndex].Seek(_mdl.VertexOffset[_lod] + Mesh.VertexBufferOffset[streamIndex]);
        }

        var sortedElements = _mdl.VertexDeclarations[_meshIndex].VertexElements
            .OrderBy(element => element.Offset)
            .Select(element => ((MdlFile.VertexUsage) element.Usage, element))
            .ToList();

        var vertices = new List<IVertexBuilder>();

        var attributes = new Dictionary<MdlFile.VertexUsage, object>();
        for (var vertexIndex = 0; vertexIndex < Mesh.VertexCount; vertexIndex++)
        {
            attributes.Clear();

            foreach (var (usage, element) in sortedElements)
                attributes[usage] = ReadVertexAttribute((MdlFile.VertexType) element.Type, streams[element.Stream]);

            var vertexSkinning = _meshUsages.BuildVertexSkinning(attributes, _boneIndexMap);
            var vertexGeometry = _meshUsages.BuildVertexGeometry(attributes,
                _raceDeformer == null ? null : (_raceDeformer, vertexSkinning.jointWeights));
            var vertexMaterial = _meshUsages.BuildVertexMaterial(attributes);

            var vertexBuilder = (IVertexBuilder) Activator.CreateInstance(vertexBuilderType, vertexGeometry,
                vertexMaterial, vertexSkinning.skinning)!;
            vertices.Add(vertexBuilder);
        }

        return vertices;
    }

    /// <summary> Read a vertex attribute of the specified type from a vertex buffer stream. </summary>
    private object ReadVertexAttribute(MdlFile.VertexType type, BinaryReader reader)
    {
        return type switch
        {
            MdlFile.VertexType.Single3 => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            MdlFile.VertexType.Single4 => new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle()),
            MdlFile.VertexType.UByte4 => reader.ReadBytes(4),
            MdlFile.VertexType.NByte4 => new Vector4(reader.ReadByte() / 255f, reader.ReadByte() / 255f,
                reader.ReadByte() / 255f,
                reader.ReadByte() / 255f),
            MdlFile.VertexType.Half2 => new Vector2((float) reader.ReadHalf(), (float) reader.ReadHalf()),
            MdlFile.VertexType.Half4 => new Vector4((float) reader.ReadHalf(), (float) reader.ReadHalf(),
                (float) reader.ReadHalf(),
                (float) reader.ReadHalf()),

            var other => throw new ArgumentOutOfRangeException($"Unhandled vertex type {other}"),
        };
    }
}