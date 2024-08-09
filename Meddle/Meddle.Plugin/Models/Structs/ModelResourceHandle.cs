using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;

namespace Meddle.Plugin.Models.Structs;

// Client::System::Resource::Handle::ModelResourceHandle
//   Client::System::Resource::Handle::ResourceHandle
//     Client::System::Common::NonCopyable
[StructLayout(LayoutKind.Explicit, Size = 0x260)]
public unsafe struct ModelResourceHandle
{
    [StructLayout(LayoutKind.Sequential, Size = 56)]
    public struct ModelHeader
    {
        public float Radius;
        public ushort MeshCount;
        public ushort AttributeCount;
        public ushort SubmeshCount;
        public ushort MaterialCount;
        public ushort BoneCount;
        public ushort BoneTableCount;
        public ushort ShapeCount;
        public ushort ShapeMeshCount;
        public ushort ShapeValueCount;
        public byte LodCount;
        public byte Flags1;
        public ushort ElementIdCount;
        public byte TerrainShadowMeshCount;
        public byte Flags2;
        public float ModelClipOutDistance;
        public float ShadowClipOutDistance;
        public ushort Unknown4;
        public ushort TerrainShadowSubmeshCount;
        public byte Unknown5;
        public byte BGChangeMaterialIndex;
        public byte BGCrestChangeMaterialIndex;
        public byte Unknown6;
        public ushort Unknown7;
        public ushort Unknown8;
        public ushort Unknown9;
    }

    public readonly ModelHeader* Header => (ModelHeader*)(StringTable + (((uint*)StringTable)[1] + 8));

    // uint* StringTable[0] = StringCount
    // uint* StringTable[1] = StringTableSize
    [FieldOffset(0xC8)]
    public byte* StringTable;

    [FieldOffset(0xF8)]
    public uint* AttributeNameOffsets;

    [FieldOffset(0x110)]
    public uint* MaterialNameOffsets;

    [FieldOffset(0x118)]
    public uint* BoneNameOffsets;

    [FieldOffset(0x208)]
    public StdMap<Pointer<byte>, short> Attributes;

    [FieldOffset(0x228)]
    public StdMap<Pointer<byte>, short> Shapes;

    public string GetMaterialFileName(uint idx)
    {
        if (idx >= Header->MaterialCount)
            throw new ArgumentOutOfRangeException(nameof(idx));

        var offset = MaterialNameOffsets[idx];
        var dataSpan = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(StringTable + offset + 8);
        return Encoding.UTF8.GetString(dataSpan);
    }
}
