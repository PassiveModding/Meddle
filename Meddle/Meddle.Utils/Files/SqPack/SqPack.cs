using System.Diagnostics;
using System.Text;

namespace Meddle.Utils.Files.SqPack;

public class SqPack : IDisposable
{
    private static readonly ActivitySource ActivitySource = new("Meddle.Utils.Files.SqPack");

    private static uint[]? CrcTable;
    public IReadOnlyList<Repository> Repositories { get; private set; }

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

        var directories = Directory.GetDirectories(sqpackDir);
        var repositories = new Repository[directories.Length];
        for (var i = 0; i < directories.Length; i++)
        {
            var directory = directories[i];
            repositories[i] = new Repository(directory);
        }

        Repositories = repositories;
    }

    public bool FileExists(string path, out ParsedFilePath hash)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("path", path);
        hash = GetFileHash(path);
        activity?.SetTag("hash", hash.IndexHash.ToString());

        var categoryName = path.Split('/')[0];

        byte? catId = null;
        if (Category.CategoryNameToIdMap.TryGetValue(categoryName, out var id))
        {
            catId = id;
        }

        foreach (var repo in Repositories)
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

    public (Category category, IndexHashTableEntry hash, SqPackFile file)? GetFile(
        string path, FileType? fileType = null)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("path", path);
        var hash = GetFileHash(path);
        activity?.SetTag("hash", hash.IndexHash.ToString());
        activity?.SetTag("category", hash.Category);

        var categoryName = path.Split('/')[0];

        byte? catId = null;
        if (Category.CategoryNameToIdMap.TryGetValue(categoryName, out var id))
        {
            catId = id;
        }

        foreach (var repo in Repositories)
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
                if (category.TryGetFile(hash.IndexHash, fileType, out var data))
                {
                    return (category, category.UnifiedIndexEntries[hash.IndexHash], data);
                }

                if (category.TryGetFile(hash.Index2Hash, fileType, out var data2))
                {
                    return (category, category.UnifiedIndexEntries[hash.Index2Hash], data2);
                }
            }
        }

        return null;
    }

    public (Repository repo, Category category, IndexHashTableEntry hash, SqPackFile file)[] GetFiles(string path)
    {
        var hash = GetFileHash(path);
        var categoryName = path.Split('/')[0];

        var files = new List<(Repository repo, Category category, IndexHashTableEntry hash, SqPackFile file)>();
        byte? catId = null;
        if (Category.CategoryNameToIdMap.TryGetValue(categoryName, out var id))
        {
            catId = id;
        }

        foreach (var repo in Repositories)
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
                    files.Add((repo, category, category.UnifiedIndexEntries[hash.IndexHash], data));
                }

                if (category.TryGetFile(hash.Index2Hash, out var data2))
                {
                    files.Add((repo, category, category.UnifiedIndexEntries[hash.Index2Hash], data2));
                }
            }
        }

        return files.ToArray();
    }

    public static ParsedFilePath GetFileHash(string path)
    {
        return new ParsedFilePath(path);
    }

    private static uint[] GetCrcTable()
    {
        if (CrcTable != null)
        {
            return CrcTable;
        }

        var table = new uint[16 * 256];
        const uint poly = 0xedb88320u;
        for (uint i = 0; i < 256; i++)
        {
            var res = i;
            for (var t = 0; t < 16; t++)
            {
                for (var k = 0; k < 8; k++)
                {
                    res = (res & 1) == 1 ? poly ^ (res >> 1) : res >> 1;
                }

                table[(t * 256) + i] = res;
            }
        }

        CrcTable = table;
        return table;
    }

    public static uint GetHash(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        var crcLocal = uint.MaxValue ^ 0U;
        var table = GetCrcTable();
        foreach (var ch in data)
        {
            crcLocal = table[(byte)(crcLocal ^ ch)] ^ (crcLocal >> 8);
        }

        var ret = ~(crcLocal ^ uint.MaxValue);
        return ret;
    }

    public void Dispose()
    {
        foreach (var repo in Repositories)
        {
            repo.Dispose();
        }
        
        Repositories = null!;
    }
}
