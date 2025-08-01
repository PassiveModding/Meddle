﻿using System.Numerics;
using Meddle.Utils.Files.Structs.Model;

namespace Meddle.Utils.Export;

public unsafe struct Vertex
{
    public enum VertexType : byte
    {
        Single1 = 0,
        Single2 = 1,
        Single3 = 2,
        Single4 = 3,
        UInt = 5,
        ByteFloat4 = 8,
        Half2 = 13,
        Half4 = 14,
        UByte8 = 17
    }

    public enum VertexUsage : byte
    {
        Position = 0,     // => 0 => POSITION0
        BlendWeights = 1, // => 1 => BLENDWEIGHT0
        BlendIndices = 2, // => 7 => BLENDINDICES0
        Normal = 3,       // => 2 => NORMAL0
        TexCoord = 4,     // => (UsageIndex +) 8 => TEXCOORD0
        Flow = 5,         // => 14 => TANGENT0
        Binormal = 6,     // => 15 => BINORMAL0
        Color = 7,        // (UsageIndex +) 3 => COLOR0
    }

    public Vector3? Position;
    public float[]? BlendWeights;
    public byte[]? BlendIndices;
    public Vector3? Normal;
    public Vector4? TexCoord;
    public Vector4? TexCoord2; // only using X/Y afaik
    public Vector4? Color;
    public Vector4? Color2;
    public Vector4? Flow;
    public Vector4? Binormal;
    
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
                VertexType.Single1 => new Vector3(f[0], f[0], f[0]),
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
                VertexType.Single2 => new Vector4(f[0], f[1], 0, 0),
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
            case VertexUsage.TexCoord when element.UsageIndex == 0:
                vertex.TexCoord = ReadVector4(buf, (VertexType)element.Type);
                break;
            case VertexUsage.TexCoord when element.UsageIndex != 0:
                vertex.TexCoord2 = ReadVector4(buf, (VertexType)element.Type);
                break;
            case VertexUsage.Color when element.UsageIndex == 0:
                vertex.Color = ReadVector4(buf, (VertexType)element.Type);
                break;
            case VertexUsage.Color when element.UsageIndex != 0:
                vertex.Color2 = ReadVector4(buf, (VertexType)element.Type);
                break;
            case VertexUsage.Flow:
                vertex.Flow = ReadVector4(buf, (VertexType)element.Type);
                break;
            case VertexUsage.Binormal:
                vertex.Binormal = ReadVector4(buf, (VertexType)element.Type);
                break;
            default:
                throw new Exception($"Unsupported usage {element.Usage} [{element.Type}]");
        }
    }
    
    private static float FromUNorm(byte value) =>
        value / 255.0f;
}
