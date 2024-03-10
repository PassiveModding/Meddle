using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Penumbra.Api;
using Penumbra.String.Classes;

namespace Meddle.Plugin.Services;

public class FileService : IService
{
    private readonly DalamudPluginInterface _pi;
    private readonly IDataManager _gameData;

    public FileService(DalamudPluginInterface pi, IDataManager gameData)
    {
        _pi = pi;
        _gameData = gameData;
    }
    
    public byte[]? ReadFile(string path)
    {
        // TODO: if cross-collection lookups are turned off, this conversion can be skipped
        if (!Utf8GamePath.FromString(path, out var utf8Path, true))
            throw new Exception($"Resolved path {path} could not be converted to a game path.");

        //var resolvedPath = _activeCollections.Current.ResolvePath(utf8Path) ?? new FullPath(utf8Path);
        var resolvedPath = ResolvePlayerPath(path) ?? new FullPath(utf8Path);

        // TODO: is it worth trying to use streams for these instead? I'll need to do this for mtrl/tex too, so might be a good idea. that said, the mtrl reader doesn't accept streams, so...
        return Path.IsPathRooted(resolvedPath.ToPath())
            ? File.ReadAllBytes(resolvedPath.FullName)
            : _gameData.GetFile(resolvedPath.InternalName.ToString())?.Data;
    }

    public FullPath? ResolvePlayerPath(string path)
    {
        var resolvedPath = Ipc.ResolvePlayerPath.Subscriber(_pi).Invoke(path);
        if (Utf8GamePath.FromString(resolvedPath, out var utf8Path, true))
        {
            return new FullPath(utf8Path);
        }
        
        return null;
    }
}