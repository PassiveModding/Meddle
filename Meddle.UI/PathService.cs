using System.Collections.Concurrent;
using System.IO.Compression;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Models;

namespace Meddle.UI;

public static class PathUtils
{
    public static List<ParsedFilePath> ParsePaths(IEnumerable<string> paths)
    {
        var parsedPaths = new ConcurrentBag<ParsedFilePath>();
        Parallel.ForEach(paths, new ParallelOptions
        {
            MaxDegreeOfParallelism = 50
        }, line =>
        {
            if (!line.Contains('/')) return;
            var hash = SqPack.GetFileHash(line);
            parsedPaths.Add(hash);
        });
        
        return parsedPaths.ToList();
    }
    
    /// <summary>
    /// Material files list their texture paths in the file, this method will parse the material files and return a list of texture paths.
    /// </summary>
    public static IEnumerable<ParsedFilePath> DiscoverTexPaths(IEnumerable<ParsedFilePath> paths, SqPack pack)
    {
        foreach (var path in paths)
        {
            if (path.Path.EndsWith(".mtrl"))
            {
                var file = pack.GetFile(path.Path);
                if (file is null) continue;
                Material material;
                try
                {
                    var mtrl = new MtrlFile(file.Value.file.RawData);
                    material = new Material(mtrl);
                }
                catch
                {
                    continue;
                }
                
                foreach (var (_, value) in material.TexturePaths)
                {
                    yield return SqPack.GetFileHash(value);
                }
            }
        }
    }

    public static IEnumerable<ParsedFilePath> ValidatePaths(IEnumerable<ParsedFilePath> paths, SqPack pack)
    {
        foreach (var path in paths)
        {
            if (pack.FileExists(path.Path, out _))
            {
                yield return path;
            }
        }
    }

    public static async Task<List<ParsedFilePath>> GetResLoggerPaths()
    {
        var url = "https://rl2.perchbird.dev/download/export/CurrentPathList.gz"; 
        using var client = new HttpClient();
        var req = await client.GetStreamAsync(url);
        await using var gz = new GZipStream(req, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);

        var rl = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line is null) continue;
            rl.Add(line);
        }

        return ParsePaths(rl);
    }
}
