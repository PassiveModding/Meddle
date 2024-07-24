using System.Numerics;
using System.Runtime.InteropServices;
using Meddle.Utils.Files.Structs.Model;

namespace Meddle.Utils.Export;

public unsafe struct Vertex
{
    public enum VertexType : byte
    {
        Single3 = 2,
        Single4 = 3,
        UInt = 5,
        ByteFloat4 = 8,
        Half2 = 13,
        Half4 = 14,
        UByte8 = 17 // 8 byte array for bone weights/bone indexes; 0,4,1,5,2,6,3,7
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

    public Vector3? Position;
    public float[]? BlendWeights;
    public byte[]? BlendIndices;
    public Vector3? Normal;
    public Vector4? UV;
    public Vector4? Color;
    public Vector4? Tangent2;
    public Vector4? Tangent1;

    private static class VertexItem
    {
        private static float FromSNorm(short value) =>
            value != short.MinValue ?
                value / 32767.0f :
                -1.0f;
        
        private static float FromUNorm(byte value) =>
            value / 255.0f;

        public static Vector4 HandleUshort4(ReadOnlySpan<byte> buffer)
        {
            var byteValues = new byte[8];
            byteValues[0] = buffer[0];
            byteValues[4] = buffer[1];
            byteValues[1] = buffer[2];
            byteValues[5] = buffer[3];
            byteValues[2] = buffer[4];
            byteValues[6] = buffer[5];
            byteValues[3] = buffer[6];
            byteValues[7] = buffer[7];
            var us = MemoryMarshal.Cast<byte, ushort>(byteValues);
            return new Vector4(us[0], us[1], us[2], us[3]);
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
    
    public static Vector3 ReadVector3(ReadOnlySpan<byte> buffer, VertexType type)
    {
        fixed (byte* b = buffer)
        {
            var h = (Half*)b;
            var f = (float*)b;

            return type switch
            {
                VertexType.Single3 => new Vector3(f[0], f[1], f[2]),
                VertexType.Single4 => new Vector3(f[0], f[1], f[2]), // skip W
                VertexType.Half4 => new Vector3((float)h[0], (float)h[1], (float)h[2]), // skip W
                _ => throw new ArgumentException($"Unsupported vector3 type {type}")
            };
        }
    }
    
    public static float[] ReadFloatArray(ReadOnlySpan<byte> buffer, VertexType type)
    {
        fixed (byte* b = buffer)
        {
            byte[] byteBuffer = type switch
            {
                VertexType.UInt => [b[0], b[1], b[2], b[3]],
                VertexType.ByteFloat4 => [b[0], b[1], b[2], b[3]],
                VertexType.UByte8 => [b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7]],
                _ => throw new ArgumentException($"Unsupported float array type {type}")
            };

            var fb = new float[byteBuffer.Length];
            for (var i = 0; i < byteBuffer.Length; ++i)
                fb[i] = byteBuffer[i] / 255.0f;
            return fb;
        }
    }
    
    public static byte[] ReadByteArray(ReadOnlySpan<byte> buffer, VertexType type)
    {
        fixed (byte* b = buffer)
        {
            return type switch
            {
                VertexType.UInt => [b[0], b[1], b[2], b[3]],
                VertexType.UByte8 => [b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7]],
                _ => throw new ArgumentException($"Unsupported byte array type {type}")
            };
        }
    }

    public static Vector4 ReadVector4(ReadOnlySpan<byte> buffer, VertexType type)
    {
        fixed (byte* b = buffer)
        {
            var h = (Half*)b;
            var f = (float*)b;

            return type switch
            {
                VertexType.ByteFloat4 => new Vector4(FromUNorm(b[0]), FromUNorm(b[1]), FromUNorm(b[2]), FromUNorm(b[3])),
                VertexType.Single4 => new Vector4(f[0], f[1], f[2], f[3]),
                VertexType.Half2 => new Vector4((float)h[0], (float)h[1], 0, 0),
                VertexType.Half4 => new Vector4((float)h[0], (float)h[1], (float)h[2], (float)h[3]),
                _ => throw new ArgumentException($"Unsupported vector4 type {type}")
            };
        }
    }
    
    public static void Apply(ref Vertex vertex, ReadOnlySpan<byte> buffer, VertexElement element)
    {
        var buf = buffer[element.Offset..];
        switch ((VertexUsage)element.Usage)
        {
            case VertexUsage.Position:
                vertex.Position = ReadVector3(buf, (VertexType)element.Type);
                break;
            case VertexUsage.BlendWeights:
                vertex.BlendWeights = ReadFloatArray(buf, (VertexType)element.Type);
                break;
            case VertexUsage.BlendIndices:
                vertex.BlendIndices = ReadByteArray(buf, (VertexType)element.Type);
                break;
            case VertexUsage.Normal:
                vertex.Normal = ReadVector3(buf, (VertexType)element.Type);
                break;
            case VertexUsage.UV:
                vertex.UV = ReadVector4(buf, (VertexType)element.Type);
                break;
            case VertexUsage.Color:
                vertex.Color = ReadVector4(buf, (VertexType)element.Type);
                break;
            case VertexUsage.Tangent2:
                vertex.Tangent2 = ReadVector4(buf, (VertexType)element.Type);
                break;
            case VertexUsage.Tangent1:
                vertex.Tangent1 = ReadVector4(buf, (VertexType)element.Type);
                break;
            default:
                Console.WriteLine($"Skipped usage {element.Usage} [{element.Type}]");
                break;
        }
    }
    
    private static float FromUNorm(byte value) =>
        value / 255.0f;
}
