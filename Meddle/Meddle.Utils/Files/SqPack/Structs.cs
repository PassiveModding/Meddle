using System.Runtime.InteropServices;

namespace Meddle.Utils.Files.SqPack;

public enum FileType : uint
{
    Empty = 1,
    Standard = 2,
    Model = 3,
    Texture = 4,
}

public enum LodLevel
{
    All = -1,
    Highest,
    High,
    Low,
    Max = 3
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SqPackFileInfo
{
    public uint Size;
    public FileType Type;
    public uint RawFileSize;
    public fixed uint __unknown[2];
    public uint NumberOfBlocks;
}

[StructLayout(LayoutKind.Sequential)]
public struct DatStdFileBlockInfos
{
    public uint Offset;
    public ushort CompressedSize;
    public ushort UncompressedSize;
};

public enum DatBlockType : uint
{
    Compressed = 16000,
    Uncompressed = 32000,
}

[StructLayout(LayoutKind.Sequential)]
struct DatBlockHeader
{
    public uint Size;

    // always 0?
    public uint unknown1;
    public DatBlockType DatBlockType;
    public uint BlockDataSize;
};

[StructLayout(LayoutKind.Sequential)]
struct LodBlock
{
    public uint CompressedOffset;
    public uint CompressedSize;
    public uint DecompressedSize;
    public uint BlockOffset;
    public uint BlockCount;
}

[StructLayout(LayoutKind.Sequential)]
public struct ReferenceBlockRange
{
    public uint Begin;
    public uint End;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ModelBlock
{
    public uint Size;
    public FileType Type;
    public uint RawFileSize;
    public uint NumberOfBlocks;
    public uint UsedNumberOfBlocks;
    public uint Version;
    public uint StackSize;
    public uint RuntimeSize;
    public fixed uint VertexBufferSize[(int)LodLevel.Max];
    public fixed uint EdgeGeometryVertexBufferSize[(int)LodLevel.Max];
    public fixed uint IndexBufferSize[(int)LodLevel.Max];
    public uint CompressedStackMemorySize;
    public uint CompressedRuntimeMemorySize;
    public fixed uint CompressedVertexBufferSize[(int)LodLevel.Max];
    public fixed uint CompressedEdgeGeometryVertexBufferSize[(int)LodLevel.Max];
    public fixed uint CompressedIndexBufferSize[(int)LodLevel.Max];
    public uint StackOffset;
    public uint RuntimeOffset;
    public fixed uint VertexBufferOffset[(int)LodLevel.Max];
    public fixed uint EdgeGeometryVertexBufferOffset[(int)LodLevel.Max];
    public fixed uint IndexBufferOffset[(int)LodLevel.Max];
    public ushort StackBlockIndex;
    public ushort RuntimeBlockIndex;
    public fixed ushort VertexBufferBlockIndex[(int)LodLevel.Max];
    public fixed ushort EdgeGeometryVertexBufferBlockIndex[(int)LodLevel.Max];
    public fixed ushort IndexBufferBlockIndex[(int)LodLevel.Max];
    public ushort StackBlockNum;
    public ushort RuntimeBlockNum;
    public fixed ushort VertexBufferBlockNum[(int)LodLevel.Max];
    public fixed ushort EdgeGeometryVertexBufferBlockNum[(int)LodLevel.Max];
    public fixed ushort IndexBufferBlockNum[(int)LodLevel.Max];
    public ushort VertexDeclarationNum;
    public ushort MaterialNum;
    public byte NumLods;
    public bool IndexBufferStreamingEnabled;
    public bool EdgeGeometryEnabled;
    public byte Padding;
};

public enum PlatformId : byte
{
    Win32,
    PS3, // obsolete now but uses big endian which I'm not going to support
    PS4
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SqPackHeader
{
    public fixed byte magic[8];
    public byte platformId;
    public fixed byte __unknown[3];
    public uint size;
    public uint version;
    public uint type;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SqPackIndexHeader
{
    public uint Size;
    public uint Version;
    public uint IndexDataOffset;
    public uint IndexDataSize;
    public fixed byte IndexDataHash[64];
    public uint DataFileCount;
    public uint SynonymDataOffset;
    public uint SynonymDataSize;
    public fixed byte SynonymDataHash[64];
    public uint EmptyBlockDataOffset;
    public uint EmptyBlockDataSize;
    public fixed byte EmptyBlockDataHash[64];
    public uint DirIndexDataOffset;
    public uint DirIndexDataSize;
    public fixed byte DirIndexDataHash[64];
    public uint IndexType;
    public fixed byte _reserved[656];
    public fixed byte self_hash[64];
}

[StructLayout(LayoutKind.Sequential)]
public struct IndexHashTableEntry
{
    public ulong Hash;
    public uint Data;
    private uint _padding;

    public bool IsSynonym => (Data & 0b1) == 0b1;

    public byte DataFileId => (byte)((Data & 0b1110) >> 1);

    public long Offset => ((uint)Data & ~0xF) * 0x08;
}

[StructLayout(LayoutKind.Sequential)]
public struct Index2HashTableEntry
{
    public uint Hash;
    public uint Data;

    public bool IsSynonym => (Data & 0b1) == 0b1;

    public byte DataFileId => (byte)((Data & 0b1110) >> 1);

    public long Offset => ((uint)Data & ~0xF) * 0x08;
}
