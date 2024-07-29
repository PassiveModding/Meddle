namespace Meddle.Utils.Files.SqPack;

public record ParsedFilePath
{
    public ParsedFilePath(string path)
    {
        Path = path.ToLowerInvariant().Trim();
        var pathParts = Path.Split('/');
        var category = pathParts[0];
        var fileName = pathParts[^1];
        var folder = Path.Substring(0, Path.LastIndexOf('/'));
        
        var folderHash = SqPack.GetHash(folder);
        var fileHash = SqPack.GetHash(fileName);
        var indexHash = ((ulong)folderHash << 32) | fileHash;

        var index2Hash = SqPack.GetHash(Path);
        Category = category;
        IndexHash = indexHash;
        Index2Hash = index2Hash;
    }
    
    /// <summary>
    /// The category of the file, essentially it's 'container'
    ///
    /// bg, game_script, etc.
    /// </summary>
    public string Category { get; internal set; }
        
    /// <summary>
    /// Index hash
    /// </summary>
    public ulong IndexHash { get; internal set; }
        
    /// <summary>
    /// Index2 hash
    /// </summary>
    public uint Index2Hash { get; internal set; }

    /// <summary>
    /// The portion of an <see cref="IndexHash"/> that represents the folders in the path only
    /// </summary>
    public uint FolderHash => (uint)(IndexHash >> 32);

    /// <summary>
    /// The portion of an <see cref="IndexHash"/> that represents the filename in the path only
    /// </summary>
    public uint FileHash => (uint)IndexHash;
        
    /// <summary>
    /// The raw path provided when parsing the initial path
    /// </summary>
    public string Path { get; internal set; }
        
    public static implicit operator string( ParsedFilePath obj ) => obj.Path;
}
