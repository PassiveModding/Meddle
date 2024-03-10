using System.Collections.Immutable;
using System.Numerics;
using Lumina.Data.Parsing;
using Meddle.Plugin.Utility;
using Penumbra.GameData.Files;
using SharpGLTF.Geometry.VertexTypes;

namespace Meddle.Plugin.Models;

public class MeshUsages
{
    private Type GetGeometryType(IReadOnlyDictionary<MdlFile.VertexUsage, MdlFile.VertexType> usages)
    {
        if (!usages.ContainsKey(MdlFile.VertexUsage.Position))
            throw new Exception("Mesh does not contain position vertex elements.");

        if (!usages.ContainsKey(MdlFile.VertexUsage.Normal))
            return typeof(VertexPosition);

        if (!usages.ContainsKey(MdlFile.VertexUsage.Tangent1))
            return typeof(VertexPositionNormal);

        return typeof(VertexPositionNormalTangent);
    }
    
    private (Type, int) GetMaterialType(IReadOnlyDictionary<MdlFile.VertexUsage, MdlFile.VertexType> usages)
    {
        var uvCount = 0;
        if (usages.TryGetValue(MdlFile.VertexUsage.UV, out var type))
            uvCount = type switch
            {
                MdlFile.VertexType.Half2   => 1,
                MdlFile.VertexType.Half4   => 2,
                MdlFile.VertexType.Single4 => 2,
                _                          => throw new Exception($"Unexpected UV vertex type {type}."),
            };

        var materialUsages = (
            uvCount,
            usages.ContainsKey(MdlFile.VertexUsage.Color)
        );

        return materialUsages switch
        {
            (2, true)  => (typeof(XivVertex), 2),
            (1, true)  => (typeof(XivVertex), 1),
            (0, true)  => (typeof(XivVertex), 0),
            (2, false) => (typeof(VertexTexture2), 2),
            (1, false) => (typeof(VertexTexture1), 1),
            (0, false) => (typeof(VertexEmpty), 0),

            _ => throw new Exception("Unreachable."),
        };
    }
    
    private static Type GetSkinningType(IReadOnlyDictionary<MdlFile.VertexUsage, MdlFile.VertexType> usages)
    {
        if (usages.ContainsKey(MdlFile.VertexUsage.BlendWeights) && usages.ContainsKey(MdlFile.VertexUsage.BlendIndices))
            return typeof(VertexJoints4);

        return typeof(VertexEmpty);
    }

    public readonly Type GeometryType;
    public readonly Type MaterialType;
    public readonly int UvCount;
    public readonly Type SkinningType;
    
    public MeshUsages(MdlStructs.VertexDeclarationStruct[] declarationStructs, int meshIndex)
    {
        var usages = declarationStructs[meshIndex].VertexElements
            .ToImmutableDictionary(
                element => (MdlFile.VertexUsage) element.Usage,
                element => (MdlFile.VertexType) element.Type
            );
        
        GeometryType = GetGeometryType(usages);
        var (materialType, uvCount) = GetMaterialType(usages);
        MaterialType = materialType;
        UvCount = uvCount;
        SkinningType = GetSkinningType(usages);
    }


    public (IVertexSkinning skinning, IReadOnlyList<(int jointIndex, float weight)> jointWeights) BuildVertexSkinning(
        IReadOnlyDictionary<MdlFile.VertexUsage, object> attributes,
        IReadOnlyDictionary<ushort, int>? boneIndexMap
    )
    {
        if (SkinningType == typeof(VertexEmpty))
            return (new VertexEmpty(), Array.Empty<(int, float)>());

        if (SkinningType == typeof(VertexJoints4))
        {
            if (boneIndexMap == null)
                throw new Exception("Tried to build skinned vertex but no bone mappings are available.");

            var bindings = GetBoneIndices(attributes, boneIndexMap).ToArray();
            return (new VertexJoints4(bindings), bindings);
        }

        throw new Exception($"Unknown skinning type {SkinningType}");
    }

    // <summary> Get bone indices for skinning a vertex. </summary> // IReadOnlyDictionary<MdlFile.VertexUsage, object> attributes
    public IReadOnlyList<(int jointIndex, float weight)> GetBoneIndices(
        IReadOnlyDictionary<MdlFile.VertexUsage, object> attributes,
        IReadOnlyDictionary<ushort, int> boneIndexMap)
    {
        if (boneIndexMap == null)
            throw new Exception("Tried to build skinned vertex but no bone mappings are available.");

        var indices = ToByteArray(attributes[MdlFile.VertexUsage.BlendIndices]);
        var weights = ToVector4(attributes[MdlFile.VertexUsage.BlendWeights]);

        var bindings = Enumerable.Range(0, 4)
            .Select(bindingIndex =>
            {
                // NOTE: I've not seen any files that throw this error that aren't completely broken.
                var xivBoneIndex = indices[bindingIndex];
                if (!boneIndexMap.TryGetValue(xivBoneIndex, out var jointIndex))
                    throw new Exception($"Vertex contains weight for unknown bone index {xivBoneIndex}.");

                return (jointIndex, weights[bindingIndex]);
            })
            .ToArray();
        return bindings;
    }

    public IVertexGeometry BuildVertexGeometry(
        IReadOnlyDictionary<MdlFile.VertexUsage, object> attributes,
        (RaceDeformer deformer, IReadOnlyList<(int jointIndex, float weight)> jointWeights)? deform = null)
    {
        var position = ToVector3(attributes[MdlFile.VertexUsage.Position]);
        if (deform.HasValue)
        {
            position = deform.Value.deformer.DeformVertex(position, deform.Value.jointWeights) ?? position;
        }
        
        if (GeometryType == typeof(VertexPosition))
            return new VertexPosition(position);

        if (GeometryType == typeof(VertexPositionNormal))
            return new VertexPositionNormal(
                position,
                ToVector3(attributes[MdlFile.VertexUsage.Normal])
            );

        if (GeometryType == typeof(VertexPositionNormalTangent))
        {
            // (Bi)tangents are universally stored as ByteFloat4, which uses 0..1 to represent the full -1..1 range.
            // TODO: While this assumption is safe, it would be sensible to actually check.
            var bitangent = ToVector4(attributes[MdlFile.VertexUsage.Tangent1]) * 2 - Vector4.One;

            return new VertexPositionNormalTangent(
                position,
                ToVector3(attributes[MdlFile.VertexUsage.Normal]),
                bitangent
            );
        }

        throw new Exception($"Unknown geometry type {GeometryType}.");
    }

    /// <summary> Build a material vertex from a vertex's attributes. </summary>
    public IVertexMaterial BuildVertexMaterial(IReadOnlyDictionary<MdlFile.VertexUsage, object> attributes)
    {
        if (MaterialType == typeof(VertexEmpty))
            return new VertexEmpty();


        if (MaterialType == typeof(VertexTexture1))
            return new VertexTexture1(ToVector2(attributes[MdlFile.VertexUsage.UV]));

        
        // XIV packs two UVs into a single vec4 attribute.
        if (MaterialType == typeof(VertexTexture2))
        {
            var uv = ToVector4(attributes[MdlFile.VertexUsage.UV]);
            return new VertexTexture2(
                new Vector2(uv.X, uv.Y),
                new Vector2(uv.Z, uv.W)
            );
        }
        
        if (MaterialType == typeof(XivVertex))
        {
            if (UvCount == 0)
                return new XivVertex(ToVector4(attributes[MdlFile.VertexUsage.Color]));
            if (UvCount == 1)
                return new XivVertex(
                    ToVector4(attributes[MdlFile.VertexUsage.Color]),
                    ToVector2(attributes[MdlFile.VertexUsage.UV])
                );
            if (UvCount == 2)
            {
                var uv = ToVector4(attributes[MdlFile.VertexUsage.UV]);
                return new XivVertex(
                    ToVector4(attributes[MdlFile.VertexUsage.Color]),
                    new Vector2(uv.X, uv.Y),
                    new Vector2(uv.Z, uv.W)
                );
            }
        }

        throw new Exception($"Unknown material type {MaterialType}");
    }

    /// <summary> Convert a vertex attribute value to a Vector2. Supported inputs are Vector2, Vector3, and Vector4. </summary>
    private static Vector2 ToVector2(object data)
        => data switch
        {
            Vector2 v2 => v2,
            Vector3 v3 => new Vector2(v3.X, v3.Y),
            Vector4 v4 => new Vector2(v4.X, v4.Y),
            _ => throw new ArgumentOutOfRangeException($"Invalid Vector2 input {data}"),
        };

    /// <summary> Convert a vertex attribute value to a Vector3. Supported inputs are Vector2, Vector3, and Vector4. </summary>
    private static Vector3 ToVector3(object data)
        => data switch
        {
            Vector2 v2 => new Vector3(v2.X, v2.Y, 0),
            Vector3 v3 => v3,
            Vector4 v4 => new Vector3(v4.X, v4.Y, v4.Z),
            _ => throw new ArgumentOutOfRangeException($"Invalid Vector3 input {data}"),
        };

    /// <summary> Convert a vertex attribute value to a Vector4. Supported inputs are Vector2, Vector3, and Vector4. </summary>
    private static Vector4 ToVector4(object data)
        => data switch
        {
            Vector2 v2 => new Vector4(v2.X, v2.Y, 0, 0),
            Vector3 v3 => new Vector4(v3.X, v3.Y, v3.Z, 1),
            Vector4 v4 => v4,
            _ => throw new ArgumentOutOfRangeException($"Invalid Vector3 input {data}"),
        };

    /// <summary> Convert a vertex attribute value to a byte array. </summary>
    private static byte[] ToByteArray(object data)
        => data switch
        {
            byte[] value => value,
            _ => throw new ArgumentOutOfRangeException($"Invalid byte[] input {data}"),
        };
}