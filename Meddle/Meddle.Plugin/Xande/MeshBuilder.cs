using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using Xande.Files;
using Xande.Models.Export;
using ExtraDataManager = Meddle.Plugin.Xande.ExtraDataManager;

namespace Meddle.Plugin.Xande;

public class MeshBuilder
{
    private NewMesh Mesh { get; }
    private List<object> GeometryParamCache { get; } = new();
    private List<object> MaterialParamCache { get; } = new();
    private List<(int, float)> SkinningParamCache { get; } = new();
    private object[] VertexBuilderParams { get; } = new object[3];

    private int[]? JointLut { get; }
    private MaterialBuilder MaterialBuilder { get; }
    private RaceDeformer? RaceDeformer { get; }

    private Type GeometryT { get; }
    private Type MaterialT { get; }
    private Type SkinningT { get; }
    private Type VertexBuilderT { get; }
    private Type MeshBuilderT { get; }

    private List<PbdFile.Deformer> Deformers { get; set; } = new();

    private List<IVertexBuilder> Vertices { get; }

    public MeshBuilder(
        NewMesh mesh,
        bool useSkinning,
        int[]? jointLut,
        MaterialBuilder materialBuilder,
        RaceDeformer? raceDeformer
    )
    {
        Mesh = mesh;
        JointLut = jointLut;
        MaterialBuilder = materialBuilder;
        RaceDeformer = raceDeformer;

        GeometryT = GetVertexGeometryType(Mesh.Vertices);
        MaterialT = GetVertexMaterialType(Mesh.Vertices);
        SkinningT = useSkinning ? typeof(VertexJoints4) : typeof(VertexEmpty);
        VertexBuilderT = typeof(VertexBuilder<,,>).MakeGenericType(GeometryT, MaterialT, SkinningT);
        MeshBuilderT = typeof(MeshBuilder<,,,>).MakeGenericType(typeof(MaterialBuilder), GeometryT, MaterialT, SkinningT);
        Vertices = new List<IVertexBuilder>(Mesh.Vertices.Count);
    }

    /// <summary>Calculates the deformation steps from two given races.</summary>
    /// <param name="from">The current race of the mesh.</param>
    /// <param name="to">The target race of the mesh.</param>
    public void SetupDeformSteps(ushort from, ushort to)
    {
        // Nothing to do
        if (from == to || RaceDeformer == null) return;

        var deformSteps = new List<ushort>();
        ushort? current = to;

        while (current != null)
        {
            deformSteps.Add(current.Value);
            current = RaceDeformer.GetParent(current.Value);
            if (current == from) break;
        }

        // Reverse it to the right order
        deformSteps.Reverse();

        // Turn these into deformers
        var pbd = RaceDeformer.PbdFile;
        var deformers = new PbdFile.Deformer[deformSteps.Count];
        for (var i = 0; i < deformSteps.Count; i++)
        {
            var raceCode = deformSteps[i];
            var deformer = pbd.GetDeformerFromRaceCode(raceCode);
            deformers[i] = deformer;
        }

        Deformers = deformers.ToList();
    }

    /// <summary>Builds the vertices. This must be called before building meshes.</summary>
    public void BuildVertices()
    {
        Vertices.Clear();
        Vertices.AddRange(Mesh.Vertices.Select(BuildVertex));
    }

    /// <summary>Creates a mesh from the given submesh.</summary>
    public IMeshBuilder<MaterialBuilder> BuildSubmesh(NewSubMesh submesh)
    {
        var ret = (IMeshBuilder<MaterialBuilder>)Activator.CreateInstance(MeshBuilderT, string.Empty)!;
        var primitive = ret.UsePrimitive(MaterialBuilder);

        if (submesh.IndexCount + submesh.IndexOffset > Mesh.Indices.Count)
            throw new InvalidOperationException("Submesh index count is out of bounds.");
        for (var triIdx = 0; triIdx < submesh.IndexCount; triIdx += 3)
        {
            var o = triIdx + (int)submesh.IndexOffset;
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
    public void BuildShapes(IReadOnlyList<NewModelShape> shapes, IMeshBuilder<MaterialBuilder> builder, int subMeshStart, int subMeshEnd)
    {
        var primitive = builder.Primitives.First();
        var triangles = primitive.Triangles;
        var vertices = primitive.Vertices;
        var vertexList = new List<(IVertexGeometry, IVertexGeometry)>();
        for (var i = 0; i < shapes.Count; ++i)
        {
            var shape = shapes[i];
            vertexList.Clear();
            foreach (var shapeMesh in shape.Meshes.Where(m => m.AssociatedMesh == Mesh))
            {
                foreach (var (baseIdx, otherIdx) in shapeMesh.Values)
                {
                    if (baseIdx < subMeshStart || baseIdx >= subMeshEnd) continue; // different submesh?
                    var triIdx = (baseIdx - subMeshStart) / 3;
                    var vertexIdx = (baseIdx - subMeshStart) % 3;

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

            var morph = builder.UseMorphTarget(i);
            foreach (var (a, b) in vertexList) { morph.SetVertex(a, b); }
        }

        var data = new ExtraDataManager();
        data.AddShapeNames(shapes);
        builder.Extras = data.Serialize();
    }

    private IVertexBuilder BuildVertex(NewVertex vertex)
    {
        ClearCaches();

        var skinningIsEmpty = SkinningT == typeof(VertexEmpty);
        if (!skinningIsEmpty && JointLut != null)
        {
            for (var k = 0; k < 4; k++)
            {
                var boneIndex = vertex.BlendIndices[k];
                var boneWeight = vertex.BlendWeights != null ? vertex.BlendWeights.Value[k] : 0;
                if (boneIndex >= JointLut.Length)
                {
                    if (boneWeight == 0)
                        continue;
                    throw new InvalidOperationException($"Bone index {boneIndex} is out of bounds! ({vertex.BlendWeights!.Value[k]})");
                }
                var mappedBoneIndex = JointLut[boneIndex];

                var binding = (mappedBoneIndex, boneWeight);
                SkinningParamCache.Add(binding);
            }
        }

        var origPos = ToVec3(vertex.Position!.Value);
        var currentPos = origPos;

        if (Deformers.Count > 0)
        {
            foreach (var deformer in Deformers)
            {
                var deformedPos = Vector3.Zero;

                foreach (var (idx, weight) in SkinningParamCache)
                {
                    if (weight == 0) continue;

                    var deformPos = RaceDeformer!.DeformVertex(deformer, idx, currentPos);
                    if (deformPos != null) deformedPos += deformPos.Value * weight;
                }

                currentPos = deformedPos;
            }
        }

        GeometryParamCache.Add(currentPos);

        // Means it's either VertexPositionNormal or VertexPositionNormalTangent; both have Normal
        if (GeometryT != typeof(VertexPosition)) GeometryParamCache.Add(vertex.Normal!.Value);

        // Tangent W should be 1 or -1, but sometimes XIV has their -1 as 0?
        if (GeometryT == typeof(VertexPositionNormalTangent))
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            GeometryParamCache.Add(vertex.Tangent1!.Value with { W = vertex.Tangent1.Value.W == 1 ? 1 : -1 });
        }

        // AKA: Has "Color1" component
        //if( _materialT != typeof( VertexTexture2 ) ) _materialParamCache.Insert( 0, vertex.Color!.Value );
        if (MaterialT != typeof(VertexTexture2)) MaterialParamCache.Insert(0, new Vector4(255, 255, 255, 255));

        // AKA: Has "TextureN" component
        if (MaterialT != typeof(VertexColor1))
        {
            var (xy, zw) = ToVec2(vertex.UV!.Value);
            MaterialParamCache.Add(xy);
            MaterialParamCache.Add(zw);
        }


        VertexBuilderParams[0] = Activator.CreateInstance(GeometryT, GeometryParamCache.ToArray())!;
        VertexBuilderParams[1] = Activator.CreateInstance(MaterialT, MaterialParamCache.ToArray())!;
        VertexBuilderParams[2] = skinningIsEmpty
                                        ? Activator.CreateInstance(SkinningT)!
                                        : Activator.CreateInstance(SkinningT, SkinningParamCache.ToArray())!;

        return (IVertexBuilder)Activator.CreateInstance(VertexBuilderT, VertexBuilderParams)!;
    }

    private void ClearCaches()
    {
        GeometryParamCache.Clear();
        MaterialParamCache.Clear();
        SkinningParamCache.Clear();
    }

    /// <summary>Obtain the correct geometry type for a given set of vertices.</summary>
    private static Type GetVertexGeometryType(List<NewVertex> vertex)
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
    private static Type GetVertexMaterialType(List<NewVertex> vertex)
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

    private static Vector3 ToVec3(Vector4 v) => new(v.X, v.Y, v.Z);
    private static (Vector2 XY, Vector2 ZW) ToVec2(Vector4 v) => (new(v.X, v.Y), new(v.Z, v.W));
}
