using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;

namespace Meddle.Utils;

public class MeshBuilder
{
    private Mesh Mesh { get; }
    private int[]? JointLut { get; }
    private MaterialBuilder MaterialBuilder { get; }
    private RaceDeformer? RaceDeformer { get; }
    private Type GeometryT { get; }
    private Type MaterialT { get; }
    private Type SkinningT { get; }
    private Type VertexBuilderT { get; }
    private Type MeshBuilderT { get; }

    private IReadOnlyList<PbdFile.Deformer> Deformers { get; }

    private IReadOnlyList<IVertexBuilder> Vertices { get; }

    public MeshBuilder(
        Mesh mesh,
        int[]? jointLut,
        MaterialBuilder materialBuilder,
        (RaceDeformer deformer, ushort from, ushort to)? raceDeformer
    )
    {
        Mesh = mesh;
        JointLut = jointLut;
        MaterialBuilder = materialBuilder;

        GeometryT = GetVertexGeometryType(Mesh.Vertices);
        MaterialT = GetVertexMaterialType(Mesh.Vertices);
        SkinningT = GetVertexSkinningType(Mesh.Vertices, JointLut != null);
        VertexBuilderT = typeof(VertexBuilder<,,>).MakeGenericType(GeometryT, MaterialT, SkinningT);
        MeshBuilderT =
            typeof(MeshBuilder<,,,>).MakeGenericType(typeof(MaterialBuilder), GeometryT, MaterialT, SkinningT);

        if (raceDeformer != null)
        {
            var (deformer, from, to) = raceDeformer.Value;
            RaceDeformer = deformer;
            Deformers = deformer.PbdFile.GetDeformers(from, to).ToList();
        }
        else
        {
            Deformers = Array.Empty<PbdFile.Deformer>();
        }

        Vertices = BuildVertices();
    }

    public IReadOnlyList<IVertexBuilder> BuildVertices()
    {
        return Mesh.Vertices.Select(BuildVertex).ToList();
        // Parallel impl keep index
        //var vertices = new IVertexBuilder[Mesh.Vertices.Count];
        //Parallel.For(0, Mesh.Vertices.Count, i => { vertices[i] = BuildVertex(Mesh.Vertices[i]); });
        //return vertices;
    }

    /// <summary>Creates a mesh from the given sub mesh.</summary>
    public IMeshBuilder<MaterialBuilder> BuildSubMesh(SubMesh subMesh)
    {
        var ret = (IMeshBuilder<MaterialBuilder>)Activator.CreateInstance(MeshBuilderT, string.Empty)!;
        var primitive = ret.UsePrimitive(MaterialBuilder);

        if (subMesh.IndexCount + subMesh.IndexOffset > Mesh.Indices.Count)
            throw new InvalidOperationException("SubMesh index count is out of bounds.");
        for (var triIdx = 0; triIdx < subMesh.IndexCount; triIdx += 3)
        {
            var o = triIdx + (int)subMesh.IndexOffset;
            var indA = Mesh.Indices[o + 0];
            var indB = Mesh.Indices[o + 1];
            var indC = Mesh.Indices[o + 2];
            var triA = Vertices[indA];
            var triB = Vertices[indB];
            var triC = Vertices[indC];
            primitive.AddTriangle(triA, triB, triC);
        }

        return ret;
    }

    /// <summary>Creates a mesh from the entire mesh.</summary>
    public IMeshBuilder<MaterialBuilder> BuildMesh()
    {
        var ret = (IMeshBuilder<MaterialBuilder>)Activator.CreateInstance(MeshBuilderT, string.Empty)!;
        var primitive = ret.UsePrimitive(MaterialBuilder);

        for (var triIdx = 0; triIdx < Mesh.Indices.Count; triIdx += 3)
        {
            var triA = Vertices[Mesh.Indices[triIdx + 0]];
            var triB = Vertices[Mesh.Indices[triIdx + 1]];
            var triC = Vertices[Mesh.Indices[triIdx + 2]];
            primitive.AddTriangle(triA, triB, triC);
        }

        return ret;
    }

    /// <summary>Builds shape keys (known as morph targets in glTF).</summary>
    public IReadOnlyList<string> BuildShapes(IReadOnlyList<ModelShape> shapes, IMeshBuilder<MaterialBuilder> builder, int subMeshStart, int subMeshEnd)
    {
        var primitive = builder.Primitives.First();
        var triangles = primitive.Triangles;
        var vertices = primitive.Vertices;
        var shapeNames = new List<string>();
        foreach (var shape in shapes)
        {
            var vertexList = new List<(IVertexGeometry, IVertexGeometry)>();
            foreach (var shapeMesh in shape.Meshes.Where(m => m.Mesh.MeshIdx == Mesh.MeshIdx))
            {
                foreach (var (baseIdx, otherIdx) in shapeMesh.Values)
                {
                    if (baseIdx < subMeshStart || baseIdx >= subMeshEnd) continue; // different submesh?
                    var triIdx = (baseIdx - subMeshStart) / 3;
                    var vertexIdx = (baseIdx - subMeshStart) % 3;
                    
                    if (triangles.Count <= triIdx) continue;
                    
                    var triA = triangles[triIdx];
                    var vertexA = vertices[vertexIdx switch
                    {
                        0 => triA.A,
                        1 => triA.B,
                        _ => triA.C,
                    }];

                    vertexList.Add((vertexA.GetGeometry(), Vertices[otherIdx].GetGeometry()));
                }
            }
            
            if (vertexList.Count == 0) continue;

            var morph = builder.UseMorphTarget(shapeNames.Count);
            shapeNames.Add(shape.Name);
            foreach (var (a, b) in vertexList)
            {
                morph.SetVertex(a, b);
            }
        }

        builder.Extras = JsonNode.Parse(JsonSerializer.Serialize(new Dictionary<string, string[]>
        {
            { "targetNames", shapeNames.ToArray() }
        }));

        return shapeNames;
    }

    private IVertexBuilder BuildVertex(Vertex vertex)
    {
        var skinningParamCache = new List<(int, float)>();
        var geometryParamCache = new List<object>();
        var materialParamCache = new List<object>();

        var skinningIsEmpty = SkinningT == typeof(VertexEmpty);
        if (!skinningIsEmpty && JointLut != null)
        {
            if (vertex.BlendIndices == null)
                throw new InvalidOperationException("Vertex has no blend indices data.");
            for (var k = 0; k < vertex.BlendIndices.Length; k++)
            {
                var boneIndex = vertex.BlendIndices[k];
                var boneWeight = vertex.BlendWeights != null ? vertex.BlendWeights[k] : 0;
                if (boneIndex >= JointLut.Length)
                {
                    if (boneWeight == 0)
                        continue;

                    var indices = vertex.BlendIndices?.Select(x => (int)x).ToArray();
                    
                    var serializedVertexData = new
                    {
                        Vertex = new
                        {
                            vertex.Position,
                            vertex.BlendWeights,
                            BlendIndices = indices,
                            vertex.Normal,
                            vertex.UV,
                            vertex.Color,
                            vertex.Tangent2,
                            vertex.Tangent1
                        },
                        JoinLutSize = JointLut.Length,
                        JointLut = JointLut,
                        BoneIndex = boneIndex,
                        BoneWeight = boneWeight
                    };
                    
                    var json = JsonSerializer.Serialize(serializedVertexData, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true});
                    
                    throw new InvalidOperationException($"Bone index {boneIndex} is out of bounds! Vertex data: {json}");
                }
                var mappedBoneIndex = JointLut[boneIndex];

                var binding = (mappedBoneIndex, boneWeight);
                skinningParamCache.Add(binding);
            }
        }

        var origPos = vertex.Position!.Value;
        var currentPos = origPos;

        if (Deformers.Count > 0 && RaceDeformer != null)
        {
            foreach (var deformer in Deformers)
            {
                var deformedPos = Vector3.Zero;

                foreach (var (idx, weight) in skinningParamCache)
                {
                    if (weight == 0) continue;

                    var deformPos = RaceDeformer.DeformVertex(deformer, idx, currentPos);
                    if (deformPos != null) deformedPos += deformPos.Value * weight;
                }

                currentPos = deformedPos;
            }
        }

        geometryParamCache.Add(currentPos);

        // Means it's either VertexPositionNormal or VertexPositionNormalTangent; both have Normal
        if (GeometryT != typeof(VertexPosition)) geometryParamCache.Add(vertex.Normal!.Value);

        // Tangent W should be 1 or -1, but sometimes XIV has their -1 as 0?
        if (GeometryT == typeof(VertexPositionNormalTangent))
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            geometryParamCache.Add(vertex.Tangent1!.Value with { W = vertex.Tangent1.Value.W == 1 ? 1 : -1 });
        }

        // AKA: Has "Color1" component
        //if( _materialT != typeof( VertexTexture2 ) ) _materialParamCache.Insert( 0, vertex.Color!.Value );
        if (MaterialT != typeof(VertexTexture2)) materialParamCache.Insert(0, new Vector4(255, 255, 255, 255));

        // AKA: Has "TextureN" component
        if (MaterialT != typeof(VertexColor1))
        {
            var (xy, zw) = ToVec2(vertex.UV!.Value);
            materialParamCache.Add(xy);
            materialParamCache.Add(zw);
        }

        var vertexBuilderParams = new object[]
        {
            Activator.CreateInstance(GeometryT, geometryParamCache.ToArray())!,
            Activator.CreateInstance(MaterialT, materialParamCache.ToArray())!,
            skinningIsEmpty
                ? Activator.CreateInstance(SkinningT)!
                : Activator.CreateInstance(SkinningT, skinningParamCache.ToArray())!
        };
        return (IVertexBuilder)Activator.CreateInstance(VertexBuilderT, vertexBuilderParams)!;
    }

    /// <summary>Obtain the correct geometry type for a given set of vertices.</summary>
    private static Type GetVertexGeometryType(IReadOnlyList<Vertex> vertex)
    {
        if (vertex.Count == 0)
        {
            return typeof(VertexPosition);
        }

        if (vertex[0].Tangent1 != null)
        {
            return typeof(VertexPositionNormalTangent);
        }

        if (vertex[0].Normal != null)
        {
            return typeof(VertexPositionNormal);
        }

        return typeof(VertexPosition);
    }

    /// <summary>Obtain the correct material type for a set of vertices.</summary>
    private static Type GetVertexMaterialType(IReadOnlyList<Vertex> vertex)
    {
        if (vertex.Count == 0)
        {
            return typeof(VertexColor1);
        }

        var hasColor = vertex[0].Color != null;
        var hasUv = vertex[0].UV != null;

        return hasColor switch
        {
            true when hasUv => typeof(VertexColor1Texture2),
            false when hasUv => typeof(VertexTexture2),
            _ => typeof(VertexColor1),
        };
    }
    
    private static Type GetVertexSkinningType(IReadOnlyList<Vertex> vertex, bool isSkinned)
    {
        if (vertex.Count == 0 || !isSkinned)
        {
            return typeof(VertexEmpty);
        }

        var blendIndices = vertex[0].BlendIndices;
        return blendIndices?.Length switch
        {
            4 => typeof(VertexJoints4),
            8 => typeof(VertexJoints8),
            _ => typeof(VertexEmpty)
        };
    }

    private static Vector3 ToVec3(Vector4 v) => new(v.X, v.Y, v.Z);
    private static (Vector2 XY, Vector2 ZW) ToVec2(Vector4 v) => (new(v.X, v.Y), new(v.Z, v.W));
}
