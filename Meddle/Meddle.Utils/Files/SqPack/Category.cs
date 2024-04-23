using System.Collections.ObjectModel;
using System.IO.Compression;
using Meddle.Utils;

namespace Meddle.Utils.Files.SqPack;

public class Category
{
    public static readonly Dictionary< string, byte > CategoryNameToIdMap = new()
    {
        { "common", 0x00 },
        { "bgcommon", 0x01 },
        { "bg", 0x02 },
        { "cut", 0x03 },
        { "chara", 0x04 },
        { "shader", 0x05 },
        { "ui", 0x06 },
        { "sound", 0x07 },
        { "vfx", 0x08 },
        { "ui_script", 0x09 },
        { "exd", 0x0A },
        { "game_script", 0x0B },
        { "music", 0x0C },
        { "sqpack_test", 0x12 },
        { "debug", 0x13 },
    };

    public static string? TryGetCategoryName(byte id)
    {
        if (CategoryNameToIdMap.ContainsValue(id))
        {
            return CategoryNameToIdMap.First(x => x.Value == id).Key;
        }
        
        return null;
    }
    
    public byte Id { get; }

    private SqPackFile ReadFile(int dataFileId, long offset)
    {
        var datFilePath = datFilePaths[dataFileId];
        using var zzzzzzzzzzzzzzzzzz = File.OpenRead(datFilePath);
        using var br = new BinaryReader(zzzzzzzzzzzzzzzzzz);
        zzzzzzzzzzzzzzzzzz.Seek(offset, SeekOrigin.Begin);
        
        var header = br.Read<SqPackFileInfo>();
        
        var buffer = new byte[(int)header.RawFileSize];
        using var ms = new MemoryStream(buffer);
        if (header.Type == FileType.Empty)
        {
            throw new FileNotFoundException($"The file located at {datFilePath} at offset {offset} is empty");
        }
        
        if (header.Type == FileType.Texture)
        {
            int lodBlocks = (int)header.NumberOfBlocks;
            var blocks = br.Read<LodBlock>(lodBlocks);

            uint mipMapSize = blocks[0].CompressedOffset;
            if (mipMapSize != 0)
            {
                var pos = br.BaseStream.Position;
                br.BaseStream.Position = offset + header.Size;
                var mipMap = br.Read<byte>((int)mipMapSize);
                ms.Write(mipMap);
                br.BaseStream.Position = pos;
            }
            
            for (var i = 0; i < blocks.Length; i++)
            {
                var blockOffset = offset + header.Size + blocks[i].CompressedOffset;
                for (int j = 0; j < blocks[i].BlockCount; j++)
                {
                    var pos = br.BaseStream.Position;
                    br.BaseStream.Position = blockOffset;
                    var blockHeader = br.Read<DatBlockHeader>();
                    if (blockHeader.DatBlockType == DatBlockType.Uncompressed)
                    {
                        ms.Write(br.Read<byte>((int)blockHeader.BlockDataSize));
                    }
                    else
                    {
                        using var zlibStream = new DeflateStream( br.BaseStream, CompressionMode.Decompress, true );
            
                        var ob = new byte[blockHeader.BlockDataSize];
                        var totalRead = 0;
                        while( totalRead < blockHeader.BlockDataSize )
                        {
                            var bytesRead = zlibStream.Read( ob, totalRead, (int)blockHeader.BlockDataSize - totalRead );
                            if( bytesRead == 0 ) { break; }
                            totalRead += bytesRead;
                        }

                        if( totalRead != (int)blockHeader.BlockDataSize )
                        {
                            throw new InvalidDataException( $"Failed to read block data, expected {blockHeader.BlockDataSize} bytes, got {totalRead}" );
                        }
            
                        ms.Write(ob);
                    }
                    
                    br.BaseStream.Position = pos;
                    var size = br.ReadUInt16();
                    blockOffset += size;
                }
            }
        }

        return new SqPackFile(header, buffer);
    }
    
    public bool FileExists(ulong hash)
    {
        return UnifiedIndexEntries.ContainsKey(hash);
    }
    
    public bool TryGetFile(ulong hash, out SqPackFile data)
    {
        if (!UnifiedIndexEntries.TryGetValue(hash, out var entry))
        {
            data = null!;
            return false;
        }
        
        data = ReadFile(entry.DataFileId, entry.Offset);
        return true;
    }

    private readonly string[] datFilePaths;
    public ReadOnlySpan<string> DatFilePaths => datFilePaths;
    public readonly ReadOnlyDictionary<ulong, IndexHashTableEntry> UnifiedIndexEntries;
    public readonly bool Index;
    public readonly bool Index2;
    public Repository? Repository { get; set; }

    // Take paths instead of file since it will eat all your ram
    public Category(byte catId, byte[]? index, byte[]? index2, string[] datFilePaths)
    {
        Id = catId;
        this.datFilePaths = datFilePaths;

        foreach (var dat in datFilePaths)
        {
            if (!File.Exists(dat))
            {
                throw new FileNotFoundException($"Dat file {dat} does not exist");
            }
        }
        
        var unifiedEntries = new Dictionary<ulong, IndexHashTableEntry>();
        if (index != null)
        {
            Index = true;
            var indexFile = new SqPackIndexFile(index);
            var dats = indexFile.IndexHeader.DataFileCount;
            if (dats > datFilePaths.Length)
            {
                throw new Exception($"Not enough dat files provided for index, expected {dats} got {datFilePaths.Length}");
            }
            
            foreach(var entry in indexFile.Entries)
            {
                unifiedEntries[entry.Hash] = entry;
            }
        }  

        if (index2 != null)
        {
            Index2 = true;
            var index2File = new SqPackIndex2File(index2);
            var dats = index2File.IndexHeader.DataFileCount;
            if (dats > datFilePaths.Length)
            {
                throw new Exception($"Not enough dat files provided for index2, expected {dats} got {datFilePaths.Length}");
            }
            
            foreach(var entry in index2File.Entries)
            {
                unifiedEntries[entry.Hash] = new IndexHashTableEntry()
                {
                    Data = entry.Data,
                    Hash = entry.Hash
                };
            }
        }
        
        this.UnifiedIndexEntries = new ReadOnlyDictionary<ulong, IndexHashTableEntry>(unifiedEntries);
    }
}
