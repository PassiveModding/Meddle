using System.Numerics;
using System.Text.Json.Serialization;
using Dalamud.Logging;

namespace Meddle.Plugin.Models;

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
        Half4 = 14  // => 52 => DXGI_FORMAT_R16G16B16A16_FLOAT
        // 15 => 97 => ?? (doesn't exist, so it resolves to DXGI_FORMAT_UNKNOWN)
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
    
    public enum KernelVertexType : byte
    {
        DXGI_FORMAT_R16G16_SNORM = 18,
        DXGI_FORMAT_R16G16B16A16_SNORM = 20,
        DXGI_FORMAT_R32_FLOAT = 33,
        DXGI_FORMAT_R32G32_FLOAT = 34,
        DXGI_FORMAT_R32G32B32_FLOAT = 35,
        DXGI_FORMAT_R32G32B32A32_FLOAT = 36,
        DXGI_FORMAT_R16G16_FLOAT = 50,
        DXGI_FORMAT_R16G16B16A16_FLOAT = 52,
        DXGI_FORMAT_R8G8B8A8_UNORM = 68,
        DXGI_FORMAT_R16G16_SINT = 82,
        DXGI_FORMAT_R16G16B16A16_SINT = 84,
        DXGI_FORMAT_R8G8B8A8_UINT = 116
    }

    public enum KernelVertexUsage : byte
    {
        POSITION0,
        BLENDWEIGHT0,
        NORMAL0,
        COLOR0,
        COLOR1,
        FOG0,
        PSIZE0,
        BLENDINDICES0,
        TEXCOORD0,
        TEXCOORD1,
        TEXCOORD2,
        TEXCOORD3,
        TEXCOORD4,
        TEXCOORD5,
        TANGENT0,
        BINORMAL0,
        DEPTH0,
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

    [JsonPropertyName("BlendIndices")]
    public int[] BlendIndicesArray => BlendIndices.ToArray().Select(i => (int)i).ToArray();

    public Vertex(Lumina.Models.Models.Vertex vertex)
    {
        Position = vertex.Position;
        BlendWeights = vertex.BlendWeights;
        for (var i = 0; i < 4; ++i)
            BlendIndices[i] = vertex.BlendIndices[i];
        Normal = vertex.Normal;
        UV = vertex.UV;
        Color = vertex.Color;
        Tangent2 = vertex.Tangent2;
        Tangent1 = vertex.Tangent1;
    }

    private static class VertexItem
    {
        private static float FromSNorm(short value) =>
            value != short.MinValue ?
                value / 32767.0f :
                -1.0f;

        private static float FromUNorm(byte value) =>
            value / 255.0f;

        public static object GetElement(ReadOnlySpan<byte> buffer, KernelVertexType type)
        {
            fixed (byte* b = buffer)
            {
                var s = (short*)b;
                var h = (Half*)b;
                var f = (float*)b;

                return type switch
                {
                    KernelVertexType.DXGI_FORMAT_R16G16_SNORM => new Vector2(FromSNorm(s[0]), FromSNorm(s[1])),
                    KernelVertexType.DXGI_FORMAT_R16G16B16A16_SNORM => new Vector4(FromSNorm(s[0]), FromSNorm(s[1]), FromSNorm(s[2]), FromSNorm(s[3])),
                    KernelVertexType.DXGI_FORMAT_R32_FLOAT => f[0],
                    KernelVertexType.DXGI_FORMAT_R32G32_FLOAT => new Vector2(f[0], f[1]),
                    KernelVertexType.DXGI_FORMAT_R32G32B32_FLOAT => new Vector3(f[0], f[1], f[2]),
                    KernelVertexType.DXGI_FORMAT_R32G32B32A32_FLOAT => new Vector4(f[0], f[1], f[2], f[3]),
                    KernelVertexType.DXGI_FORMAT_R16G16_FLOAT => new Vector2((float)h[0], (float)h[1]),
                    KernelVertexType.DXGI_FORMAT_R16G16B16A16_FLOAT => new Vector4((float)h[0], (float)h[1], (float)h[2], (float)h[3]),
                    KernelVertexType.DXGI_FORMAT_R8G8B8A8_UNORM => new Vector4(FromUNorm(b[0]), FromUNorm(b[1]), FromUNorm(b[2]), FromUNorm(b[3])),
                    KernelVertexType.DXGI_FORMAT_R16G16_SINT => new Vector2(s[0], s[1]),
                    KernelVertexType.DXGI_FORMAT_R16G16B16A16_SINT => new Vector4(s[0], s[1], s[2], s[3]),
                    KernelVertexType.DXGI_FORMAT_R8G8B8A8_UINT => new Vector4(b[0], b[1], b[2], b[3]),
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

    public static void Apply(Vertex[] vertices, ReadOnlySpan<byte> buffer, FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.VertexElement element, byte stride)
    {
        for (var i = 0; i < vertices.Length; ++i)
        {
            ref var vert = ref vertices[i];
            var buf = buffer.Slice(i * stride, stride);

            var item = VertexItem.GetElement(buf[element.Offset..], (KernelVertexType)element.Type);

            switch ((KernelVertexUsage)element.Usage)
            {
                case KernelVertexUsage.POSITION0:
                    vert.Position = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.BLENDWEIGHT0:
                    vert.BlendWeights = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.BLENDINDICES0:
                    var itemVector = VertexItem.ConvertTo<Vector4>(item);
                    for (var j = 0; j < 4; ++j)
                        vert.BlendIndices[j] = (byte)itemVector[j];
                    break;
                case KernelVertexUsage.NORMAL0:
                    vert.Normal = VertexItem.ConvertTo<Vector3>(item);
                    break;
                case KernelVertexUsage.TEXCOORD0:
                    vert.UV = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.COLOR0:
                    vert.Color = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.TANGENT0:
                    vert.Tangent2 = VertexItem.ConvertTo<Vector4>(item);
                    break;
                case KernelVertexUsage.BINORMAL0:
                    vert.Tangent1 = VertexItem.ConvertTo<Vector4>(item);
                    break;
                default:
                    PluginLog.Error($"Skipped usage {(KernelVertexUsage)element.Usage} [{(KernelVertexType)element.Type}] = {item}");
                    break;
            }
        }
    }
}
