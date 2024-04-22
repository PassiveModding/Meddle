using System.Text;

namespace Meddle.Utils.Files.SqPack;

public class SqPack
{
    private Repository[] repositories;
    public ReadOnlySpan<Repository> Repositories => repositories;
    
    public SqPack(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException();
        }
        
        
        if (path.Split(Path.DirectorySeparatorChar).Last() != "game")
        {
            path = Path.Combine(path, "game");
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException();
            }
        }
        
        var sqpackDir = Path.Combine(path, "sqpack");
        if (!Directory.Exists(sqpackDir))
        {
            throw new DirectoryNotFoundException();
        }
        
        // install/game/sqpack
        // list directories under sqpack
        // list files under each directory
        // parse each file
        var directories = Directory.GetDirectories(sqpackDir);
        repositories = new Repository[directories.Length];
        for (var i = 0; i < directories.Length; i++)
        {
            var directory = directories[i];
            repositories[i] = new Repository(directory);
        }
    }

    public bool FileExists(string path, out ParsedFilePath hash)
    {
        hash = GetFileHash(path);
        var categoryName = path.Split('/')[0];

        byte? catId = null;
        if (Category.CategoryNameToIdMap.TryGetValue(categoryName, out var id))
        {
            catId = id;
        }

        foreach (var repo in repositories)
        {
            var catMatch = repo.Categories.ToArray();
            if (catId != null)
            {
                catMatch = catMatch.Where(x => x.Key.category == catId.Value).ToArray();
            }
            
            if (catMatch.Length == 0)
            {
                continue;
            }

            foreach (var (key, category) in catMatch)
            {
                if (category.FileExists(hash.IndexHash)) return true;
                if (category.FileExists(hash.Index2Hash)) return true;
            }
        }
        
        return false;
    }
    
    public (IndexHashTableEntry hash, SqPackFile file)? GetFile(string path)
    {
        var hash = GetFileHash(path);
        var categoryName = path.Split('/')[0];

        byte? catId = null;
        if (Category.CategoryNameToIdMap.TryGetValue(categoryName, out var id))
        {
            catId = id;
        }

        foreach (var repo in repositories)
        {
            var catMatch = repo.Categories.ToArray();
            if (catId != null)
            {
                catMatch = catMatch.Where(x => x.Key.category == catId.Value).ToArray();
            }
            
            if (catMatch.Length == 0)
            {
                continue;
            }

            foreach (var (key, category) in catMatch)
            {
                if (category.TryGetFile(hash.IndexHash, out var data))
                {
                    return (category.UnifiedIndexEntries[hash.IndexHash], data);
                }
                
                if (category.TryGetFile(hash.Index2Hash, out var data2))
                {
                    return (category.UnifiedIndexEntries[hash.Index2Hash], data2);
                }
            }
        }

        return null;
    }
    
    public static ParsedFilePath GetFileHash(string path)
    {
        var pathParts = path.Split('/');
        var category = pathParts[0];
        var fileName = pathParts[^1];
        var folder = path.Substring(0, path.LastIndexOf('/'));
        
        var folderHash = GetHash(folder);
        var fileHash = GetHash(fileName);
        var indexHash = ((ulong)folderHash << 32) | fileHash;

        var index2Hash = GetHash(path);

        return new ParsedFilePath
        {
            Category = category,
            IndexHash = indexHash,
            Index2Hash = index2Hash,
            Path = path
        };
    }

    private static uint GetHash(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        var crcLocal = uint.MaxValue ^ 0U;
        var table = new uint[16 * 256];
        const uint poly = 0xedb88320u;
        for (uint i = 0; i < 256; i++)
        {
            var res = i;
            for (var t = 0; t < 16; t++)
            {
                for (var k = 0; k < 8; k++)
                {
                    res = (res & 1) == 1 ? poly ^ (res >> 1) : (res >> 1);
                }

                table[(t * 256) + i] = res;
            }
        }

        foreach (var ch in data) {
            crcLocal = table[(byte)(crcLocal ^ ch)] ^ (crcLocal >> 8);
        }
        
        return ~(crcLocal ^ uint.MaxValue);
    }
}
