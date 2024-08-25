using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.Models.Structs;

// Client::System::Resource::Handle::ModelResourceHandle
//   Client::System::Resource::Handle::ResourceHandle
//     Client::System::Common::NonCopyable
[StructLayout(LayoutKind.Explicit, Size = 0x260)]
public unsafe struct ModelResourceHandle
{
    [StructLayout(LayoutKind.Explicit, Size = 0x38)] 
    public struct ModelHeader
    {
        [FieldOffset(0x00)] public float Radius;
        [FieldOffset(0x04)] public ushort MeshCount;
        [FieldOffset(0x06)] public ushort AttributeCount;
        [FieldOffset(0x08)] public ushort SubmeshCount;
        [FieldOffset(0x0A)] public ushort MaterialCount;
        [FieldOffset(0x0C)] public ushort BoneCount;
        [FieldOffset(0x0E)] public ushort BoneTableCount;
        [FieldOffset(0x10)] public ushort ShapeCount;
        [FieldOffset(0x12)] public ushort ShapeMeshCount;
        [FieldOffset(0x14)] public ushort ShapeValueCount;
        [FieldOffset(0x16)] public byte LodCount;
        [FieldOffset(0x17)] public byte Flags1;
        [FieldOffset(0x18)] public ushort ElementIdCount;
        [FieldOffset(0x1A)] public byte TerrainShadowMeshCount;
        [FieldOffset(0x1B)] public byte Flags2;
        [FieldOffset(0x1C)] public float ModelClipOutDistance;
        [FieldOffset(0x20)] public float ShadowClipOutDistance;
        [FieldOffset(0x24)] public ushort Unknown4;
        [FieldOffset(0x26)] public ushort TerrainShadowSubmeshCount;
        [FieldOffset(0x28)] public byte Unknown5;
        [FieldOffset(0x29)] public byte BGChangeMaterialIndex;
        [FieldOffset(0x2A)] public byte BGCrestChangeMaterialIndex;
        [FieldOffset(0x2B)] public byte Unknown6;
        [FieldOffset(0x2C)] public ushort Unknown7;
        [FieldOffset(0x2E)] public ushort Unknown8;
        [FieldOffset(0x30)] public ushort Unknown9;
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 0x3C)] 
    public unsafe partial struct Lod {
        [FieldOffset(0x00)] public ushort MeshIndex;
        [FieldOffset(0x02)] public ushort MeshCount;
        [FieldOffset(0x04)] public float ModelLodRange;
        [FieldOffset(0x08)] public float TextureLodRange;
        [FieldOffset(0x0C)] public ushort WaterMeshIndex;
        [FieldOffset(0x0E)] public ushort WaterMeshCount;
        [FieldOffset(0x10)] public ushort ShadowMeshIndex;
        [FieldOffset(0x12)] public ushort ShadowMeshCount;
        [FieldOffset(0x14)] public ushort TerrainShadowMeshIndex;
        [FieldOffset(0x16)] public ushort TerrainShadowMeshCount;
        [FieldOffset(0x18)] public ushort VerticalFogMeshIndex;
        [FieldOffset(0x1A)] public ushort VerticalFogMeshCount;
        [FieldOffset(0x1C)] public uint EdgeGeometrySize;
        [FieldOffset(0x20)] public uint EdgeGeometryDataOffset;
        [FieldOffset(0x24)] public uint PolygonCount;
        [FieldOffset(0x28)] public uint Unknown1;
        [FieldOffset(0x2C)] public uint VertexBufferSize;
        [FieldOffset(0x30)] public uint IndexBufferSize;
        [FieldOffset(0x34)] public uint VertexDataOffset;
        [FieldOffset(0x38)] public uint IndexDataOffset;
    }
    
    public readonly struct ModelResourceHandleData
    {
        public readonly uint StringCount;
        public readonly uint StringTableSize;
        public readonly byte[] StringTableData;
        public readonly ModelHeader ModelHeader;
        
        public ModelResourceHandleData(Pointer<byte> ptr)
        {
            var offset = 0;
            StringCount = ptr.Read<uint>(ref offset);
            StringTableSize = ptr.Read<uint>(ref offset);
            StringTableData = ptr.ReadSpan<byte>((int)StringTableSize, ref offset).ToArray();
            ModelHeader = ptr.Read<ModelHeader>(ref offset);
        }
    } 

    [FieldOffset(0x00)] public ResourceHandle ResourceHandle;
    
    //public readonly ModelHeader* Header => (ModelHeader*)(StringTable + (((uint*)StringTable)[1] + 8));
    public readonly ModelResourceHandleData Data => new(StringTable);
    
    [FieldOffset(0xB0)] public uint FileVersion;
    [FieldOffset(0xB4)] public uint FileLength;
    [FieldOffset(0xC8)] public byte* StringTable; 
    [FieldOffset(0xD0)] public uint RuntimeSize;
    [FieldOffset(0xD4)] public uint FileLength2;
    // [FieldOffset(0xD8)] public ElementId* ElementIds;
    [FieldOffset(0xE0)] public Lod* Lods;
    //  public readonly ExtraLod* ExtraLods => (Header->Flags2 & 0x10) != 0 ? (ExtraLod*)(Lods + 3) : null;
    // [FieldOffset(0xE8)] public Mesh* Meshes;
    // [FieldOffset(0xF0)] public TerrainShadowMesh* TerrainShadowMeshes;
    [FieldOffset(0xF8)] public uint* AttributeNameOffsets;
    // [FieldOffset(0x100)] public Submesh* Submeshes;
    // [FieldOffset(0x108)] public TerrainShadowSubmesh* TerrainShadowSubmeshes;
    [FieldOffset(0x110)] public uint* MaterialNameOffsets;
    [FieldOffset(0x118)] public uint* BoneNameOffsets;
    // [FieldOffset(0x120)] public BoneTable* BoneTables;
    // [FieldOffset(0x128)] public Shape* ShapesPtr;
    // [FieldOffset(0x130)] public ShapeMesh* ShapeMeshes;
    // [FieldOffset(0x138)] public ShapeValue* ShapeValues;
    [FieldOffset(0x140)] public uint* SubmeshBoneMapByteSize;
    [FieldOffset(0x148)] public ushort* SubmeshBoneMap;
    // [FieldOffset(0x150)] public BoundingBox* BoundingBoxes;
    // [FieldOffset(0x158)] public BoundingBox* ModelBoundingBoxes;
    // [FieldOffset(0x160)] public BoundingBox* WaterBoundingBoxes;
    // [FieldOffset(0x168)] public BoundingBox* VerticalFogBoundingBoxes;
    // [FieldOffset(0x170)] public BoundingBox* BoneBoundingBoxes;
    // [FieldOffset(0x178)] public byte* Unknown4Data;
    // [FieldOffset(0x180)] public Graphics.Kernel.VertexDeclaration** KernelVertexDeclarations;
    // [FixedSizeArray<Pointer<VertexBuffer>>(3)]
    // [FieldOffset(0x188)] public fixed byte VertexBuffers[3 * 8];
    // [FixedSizeArray<Pointer<IndexBuffer>>(3)]
    // [FieldOffset(0x1A0)] public fixed byte IndexBuffers[3 * 8];
    // [FixedSizeArray<nint>(3)]
    // [FieldOffset(0x1D0)] public fixed byte IndexData[3 * 8];
    
    [FieldOffset(0x208)] public StdMap<Pointer<byte>, short> Attributes;
    [FieldOffset(0x218)] public StdMap<Pointer<byte>, short> BoneNames;
    [FieldOffset(0x228)] public StdMap<Pointer<byte>, short> Shapes;

    
    [FieldOffset(0x244)] public fixed float MeshLodRanges[4];
    [FieldOffset(0x254)] public float TextureLodRange;
    [FieldOffset(0x258)] public byte LodCount;
    [FieldOffset(0x259)] public byte LodCountMinus1;
    
    public Span<Lod> LodSpan => new(Lods, Data.ModelHeader.LodCount);
    
    public string GetMaterialFileName(uint idx)
    {
        if (idx >= Data.ModelHeader.MaterialCount)
            throw new ArgumentOutOfRangeException(nameof(idx));
 
        var offset = MaterialNameOffsets[idx];
        var dataSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(StringTable + offset + 8);
        return Encoding.UTF8.GetString(dataSpan);
    }
    
    // [MemberFunction("E8 ?? ?? ?? ?? 44 8B CD 48 89 44 24")]
    // public readonly unsafe partial byte* GetMaterialFileNameBySlot(uint slot);
    //
    // public readonly unsafe ReadOnlySpan<byte> GetMaterialFileNameBySlotAsSpan(uint slot)
    //     => MemoryMarshal.CreateReadOnlySpanFromNullTerminated(GetMaterialFileNameBySlot(slot));
    //
    // public readonly string GetMaterialFileNameBySlotAsString(uint slot)
    //     => Encoding.UTF8.GetString(GetMaterialFileNameBySlotAsSpan(slot));
}
