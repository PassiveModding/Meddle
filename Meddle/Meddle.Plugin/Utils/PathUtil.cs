using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.STD;
using Meddle.Utils.Files.SqPack;

namespace Meddle.Plugin.Utils;

public static class PathUtil
{
    // lumina still fails on certain textures ie. array
    public static byte[]? GetFileOrReadFromDisk(this IDataManager pack, string path)
    {
        path = path.TrimHandlePath();

        // if path is rooted, get from disk
        if (Path.IsPathRooted(path))
        {
            var data = File.ReadAllBytes(path);
            return data;
        }

        var file = pack.GetFile(path);
        return file?.Data;
    }
    
    public static byte[]? GetFileOrReadFromDisk(this SqPack pack, string path)
    {
        path = path.TrimHandlePath();
        
        // if path is rooted, get from disk
        if (Path.IsPathRooted(path))
        {
            var data = File.ReadAllBytes(path);
            return data;
        }

        var file = pack.GetFile(path);
        return file?.file.RawData.ToArray();
    }
    
    public static string TrimHandlePath(this string path)
    {
        // if path is in format |...|path/to/file, trim the |...| part
        if (path[0] == '|')
        {
            path = path.Substring(path.IndexOf('|', 1) + 1);
        }

        return path;
    }
    
    public static string ParseString(this StdString stdString)
    {
        var data = stdString.ToArray();
        if (data.Length == 0)
            return string.Empty;

        return Encoding.UTF8.GetString(data);
    }
}
