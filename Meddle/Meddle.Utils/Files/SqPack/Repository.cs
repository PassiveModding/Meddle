using System.Globalization;

namespace Meddle.Utils.Files.SqPack;

public class Repository
{
    public string Version { get; }
    public int? ExpansionId { get; }
    public string Path { get; }
    public Dictionary<(byte category, byte expansion, byte chunk), Category> Categories { get; } = new();
    
    public static string ParseVersion(DirectoryInfo info)
    {
        // game/sqpack/ffxiv version is in the game/ffxivgame.ver file (bench doesn't bundle this though)
        // game/sqpack/ex{X} version is in the ex{x}/ex{X}.ver file
        string filePath;
        if (info.Name == "ffxiv")
        {
            var path = info.Parent!.Parent;
            filePath = System.IO.Path.Combine(path!.FullName, "ffxivgame.ver");
        }
        else
        {
            filePath = System.IO.Path.Combine(info.FullName, $"{info.Name}.ver");
        }
        
        if (!File.Exists(filePath))
        {
            return string.Empty;
        }
        
        return File.ReadAllText(filePath);
    }
    public static int? GetExpansionId(DirectoryInfo info)
    {
        if (!info.Name.StartsWith("ex"))
        {
            return null;
        }
        
        try
        {
            return int.Parse(info.Name[2..]);
        }
        catch (FormatException e)
        {
            return null;
        }
    }
    
    public Repository(string path)
    {
        Version = ParseVersion(new DirectoryInfo(path));
        ExpansionId = GetExpansionId(new DirectoryInfo(path));
        Path = path;

        var allFiles = Directory.GetFiles(path);
        
        // {XXXXXX}.win32.index
        // {XXXXXX}.win32.index2
        // {XXXXXX}.win32.datX - dat0, dat1, dat2, etc
        // {XX    } is the category id
        // {  XX  } is the expansion id
        // {    XX} is the chunk id
        var setGroups = allFiles.GroupBy(x => System.IO.Path.GetFileName(x)[..6]);
        foreach (var setGroup in setGroups)
        {
            var setId = setGroup.Key;
            var catFiles = setGroup.ToList();
            var indexFile = catFiles.FirstOrDefault(x => x.EndsWith(".index"));
            var index2File = catFiles.FirstOrDefault(x => x.EndsWith(".index2"));
            var datFiles = catFiles.Where(x => x.Split('.').Last().StartsWith("dat")).ToArray();

            if (datFiles.Length == 0)
            {
                throw new FileNotFoundException($"Could not find .dat files for category {setId} in {path}");
            }
            
            if (indexFile == null && index2File == null)
            {
                throw new FileNotFoundException($"Could not find .index or .index2 file for category {setId} in {path}");
            }
            
            var index = indexFile == null ? null : File.ReadAllBytes(indexFile);
            var index2 = index2File == null ? null : File.ReadAllBytes(index2File);
            
            var categoryId = byte.Parse(setId[..2], NumberStyles.HexNumber);
            var expansionId = byte.Parse(setId[2..4], NumberStyles.HexNumber);
            var chunkId = byte.Parse(setId[4..6], NumberStyles.HexNumber);
            var cat = new Category(categoryId, index, index2, datFiles);
            Categories[(categoryId, expansionId, chunkId)] = cat;
        }
    }
}
