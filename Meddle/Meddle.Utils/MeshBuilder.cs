using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using Mesh = Meddle.Utils.Export.Mesh;

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
        (GenderRace fromDeform, GenderRace toDeform, RaceDeformer deformer)? raceDeformer
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

        if (raceDeformer != null && jointLut != null)
        {
            var (from, to, deformer) = raceDeformer.Value;
            RaceDeformer = deformer;
            Deformers = deformer.PbdFile.GetDeformers((ushort)from, (ushort)to).ToList();
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

                    throw new InvalidOperationException($"Bone index {boneIndex} is out of bounds!");
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
        // ReSharper disable CompareOfFloatsByEqualityOperator
        if (GeometryT == typeof(VertexPositionNormalTangent))
        {
            geometryParamCache.Add(vertex.Tangent1!.Value with { W = vertex.Tangent1.Value.W == 1 ? 1 : -1 });
        }
        if (GeometryT == typeof(VertexPositionNormalTangent2))
        {
            geometryParamCache.Add(vertex.Tangent1!.Value with { W = vertex.Tangent1.Value.W == 1 ? 1 : -1 });
            geometryParamCache.Add(vertex.Tangent2!.Value with { W = vertex.Tangent2.Value.W == 1 ? 1 : -1 });
        }
        // ReSharper restore CompareOfFloatsByEqualityOperator

        // AKA: Has "Color1" component
        // Some models have a color component, but it's packed data, so we don't use it as color
        if (MaterialT != typeof(VertexTexture2))
        {
            Vector4 vertexColor = new Vector4(1, 1, 1, 1);
            if (MaterialBuilder is IVertexPaintMaterialBuilder paintBuilder)
            {
                vertexColor = paintBuilder.VertexPaint switch
                {
                    true => vertex.Color!.Value,
                    false => new Vector4(1, 1, 1, 1)
                };
            }
            
            materialParamCache.Insert(0, vertexColor);
        }

        //if(MaterialT != typeof(VertexTexture2)) materialParamCache.Insert(0, vertex.Color!.Value);
        //if(MaterialT != typeof(VertexTexture2)) materialParamCache.Insert(0, new Vector4(1, 1, 1, 1));

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
        
        if (vertex[0].Tangent2 != null && vertex[0].Tangent1 != null)
        {
            return typeof(VertexPositionNormalTangent2);
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
    
    private static IVertexGeometry CreateGeometryParamCache(Vertex vertex, Type type)
    {
        // ReSharper disable CompareOfFloatsByEqualityOperator
        switch (type)
        {
            case not null when type == typeof(VertexPosition):
                return new VertexPosition(vertex.Position!.Value);
            case not null when type == typeof(VertexPositionNormal):
                return new VertexPositionNormal(vertex.Position!.Value, vertex.Normal!.Value);
            case not null when type == typeof(VertexPositionNormalTangent):
                // Tangent W should be 1 or -1, but sometimes XIV has their -1 as 0?
                return new VertexPositionNormalTangent(vertex.Position!.Value, 
                       vertex.Normal!.Value, 
                       vertex.Tangent1!.Value with { W = vertex.Tangent1.Value.W == 1 ? 1 : -1 });
            case not null when type == typeof(VertexPositionNormalTangent2):
                return new VertexPositionNormalTangent2(vertex.Position!.Value, 
                    vertex.Normal!.Value, 
                    vertex.Tangent1!.Value with { W = vertex.Tangent1.Value.W == 1 ? 1 : -1 },
                    vertex.Tangent2!.Value with { W = vertex.Tangent2.Value.W == 1 ? 1 : -1 });
            default:
                return new VertexPosition(vertex.Position!.Value);
        }
        // ReSharper restore CompareOfFloatsByEqualityOperator
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

        if (hasColor && hasUv)
        {
            return typeof(VertexColor1Texture2);
        }
        
        if (hasColor)
        {
            return typeof(VertexColor1);
        }
        
        if (hasUv)
        {
            return typeof(VertexTexture2);
        }
        
        return typeof(VertexColor1);
    }
    
    private static IVertexMaterial CreateMaterialParamCache(Vertex vertex, Type type)
    {
        switch (type)
        {
            case not null when type == typeof(VertexColor1):
            {
                return new VertexColor1(vertex.Color!.Value);
            }
            case not null when type == typeof(VertexTexture2):
            {
                var (xy, zw) = ToVec2(vertex.UV!.Value);
                return new VertexTexture2(xy, zw);
            }
            case not null when type == typeof(VertexColor1Texture2):
            {
                var (xy, zw) = ToVec2(vertex.UV!.Value);
                return new VertexColor1Texture2(vertex.Color!.Value, xy, zw);
            }
            default:
                return new VertexEmpty();
        }
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

public struct VertexPositionNormalTangent2 : IVertexGeometry, IEquatable<VertexPositionNormalTangent2>
{
    public VertexPositionNormalTangent2(in Vector3 p, in Vector3 n, in Vector4 t, in Vector4 t2)
    {
        this.Position = p;
        this.Normal = n;
        this.Tangent = t;
        this.Tangent2 = t2;
    }

    public static implicit operator VertexPositionNormalTangent2(in (Vector3 Pos, Vector3 Nrm, Vector4 Tgt, Vector4 Tgt2) tuple)
    {
        return new VertexPositionNormalTangent2(tuple.Pos, tuple.Nrm, tuple.Tgt, tuple.Tgt2);
    }

    #region data
    
    public Vector3 Position;        
    public Vector3 Normal;
    public Vector4 Tangent;
    public Vector4 Tangent2;

    IEnumerable<KeyValuePair<string, AttributeFormat>> IVertexReflection.GetEncodingAttributes()
    {
        yield return new KeyValuePair<string, AttributeFormat>("POSITION", new AttributeFormat(DimensionType.VEC3));
        yield return new KeyValuePair<string, AttributeFormat>("NORMAL", new AttributeFormat(DimensionType.VEC3));
        yield return new KeyValuePair<string, AttributeFormat>("TANGENT", new AttributeFormat(DimensionType.VEC4));
        yield return new KeyValuePair<string, AttributeFormat>("TANGENT2", new AttributeFormat(DimensionType.VEC4));
    }

    public override readonly int GetHashCode() { return Position.GetHashCode(); }

    /// <inheritdoc/>
    public override readonly bool Equals(object obj) { return obj is VertexPositionNormalTangent2 other && AreEqual(this, other); }

    /// <inheritdoc/>
    public readonly bool Equals(VertexPositionNormalTangent2 other) { return AreEqual(this, other); }
    public static bool operator ==(in VertexPositionNormalTangent2 a, in VertexPositionNormalTangent2 b) { return AreEqual(a, b); }
    public static bool operator !=(in VertexPositionNormalTangent2 a, in VertexPositionNormalTangent2 b) { return !AreEqual(a, b); }
    public static bool AreEqual(in VertexPositionNormalTangent2 a, in VertexPositionNormalTangent2 b)
    {
        return a.Position == b.Position && a.Normal == b.Normal && a.Tangent == b.Tangent && a.Tangent2 == b.Tangent2;
    }        

    #endregion

    #region API

    void IVertexGeometry.SetPosition(in Vector3 position) { this.Position = position; }

    void IVertexGeometry.SetNormal(in Vector3 normal) { this.Normal = normal; }

    void IVertexGeometry.SetTangent(in Vector4 tangent) { this.Tangent = tangent; }
    
    void SetTangent2(in Vector4 tangent2) { this.Tangent2 = tangent2; }

    /// <inheritdoc/>
    public readonly VertexGeometryDelta Subtract(IVertexGeometry baseValue)
    {
        var baseVertex = (VertexPositionNormalTangent2)baseValue;
        var tangentDelta = this.Tangent - baseVertex.Tangent;

        return new VertexGeometryDelta(
            this.Position - baseVertex.Position,
            this.Normal - baseVertex.Normal,
            new Vector3(tangentDelta.X, tangentDelta.Y, tangentDelta.Z));
    }

    public void Add(in VertexGeometryDelta delta)
    {
        this.Position += delta.PositionDelta;
        this.Normal += delta.NormalDelta;
        this.Tangent += new Vector4(delta.TangentDelta, 0);
    }

    public readonly Vector3 GetPosition() { return this.Position; }
    public readonly bool TryGetNormal(out Vector3 normal) { normal = this.Normal; return true; }
    public readonly bool TryGetTangent(out Vector4 tangent) { tangent = this.Tangent; return true; }
    public readonly bool TryGetTangent2(out Vector4 tangent2) { tangent2 = this.Tangent2; return true; }

    /// <inheritdoc/>
    public void ApplyTransform(in Matrix4x4 xform)
    {
        Position = Vector3.Transform(Position, xform);
        Normal = Vector3.Normalize(Vector3.TransformNormal(Normal, xform));

        var txyz = Vector3.Normalize(Vector3.TransformNormal(new Vector3(Tangent.X, Tangent.Y, Tangent.Z), xform));
        Tangent = new Vector4(txyz, Tangent.W);
        
        var t2xyz = Vector3.Normalize(Vector3.TransformNormal(new Vector3(Tangent2.X, Tangent2.Y, Tangent2.Z), xform));
        Tangent2 = new Vector4(t2xyz, Tangent2.W);
    }

    #endregion
}
