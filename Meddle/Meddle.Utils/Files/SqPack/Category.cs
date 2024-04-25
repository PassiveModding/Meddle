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
        
        data = SqPackUtil.ReadFile(entry.Offset, datFilePaths[entry.DataFileId]);
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
