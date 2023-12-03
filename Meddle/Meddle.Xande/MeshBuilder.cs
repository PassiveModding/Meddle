using Lumina.Models.Models;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using System.Numerics;
using Xande.Files;
using Xande.Models.Export;

namespace Meddle.Xande;

public class MeshBuilder
{
    private Mesh Mesh { get; }
    private List<object> GeometryParamCache { get; } = new();
    private List<object> MaterialParamCache { get; } = new();
    private List<(int, float)> SkinningParamCache { get; } = new();
    private object[] VertexBuilderParams { get; } = new object[3];

    private IReadOnlyDictionary<int, int> JointMap { get; }
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
        Mesh mesh,
        bool useSkinning,
        IReadOnlyDictionary<int, int> jointMap,
        MaterialBuilder materialBuilder,
        RaceDeformer? raceDeformer
    )
    {
        Mesh = mesh;
        JointMap = jointMap;
        MaterialBuilder = materialBuilder;
        RaceDeformer = raceDeformer;

        GeometryT = GetVertexGeometryType(Mesh.Vertices);
        MaterialT = GetVertexMaterialType(Mesh.Vertices);
        SkinningT = useSkinning ? typeof(VertexJoints4) : typeof(VertexEmpty);
        VertexBuilderT = typeof(VertexBuilder<,,>).MakeGenericType(GeometryT, MaterialT, SkinningT);
        MeshBuilderT = typeof(MeshBuilder<,,,>).MakeGenericType(typeof(MaterialBuilder), GeometryT, MaterialT, SkinningT);
        Vertices = new List<IVertexBuilder>(Mesh.Vertices.Length);
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
    public IMeshBuilder<MaterialBuilder> BuildSubmesh(Submesh submesh)
    {
        var ret = (IMeshBuilder<MaterialBuilder>)Activator.CreateInstance(MeshBuilderT, string.Empty)!;
        var primitive = ret.UsePrimitive(MaterialBuilder);

        for (var triIdx = 0; triIdx < submesh.IndexNum; triIdx += 3)
        {
            var triA = Vertices[Mesh.Indices[triIdx + (int)submesh.IndexOffset + 0]];
            var triB = Vertices[Mesh.Indices[triIdx + (int)submesh.IndexOffset + 1]];
            var triC = Vertices[Mesh.Indices[triIdx + (int)submesh.IndexOffset + 2]];
            primitive.AddTriangle(triA, triB, triC);
        }

        return ret;
    }

    /// <summary>Creates a mesh from the entire mesh.</summary>
    public IMeshBuilder<MaterialBuilder> BuildMesh()
    {
        var ret = (IMeshBuilder<MaterialBuilder>)Activator.CreateInstance(MeshBuilderT, string.Empty)!;
        var primitive = ret.UsePrimitive(MaterialBuilder);

        for (var triIdx = 0; triIdx < Mesh.Indices.Length; triIdx += 3)
        {
            var triA = Vertices[Mesh.Indices[triIdx + 0]];
            var triB = Vertices[Mesh.Indices[triIdx + 1]];
            var triC = Vertices[Mesh.Indices[triIdx + 2]];
            primitive.AddTriangle(triA, triB, triC);
        }

        return ret;
    }

    /// <summary>Builds shape keys (known as morph targets in glTF).</summary>
    public void BuildShapes(IReadOnlyList<Shape> shapes, IMeshBuilder<MaterialBuilder> builder, int subMeshStart, int subMeshEnd)
    {
        var primitive = builder.Primitives.First();
        var triangles = primitive.Triangles;
        var vertices = primitive.Vertices;
        var vertexList = new List<(IVertexGeometry, IVertexGeometry)>();
        var nameList = new List<Shape>();
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

            if (vertexList.Count == 0) continue;

            var morph = builder.UseMorphTarget(nameList.Count);
            foreach (var (a, b) in vertexList) { morph.SetVertex(a, b); }

            nameList.Add(shape);
        }

        var data = new ExtraDataManager();
        data.AddShapeNames(nameList);
        builder.Extras = data.Serialize();
    }

    private IVertexBuilder BuildVertex(Vertex vertex)
    {
        ClearCaches();

        var skinningIsEmpty = SkinningT == typeof(VertexEmpty);
        if (!skinningIsEmpty)
        {
            for (var k = 0; k < 4; k++)
            {
                var boneIndex = vertex.BlendIndices[k];
                if (JointMap == null || !JointMap.ContainsKey(boneIndex)) continue;
                var mappedBoneIndex = JointMap[boneIndex];
                var boneWeight = vertex.BlendWeights != null ? vertex.BlendWeights.Value[k] : 0;

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

        // AKA: Has "TextureN" component
        if (MaterialT != typeof(VertexColor1)) MaterialParamCache.Add(ToVec2(vertex.UV!.Value));

        // AKA: Has "Color1" component
        //if( _materialT != typeof( VertexTexture1 ) ) _materialParamCache.Insert( 0, vertex.Color!.Value );
        if (MaterialT != typeof(VertexTexture1)) MaterialParamCache.Insert(0, new Vector4(255, 255, 255, 255));


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
    private static Type GetVertexGeometryType(Vertex[] vertex)
    {
        if (vertex.Length == 0)
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
    private static Type GetVertexMaterialType(Vertex[] vertex)
    {
        if (vertex.Length == 0)
        {
            return typeof(VertexColor1);
        }

        var hasColor = vertex[0].Color != null;
        var hasUv = vertex[0].UV != null;

        return hasColor switch
        {
            true when hasUv => typeof(VertexColor1Texture1),
            false when hasUv => typeof(VertexTexture1),
            _ => typeof(VertexColor1),
        };
    }

    private static Vector3 ToVec3(Vector4 v) => new(v.X, v.Y, v.Z);
    private static Vector2 ToVec2(Vector4 v) => new(v.X, v.Y);
}
