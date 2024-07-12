using Meddle.Utils.Files.SqPack;

namespace Meddle.Plugin.Utils;

public static class PathUtil
{
    public static byte[]? GetFileOrReadFromDisk(this SqPack pack, string path)
    {
        // if path is in format |...|path/to/file, trim the |...| part
        if (path[0] == '|')
        {
            path = path.Substring(path.IndexOf('|', 1) + 1);
        }
        
        // if path is rooted, get from disk
        if (Path.IsPathRooted(path))
        {
            var data = File.ReadAllBytes(path);
            return data;
        }

        var file = pack.GetFile(path);
        return file?.file.RawData.ToArray();
    }
}
