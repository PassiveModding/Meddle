namespace Meddle.Utils.Helpers;

public static class PathUtil
{
    public static string TrimHandlePath(this string path)
    {
        // if path is in format |...|path/to/file, trim the |...| part
        if (path[0] == '|')
        {
            path = path.Substring(path.IndexOf('|', 1) + 1);
        }

        return path;
    }
}
