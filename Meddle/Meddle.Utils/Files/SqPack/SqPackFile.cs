using System.Runtime.CompilerServices;

namespace Meddle.Utils.Files.SqPack;

public class SqPackFile
{
    public SqPackFileInfo FileHeader { get; }
    private readonly byte[] rawData;
    public ReadOnlySpan<byte> RawData => rawData;

    public SqPackFile(SqPackFileInfo fileHeader, byte[] fileData)
    {
        FileHeader = fileHeader;
        rawData = fileData;
    }
}

public class SqPackIndex2File
{
    public SqPackHeader FileHeader { get; }
    public SqPackIndexHeader IndexHeader { get; }
    private readonly Index2HashTableEntry[] entries;
    public ReadOnlySpan<Index2HashTableEntry> Entries => entries;

    public SqPackIndex2File(byte[] data)
    {
        var reader = new SpanBinaryReader(data);
        FileHeader = reader.Read<SqPackHeader>();

        reader.Seek((int)FileHeader.size, SeekOrigin.Begin);
        IndexHeader = reader.Read<SqPackIndexHeader>();

        reader.Seek((int)IndexHeader.IndexDataOffset, SeekOrigin.Begin);
        var entryCount = IndexHeader.IndexDataSize / Unsafe.SizeOf<Index2HashTableEntry>();
        entries = reader.Read<Index2HashTableEntry>((int)entryCount).ToArray();
    }
}

public class SqPackIndexFile
{
    public SqPackHeader FileHeader { get; }
    public SqPackIndexHeader IndexHeader { get; }
    private readonly IndexHashTableEntry[] entries;
    public ReadOnlySpan<IndexHashTableEntry> Entries => entries;

    public SqPackIndexFile(byte[] data)
    {
        var reader = new SpanBinaryReader(data);
        FileHeader = reader.Read<SqPackHeader>();

        reader.Seek((int)FileHeader.size, SeekOrigin.Begin);
        IndexHeader = reader.Read<SqPackIndexHeader>();

        reader.Seek((int)IndexHeader.IndexDataOffset, SeekOrigin.Begin);
        var entryCount = IndexHeader.IndexDataSize / Unsafe.SizeOf<IndexHashTableEntry>();
        entries = reader.Read<IndexHashTableEntry>((int)entryCount).ToArray();
    }
}
