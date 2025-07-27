using System.Runtime.InteropServices;

namespace Meddle.Utils.Files.Structs.Model;

/*[GenerateInterop]
[StructLayout(LayoutKind.Explicit, Size = 32)]
public partial struct ElementId {
    [FieldOffset(0)] public uint Id;
    [FieldOffset(4)] public uint ParentBoneName;
    [FieldOffset(8), FixedSizeArray] internal FixedSizeArray3<float> _translate;
    [FieldOffset(20), FixedSizeArray] internal FixedSizeArray3<float> _rotate;
}*/

[StructLayout(LayoutKind.Sequential, Size = 32)]
public unsafe struct ElementId
{
    public uint Id;
    public uint ParentBoneName;
    public fixed float Translate[3];
    public fixed float Rotate[3];
}

[StructLayout(LayoutKind.Sequential, Size = 60)]
public struct Lod {
    public ushort MeshIndex;
    public ushort MeshCount;

    public float ModelLodRange;
    public float TextureLodRange;

    public ushort WaterMeshIndex;
    public ushort WaterMeshCount;

    public ushort ShadowMeshIndex;
    public ushort ShadowMeshCount;

    public ushort TerrainShadowMeshIndex;
    public ushort TerrainShadowMeshCount;

    public ushort VerticalFogMeshIndex;
    public ushort VerticalFogMeshCount;

    public uint EdgeGeometrySize;
    public uint EdgeGeometryDataOffset;

    public uint PolygonCount;
    public uint Unknown1;

    public uint VertexBufferSize;
    public uint IndexBufferSize;

    public uint VertexDataOffset;
    public uint IndexDataOffset;
}

[StructLayout(LayoutKind.Sequential, Size = 40)]
public struct ExtraLod {
    public ushort LightShaftMeshIndex;
    public ushort LightShaftMeshCount;

    public ushort GlassMeshIndex;
    public ushort GlassMeshCount;

    public ushort MaterialChangeMeshIndex;
    public ushort MaterialChangeMeshCount;

    public ushort CrestChangeMeshIndex;
    public ushort CrestChangeMeshCount;

    public ushort Unknown1;
    public ushort Unknown2;
    public ushort Unknown3;
    public ushort Unknown4;
    public ushort Unknown5;
    public ushort Unknown6;
    public ushort Unknown7;
    public ushort Unknown8;
    public ushort Unknown9;
    public ushort Unknown10;
    public ushort Unknown11;
    public ushort Unknown12;
}

/*[GenerateInterop]
[StructLayout(LayoutKind.Explicit, Size = 36)]
public partial struct Mesh {
    [FieldOffset(0)] public ushort VertexCount;
    [FieldOffset(2)] public ushort Padding;
    [FieldOffset(4)] public uint IndexCount;
    [FieldOffset(8)] public ushort MaterialIndex;
    [FieldOffset(10)] public ushort SubMeshIndex;
    [FieldOffset(12)] public ushort SubMeshCount;
    [FieldOffset(14)] public ushort BoneTableIndex;
    [FieldOffset(16)] public uint StartIndex;
    [FieldOffset(20), FixedSizeArray]
    internal FixedSizeArray3<uint> _vertexBufferOffset;
    [FieldOffset(32), FixedSizeArray]
    internal FixedSizeArray3<byte> _vertexBufferStride;
    [FieldOffset(35)]
    public byte VertexStreamCount;
}*/

[StructLayout(LayoutKind.Sequential, Size = 36)]
public unsafe struct Mesh {
    public ushort VertexCount;
    public ushort Padding;
    public uint IndexCount;
    public ushort MaterialIndex;
    public ushort SubMeshIndex;
    public ushort SubMeshCount;
    public ushort BoneTableIndex;
    public uint StartIndex;
    public fixed uint VertexBufferOffset[3];
    public fixed byte VertexBufferStride[3];
    public byte VertexStreamCount;
}

[StructLayout(LayoutKind.Sequential, Size = 20)]
public struct TerrainShadowMesh {
    public uint IndexCount;
    public uint StartIndex;
    public uint VertexBufferOffset;
    public ushort VertexCount;
    public ushort SubMeshIndex;
    public ushort SubMeshCount;
    public byte VertexBufferStride;
}

[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct Submesh {
    public uint IndexOffset;
    public uint IndexCount;
    public uint AttributeIndexMask;
    public ushort BoneStartIndex;
    public ushort BoneCount;
}

[StructLayout(LayoutKind.Sequential, Size = 12)]
public struct TerrainShadowSubmesh {
    public uint IndexOffset;
    public uint IndexCount;
    public ushort Unknown1;
    public ushort Unknown2;
}

/*[GenerateInterop]
[StructLayout(LayoutKind.Explicit, Size = 132)]
public partial struct BoneTable {
    [FieldOffset(0), FixedSizeArray]
    internal FixedSizeArray64<ushort> _boneIndex;
    [FieldOffset(128)]
    public byte BoneCount;
}*/

[StructLayout(LayoutKind.Sequential, Size = 132)]
public unsafe struct BoneTable {
    public fixed ushort BoneIndex[64];
    public byte BoneCount;
}

/*[GenerateInterop]
[StructLayout(LayoutKind.Explicit, Size = 16)]
public partial struct Shape {
    [FieldOffset(0)] public uint StringOffset;
    [FieldOffset(4), FixedSizeArray]
    internal FixedSizeArray3<ushort> _shapeMeshStartIndex;
    [FieldOffset(10), FixedSizeArray]
    internal FixedSizeArray3<ushort> _shapeMeshCount;
}*/

[StructLayout(LayoutKind.Sequential, Size = 16)]
public unsafe struct Shape {
    public uint StringOffset;
    public fixed ushort ShapeMeshStartIndex[3];
    public fixed ushort ShapeMeshCount[3];
}

[StructLayout(LayoutKind.Sequential, Size = 12)]
public struct ShapeMesh {
    public uint MeshIndexOffset;
    public uint ShapeValueCount;
    public uint ShapeValueOffset;
}

[StructLayout(LayoutKind.Sequential, Size = 4)]
public struct ShapeValue {
    public ushort BaseIndicesIndex;
    public ushort ReplacingVertexIndex;
}

/*[GenerateInterop, StructLayout(LayoutKind.Explicit, Size = 32)]
public partial struct BoundingBox {
    [FieldOffset(0), FixedSizeArray]
    internal FixedSizeArray4<float> _min;
    [FieldOffset(16), FixedSizeArray]
    internal FixedSizeArray4<float> _max;
}*/

[StructLayout(LayoutKind.Sequential, Size = 32)]
public unsafe struct BoundingBox {
    public fixed float Min[4];
    public fixed float Max[4];
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public struct VertexElement {
    public byte Stream;
    public byte Offset; 
    public byte Type;
    public byte Usage;
    public byte UsageIndex;
}

/*[GenerateInterop]
[StructLayout(LayoutKind.Explicit, Size = 17 * 8)]
public unsafe partial struct VertexDeclaration {
    [FieldOffset(0), FixedSizeArray]
    internal FixedSizeArray17<VertexElement> _elements;
}*/

[StructLayout(LayoutKind.Sequential, Size = 17 * 8)]
public unsafe struct VertexDeclaration {
    public fixed byte Elements[17 * 8];
    
    public Span<VertexElement> GetElements()
    {
        fixed (byte* ptr = Elements)
        {
            return new Span<VertexElement>(ptr, 17);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Size = 56)]
public struct ModelHeader {
    public float Radius; // 0x0
    public ushort MeshCount; // 0x4
    public ushort AttributeCount; // 0x6
    public ushort SubmeshCount; // 0x8
    public ushort MaterialCount; // 0xA
    public ushort BoneCount; // 0xC
    public ushort BoneTableCount; // 0xE
    public ushort ShapeCount; // 0x10
    public ushort ShapeMeshCount; // 0x12
    public ushort ShapeValueCount; // 0x14
    public byte LodCount; // 0x16
    public byte Flags1; // 0x17
    public ushort ElementIdCount; // 0x18
    public byte TerrainShadowMeshCount; // 0x1A
    public byte Flags2; // 0x1B
    public float ModelClipOutDistance; // 0x1C
    public float ShadowClipOutDistance; // 0x20
    public ushort Unknown4; // 0x24
    public ushort TerrainShadowSubmeshCount; // 0x26
    public byte Flags3; // 0x28
    public byte BGChangeMaterialIndex; // 0x29
    public byte BGCrestChangeMaterialIndex; // 0x2A
    public byte Unknown6; // 0x2B
    public ushort BoneTableArrayCountTotal; // 0x2C
    public ushort Unknown8; // 0x2E
    public ushort Unknown9; // 0x30
}
