using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Meddle.Utils.Files.SqPack;

public class Repository : IDisposable
{
    public string Version { get; }
    public int? ExpansionId { get; }
    public string Path { get; }
    public ReadOnlyDictionary<(byte category, byte expansion, byte chunk), Category> Categories { get; private set; }
    
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
        else if (info.Name.StartsWith("ex"))
        {
            filePath = System.IO.Path.Combine(info.FullName, $"{info.Name}.ver");
        }
        else
        {
            return string.Empty;
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
        catch (FormatException)
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
        var keyRegex = new Regex(@"^(?<setId>[0-9a-fA-F]{6})\.(?<platform>\w+)\.(?<fileType>index|index2|dat\d+)$",
                                 RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking);
        var setGroups = allFiles.GroupBy(x =>
                                {
                                    var fileName = System.IO.Path.GetFileName(x);
                                    var match = keyRegex.Match(fileName);
                                    if (!match.Success)
                                    {
                                        Global.Logger.LogWarning("File {FileName} does not match expected pattern", fileName);
                                        return string.Empty;
                                    }

                                    var setId = match.Groups["setId"].Value;
                                    return setId;
                                })
                                .Where(x => !string.IsNullOrEmpty(x.Key));
        var categories = new Dictionary<(byte category, byte expansion, byte chunk), Category>();
        foreach (var setGroup in setGroups)
        {
            var setId = setGroup.Key;
            var catFiles = setGroup.ToList();
            var indexFile = catFiles.FirstOrDefault(x => x.EndsWith(".index"));
            var index2File = catFiles.FirstOrDefault(x => x.EndsWith(".index2"));
            var datFiles = catFiles.Where(x => x.Split('.').Last().StartsWith("dat")).ToArray();

            if (datFiles.Length == 0)
            {
                Global.Logger.LogWarning("Could not find .dat files for category {SetId} in {Path}", setId, path);
                continue;
            }
            
            if (indexFile == null && index2File == null)
            {
                Global.Logger.LogWarning("Could not find .index or .index2 file for category {SetId} in {Path}", setId, path);
                continue;
            }
            
            var index = indexFile == null ? null : File.ReadAllBytes(indexFile);
            var index2 = index2File == null ? null : File.ReadAllBytes(index2File);
            
            var categoryId = byte.Parse(setId[..2], NumberStyles.HexNumber);
            var expansionId = byte.Parse(setId[2..4], NumberStyles.HexNumber);
            var chunkId = byte.Parse(setId[4..6], NumberStyles.HexNumber);
            var cat = new Category(categoryId, index, index2, datFiles);
            categories.Add((categoryId, expansionId, chunkId), cat);
        }
        
        Categories = new ReadOnlyDictionary<(byte category, byte expansion, byte chunk), Category>(categories);
    }

    public void Dispose()
    {
        foreach (var (_, category) in Categories)
        {
            category.Dispose();
        }
        
        Categories = null!;
    }
}
