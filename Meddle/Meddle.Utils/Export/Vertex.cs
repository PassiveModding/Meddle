using System.Numerics;
using System.Text.Json.Serialization;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace Meddle.Utils.Export;

public unsafe struct Vertex
{
    // VertexType and VertexUsage are as-defined in the .mdl file
    // These get changed into some intermediate other enum
    // When passed onto DX11, the intermediate enum get converted to their real values

    public enum VertexType : byte
    {
        // 0 => 33 => DXGI_FORMAT_R32_FLOAT
        // 1 => 34 => DXGI_FORMAT_R32G32_FLOAT
        Single3 = 2, // => 35 => DXGI_FORMAT_R32G32B32_FLOAT
        Single4 = 3, // => 36 => DXGI_FORMAT_R32G32B32A32_FLOAT
        // 4 Doesn't exist!
        UInt = 5, // => 116 => DXGI_FORMAT_R8G8B8A8_UINT
        // 6 => 82 => DXGI_FORMAT_R16G16_SINT
        // 7 => 84 => DXGI_FORMAT_R16G16B16A16_SINT
        ByteFloat4 = 8, // => 68 => DXGI_FORMAT_R8G8B8A8_UNORM
        // 9 => 18 => DXGI_FORMAT_R16G16_SNORM
        // 10 => 20 => DXGI_FORMAT_R16G16B16A16_SNORM
        // 11 Doesn't exist!
        // 12 Doesn't exist!
        Half2 = 13, // => 50 => DXGI_FORMAT_R16G16_FLOAT
        Half4 = 14,  // => 52 => DXGI_FORMAT_R16G16B16A16_FLOAT
        // 15 => 97 => ?? (doesn't exist, so it resolves to DXGI_FORMAT_UNKNOWN)

        UShort4 = 17 // 8 byte array for bone weights/bone indexes; 0,4,1,5,2,6,3,7
    }

    public enum VertexUsage : byte
    {
        Position = 0,     // => 0 => POSITION0
        BlendWeights = 1, // => 1 => BLENDWEIGHT0
        BlendIndices = 2, // => 7 => BLENDINDICES0
        Normal = 3,       // => 2 => NORMAL0
        UV = 4,           // => (UsageIndex +) 8 => TEXCOORD0
        Tangent2 = 5,     // => 14 => TANGENT0
        Tangent1 = 6,     // => 15 => BINORMAL0
        Color = 7,        // (UsageIndex +) 3 => COLOR0
    }

    public Vector4? Position;
    public Vector4? BlendWeights;
    [JsonIgnore]
    public fixed byte BlendIndicesData[4];
    public Vector3? Normal;
    public Vector4? UV;
    public Vector4? Color;
    public Vector4? Tangent2;
    public Vector4? Tangent1;

    [JsonIgnore]
    public Span<byte> BlendIndices
    {
        get
        {
            fixed (byte* buffer = BlendIndicesData)
                return new(buffer, 4);
        }
    }

    private static class VertexItem
    {
        private static float FromSNorm(short value) =>
            value != short.MinValue ?
                value / 32767.0f :
                -1.0f;

        private static float FromUNorm(byte value) =>
            value / 255.0f;
        
        private static float FromUShort(ushort value) =>
            value / 65535.0f;
        
        public static object GetElement(ReadOnlySpan<byte> buffer, VertexType type)
        {
            fixed (byte* b = buffer)
            {
                var s = (short*)b;
                var us = (ushort*)b;
                var h = (Half*)b;
                var f = (float*)b;

                return type switch
                {
                    VertexType.Single3 => new Vector3(f[0], f[1], f[2]),
                    VertexType.Single4 => new Vector4(f[0], f[1], f[2], f[3]),
                    VertexType.UInt => new Vector4(b[0], b[1], b[2], b[3]),
                    VertexType.ByteFloat4 => new Vector4(FromUNorm(b[0]), FromUNorm(b[1]), FromUNorm(b[2]), FromUNorm(b[3])),
                    VertexType.Half2 => new Vector2((float)h[0], (float)h[1]),
                    VertexType.Half4 => new Vector4((float)h[0], (float)h[1], (float)h[2], (float)h[3]),
                    VertexType.UShort4 => new Vector4(FromUShort(us[0]), FromUShort(us[1]), FromUShort(us[2]), FromUShort(us[3])),
                    _ => throw new ArgumentException($"Unknown type {type}"),
                };
            }
        }

        public static T ConvertTo<T>(object value) where T : struct
        {
            if (value is T t)
                return t;
            if (value is Vector2 v2)
            {
                if (typeof(T) == typeof(Vector3))
                    return (T)(object)new Vector3(v2, 0);
                if (typeof(T) == typeof(Vector4))
                    return (T)(object)new Vector4(v2, 0, 0);
            }
            if (value is Vector3 v3)
            {
                if (typeof(T) == typeof(Vector2))
                    return (T)(object)new Vector2(v3.X, v3.Y);
                if (typeof(T) == typeof(Vector4))
                    return (T)(object)new Vector4(v3, 0);
            }
            if (value is Vector4 v4)
            {
                if (typeof(T) == typeof(Vector2))
                    return (T)(object)new Vector2(v4.X, v4.Y);
                if (typeof(T) == typeof(Vector3))
                    return (T)(object)new Vector3(v4.X, v4.Y, v4.Z);
            }
            throw new ArgumentException($"Cannot convert {value} to {typeof(T)}");
        }
    }

    public static void Apply(ref Vertex vertex, ReadOnlySpan<byte> buffer, ModelResourceHandle.VertexElement element)
    {
        var item = VertexItem.GetElement(buffer[element.Offset..], (VertexType)element.Type);
        switch ((VertexUsage)element.Usage)
        {
            case VertexUsage.Position:
                vertex.Position = VertexItem.ConvertTo<Vector4>(item);
                break;
            case VertexUsage.BlendWeights:
                vertex.BlendWeights = VertexItem.ConvertTo<Vector4>(item);
                break;
            case VertexUsage.BlendIndices:
                var itemVector = VertexItem.ConvertTo<Vector4>(item);
                for (var j = 0; j < 4; ++j)
                    vertex.BlendIndices[j] = (byte)itemVector[j];
                break;
            case VertexUsage.Normal:
                vertex.Normal = VertexItem.ConvertTo<Vector3>(item);
                break;
            case VertexUsage.UV:
                vertex.UV = VertexItem.ConvertTo<Vector4>(item);
                break;
            case VertexUsage.Color:
                vertex.Color = VertexItem.ConvertTo<Vector4>(item);
                break;
            case VertexUsage.Tangent2:
                vertex.Tangent2 = VertexItem.ConvertTo<Vector4>(item);
                break;
            case VertexUsage.Tangent1:
                vertex.Tangent1 = VertexItem.ConvertTo<Vector4>(item);
                break;
            default:
                Console.WriteLine($"Skipped usage {element.Usage} [{element.Type}] = {item}");
                break;
        }
    }
}
