using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using Mesh = Meddle.Utils.Export.Mesh;

namespace Meddle.Utils;

public class MeshBuilder
{
    private Mesh Mesh { get; }
    private IReadOnlyList<BoneNodeBuilder>? BoneMap { get; }
    private MaterialBuilder MaterialBuilder { get; }
    private RaceDeformer? RaceDeformer { get; }
    public Type GeometryT { get; }
    public Type MaterialT { get; }
    public Type SkinningT { get; }
    public Type VertexBuilderT { get; }
    public Type MeshBuilderT { get; }

    private IReadOnlyList<PbdFile.Deformer> Deformers { get; }

    private IReadOnlyList<IVertexBuilder> Vertices { get; }

    public MeshBuilder(
        Mesh mesh,
        IReadOnlyList<BoneNodeBuilder>? boneMap,
        MaterialBuilder materialBuilder,
        (GenderRace fromDeform, GenderRace toDeform, RaceDeformer deformer)? raceDeformer
    )
    {
        BoneMap = boneMap;
        Mesh = mesh;
        MaterialBuilder = materialBuilder;
        
        GeometryT = GetVertexGeometryType(Mesh.Vertices);
        MaterialT = GetVertexMaterialType(Mesh.Vertices);
        SkinningT = GetVertexSkinningType(Mesh.Vertices, boneMap != null);
        VertexBuilderT = typeof(VertexBuilder<,,>).MakeGenericType(GeometryT, MaterialT, SkinningT);
        MeshBuilderT =
            typeof(MeshBuilder<,,,>).MakeGenericType(typeof(MaterialBuilder), GeometryT, MaterialT, SkinningT);

        if (raceDeformer != null && BoneMap != null)
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

    private IReadOnlyList<IVertexBuilder> BuildVertices()
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
        var skinning = CreateSkinningParamCache(vertex, BoneMap, SkinningT, out var skinningWeights);

        var vertexBuilderParams = new object[]
        {
            CreateGeometryParamCache(vertex, GeometryT, skinningWeights),
            CreateMaterialParamCache(vertex, MaterialT, MaterialBuilder),
            skinning
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

        if (vertex[0].Binormal != null)
        {
            return typeof(VertexPositionNormalTangent);
        }

        if (vertex[0].Normal != null)
        {
            return typeof(VertexPositionNormal);
        }

        return typeof(VertexPosition);
    }
    
    private IVertexGeometry CreateGeometryParamCache(Vertex vertex, Type type, IReadOnlyList<(int, float)> skinningWeights)
    {
        var position = vertex.Position!.Value;
        var currentPos = position;

        if (Deformers.Count > 0 && RaceDeformer != null)
        {
            foreach (var deformer in Deformers)
            {
                var deformedPos = Vector3.Zero;

                foreach (var (idx, weight) in skinningWeights)
                {
                    if (weight == 0) continue;

                    var deformPos = RaceDeformer.DeformVertex(deformer, idx, currentPos);
                    if (deformPos != null) deformedPos += deformPos.Value * weight;
                }

                currentPos = deformedPos;
            }
        }
        
        // ReSharper disable CompareOfFloatsByEqualityOperator
        switch (type)
        {
            case not null when type == typeof(VertexPosition):
                return new VertexPosition(currentPos);
            case not null when type == typeof(VertexPositionNormal):
                return new VertexPositionNormal(currentPos, vertex.Normal!.Value.SanitizeNormal());
            case not null when type == typeof(VertexPositionNormalTangent):
                // Tangent W should be 1 or -1, but sometimes XIV has their -1 as 0?
                return new VertexPositionNormalTangent(currentPos, 
                                                       vertex.Normal!.Value.SanitizeNormal(), 
                                                       (vertex.Binormal!.Value with { W = vertex.Binormal.Value.W == 1 ? 1 : -1 }).SanitizeTangent()); 
            default:
                Global.Logger.LogWarning("Unknown vertex type, defaulting to VertexPosition {Vertex}", JsonSerializer.Serialize(vertex, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    IncludeFields = true
                }));
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

        var firstVertex = vertex[0];
        // (UsageIndex +) 8 => TEXCOORD_0
        // (UsageIndex +) 3 => COLOR0

        var colors = 0;
        if (firstVertex.Color != null) colors++;
        if (firstVertex.Color2 != null) colors++;
        
        var texCoords = 0;
        if (firstVertex.TexCoord != null) texCoords += 2;
        if (firstVertex.TexCoord2 != null) texCoords += 2;

        var type = (colors, texCoords) switch
        {
            (2, 0) => typeof(VertexColor2),
            (2, 1) => typeof(VertexColor2Texture1),
            (2, 2) => typeof(VertexColor2Texture2),
            (2, 3) => typeof(VertexColor2Texture3),
            (2, 4) => typeof(VertexColor2Texture4),
            (1, 0) => typeof(VertexColor1),
            (1, 1) => typeof(VertexColor1Texture1),
            (1, 2) => typeof(VertexColor1Texture2),
            (1, 3) => typeof(VertexColor1Texture3),
            (1, 4) => typeof(VertexColor1Texture4),
            (0, 0) => typeof(VertexEmpty),
            (0, 1) => typeof(VertexTexture1),
            (0, 2) => typeof(VertexTexture2),
            (0, 3) => typeof(VertexTexture3),
            (0, 4) => typeof(VertexTexture4),
            _ => null
        };

        if (type == null)
        {
            Global.Logger.LogWarning("Unknown vertex material type, defaulting to VertexColor1");
            type = typeof(VertexColor1);
        }
        
        return type;
    }
    
    private static Vector4 GetColor(Vertex vertex, MaterialBuilder materialBuilder)
    {
        // Some models have a color component, but it's packed data, so we don't use it as color
        if (materialBuilder is IVertexPaintMaterialBuilder paintBuilder)
        {
            return paintBuilder.VertexPaint switch
            {
                true => vertex.Color!.Value,
                false => new Vector4(1, 1, 1, 1)
            };
        }

        return new Vector4(1, 1, 1, 1);
    }
    
    private static IVertexMaterial CreateMaterialParamCache(Vertex vertex, Type type, MaterialBuilder materialBuilder)
    {
        if (type == typeof(VertexColor2))
        {
            return new VertexColor2(GetColor(vertex, materialBuilder), vertex.Color2!.Value);
        }
        
        if (type == typeof(VertexColor2Texture1))
        {
            return new VertexColor2Texture1(GetColor(vertex, materialBuilder), vertex.Color2!.Value, ToVec2(vertex.TexCoord!.Value).XY);
        }
        
        if (type == typeof(VertexColor2Texture2))
        {
            var texCoord = ToVec2(vertex.TexCoord!.Value);
            return new VertexColor2Texture2(GetColor(vertex, materialBuilder), vertex.Color2!.Value, texCoord.XY, texCoord.ZW);
        }
        
        if (type == typeof(VertexColor2Texture3))
        {
            var texCoord = ToVec2(vertex.TexCoord!.Value);
            var texCoord2 = ToVec2(vertex.TexCoord2!.Value);
            return new VertexColor2Texture3(GetColor(vertex, materialBuilder), vertex.Color2!.Value, texCoord.XY, texCoord.ZW, texCoord2.XY);
        }
        
        if (type == typeof(VertexColor2Texture4))
        {
            var texCoord = ToVec2(vertex.TexCoord!.Value);
            var texCoord2 = ToVec2(vertex.TexCoord2!.Value);
            return new VertexColor2Texture4(GetColor(vertex, materialBuilder), vertex.Color2!.Value, texCoord.XY, texCoord.ZW, texCoord2.XY, texCoord2.ZW);
        }
        
        if (type == typeof(VertexColor1))
        {
            return new VertexColor1(GetColor(vertex, materialBuilder));
        }
        
        if (type == typeof(VertexColor1Texture1))
        {
            return new VertexColor1Texture1(GetColor(vertex, materialBuilder), ToVec2(vertex.TexCoord!.Value).XY);
        }
        
        if (type == typeof(VertexColor1Texture2))
        {
            var texCoord = ToVec2(vertex.TexCoord!.Value);
            return new VertexColor1Texture2(GetColor(vertex, materialBuilder), texCoord.XY, texCoord.ZW);
        }
        
        if (type == typeof(VertexColor1Texture3))
        {
            var texCoord = ToVec2(vertex.TexCoord!.Value);
            var texCoord2 = ToVec2(vertex.TexCoord2!.Value);
            return new VertexColor1Texture3(GetColor(vertex, materialBuilder), texCoord.XY, texCoord.ZW, texCoord2.XY);
        }
        
        if (type == typeof(VertexColor1Texture4))
        {
            var texCoord = ToVec2(vertex.TexCoord!.Value);
            var texCoord2 = ToVec2(vertex.TexCoord2!.Value);
            return new VertexColor1Texture4(GetColor(vertex, materialBuilder), texCoord.XY, texCoord.ZW, texCoord2.XY, texCoord2.ZW);
        }
        
        if (type == typeof(VertexTexture1))
        {
            return new VertexTexture1(ToVec2(vertex.TexCoord!.Value).XY);
        }
        
        if (type == typeof(VertexTexture2))
        {
            var texCoord = ToVec2(vertex.TexCoord!.Value);
            return new VertexTexture2(texCoord.XY, texCoord.ZW);
        }
        
        if (type == typeof(VertexTexture3))
        {
            var texCoord = ToVec2(vertex.TexCoord!.Value);
            var texCoord2 = ToVec2(vertex.TexCoord2!.Value);
            return new VertexTexture3(texCoord.XY, texCoord.ZW, texCoord2.XY);
        }
        
        if (type == typeof(VertexTexture4))
        {
            var texCoord = ToVec2(vertex.TexCoord!.Value);
            var texCoord2 = ToVec2(vertex.TexCoord2!.Value);
            return new VertexTexture4(texCoord.XY, texCoord.ZW, texCoord2.XY, texCoord2.ZW);
        }
        
        return new VertexEmpty();
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
    
    private IVertexSkinning CreateSkinningParamCache(Vertex vertex, IReadOnlyList<BoneNodeBuilder>? boneMap, Type type, out List<(int, float)> skinningWeights)
    {
        skinningWeights = new List<(int, float)>();
        if (type == typeof(VertexEmpty) || boneMap == null)
        {
            return new VertexEmpty();
        }
        
        if (vertex.BlendIndices == null)
            throw new InvalidOperationException("Vertex has no blend indices data.");
        for (var k = 0; k < vertex.BlendIndices.Length; k++)
        {
            var boneIndex = vertex.BlendIndices[k];
            var boneWeight = vertex.BlendWeights != null ? vertex.BlendWeights[k] : 0;
            
            if (Mesh.BoneTable == null) continue;
            var boneName = Mesh.BoneTable[boneIndex];
            var boneNode = boneMap.FirstOrDefault(x => x.BoneName.Equals(boneName, StringComparison.Ordinal));
            if (boneNode == null)
            {
                if (boneWeight == 0) continue;
                Global.Logger.LogWarning("Bone {BoneName} not found in bone map for {Mesh} but has weight {Weight}", boneName, Mesh.MeshIdx, boneWeight);
                continue;
            }
            
            // map bone to index within boneMap
            var mappedBoneIndex = 0;
            for (var i = 0; i < boneMap.Count; i++)
            {
                if (boneMap[i].BoneName.Equals(boneName, StringComparison.Ordinal))
                {
                    mappedBoneIndex = i;
                    break;
                }
            }
            
            skinningWeights.Add((mappedBoneIndex, boneWeight));
        }
        
        return type switch
        {
            not null when type == typeof(VertexJoints4) => new VertexJoints4(skinningWeights.ToArray()),
            not null when type == typeof(VertexJoints8) => new VertexJoints8(skinningWeights.ToArray()),
            _ => new VertexEmpty()
        };
    }

    private static Vector3 ToVec3(Vector4 v) => new(v.X, v.Y, v.Z);
    private static (Vector2 XY, Vector2 ZW) ToVec2(Vector4 v) => (new(v.X, v.Y), new(v.Z, v.W));
}
