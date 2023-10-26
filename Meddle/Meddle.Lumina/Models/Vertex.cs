using System.Numerics;

namespace Meddle.Lumina.Models;

public struct Vertex {

    public enum VertexType : byte {
        Single3 = 2,
        Single4 = 3,
        UInt = 5,
        ByteFloat4 = 8,
        Half2 = 13,
        Half4 = 14
    }

    public enum VertexUsage : byte {
        Position = 0,
        BlendWeights = 1,
        BlendIndices = 2,
        Normal = 3,
        UV = 4,
        Tangent2 = 5,
        Tangent1 = 6,
        Color = 7,
    }

    public Vector4? Position;
    public Vector4? BlendWeights;
    public byte[] BlendIndices;
    public Vector3? Normal;
    public Vector4? UV;
    public Vector4? Color;
    public Vector4? Tangent2;
    public Vector4? Tangent1;
}