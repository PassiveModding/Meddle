using System.Collections.Concurrent;
using Dalamud.Plugin;
using Meddle.Formats;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class PathService : IService
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ILogger<PathService> logger;

    public PathService(IDalamudPluginInterface pluginInterface, ILogger<PathService> logger)
    {
        this.pluginInterface = pluginInterface;
        this.logger = logger;
    }

    // null when penumbra isn't installed, loaded or ready
    public string? GetPenumbraDirectory()
    {
        try
        {
            var dir = pluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory").InvokeFunc();
            return string.IsNullOrWhiteSpace(dir) ? null : dir;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Penumbra mod directory unavailable");
            return null;
        }
    }

    // one mapper per export, folder name collisions are tracked per instance
    public ModdedPathMapper CreateMapper() => new(GetPenumbraDirectory());
}

// penumbra files keep their path inside the mod directory
// anything else is assumed to be a flat sync plugin cache and keeps only its folder name
public class ModdedPathMapper
{
    private readonly string? penumbraDir;
    private readonly ConcurrentDictionary<string, string> syncFolderNames = new(StringComparer.OrdinalIgnoreCase);

    public ModdedPathMapper(string? penumbraDir)
    {
        this.penumbraDir = penumbraDir?.Replace('\\', '/').TrimEnd('/');
    }

    public string GetRelativePath(string diskPath)
    {
        var normalized = diskPath.Replace('\\', '/');

        if (penumbraDir != null && normalized.StartsWith(penumbraDir + "/", StringComparison.OrdinalIgnoreCase))
        {
            return $"Penumbra/{normalized[(penumbraDir.Length + 1)..]}";
        }

        var lastSlash = normalized.LastIndexOf('/');
        var dir = lastSlash > 0 ? normalized[..lastSlash] : string.Empty;
        var fileName = normalized[(lastSlash + 1)..];

        var folderName = syncFolderNames.GetOrAdd(dir, d =>
        {
            var name = Path.GetFileName(d);
            if (string.IsNullOrEmpty(name)) name = "root";

            lock (syncFolderNames)
            {
                var clash = syncFolderNames.Any(kv => !kv.Key.Equals(d, StringComparison.OrdinalIgnoreCase) &&
                                                      kv.Value.Equals(name, StringComparison.OrdinalIgnoreCase));
                return clash ? $"{name}_{Crc32.GetHash(d.ToLowerInvariant())}" : name;
            }
        });

        return $"{folderName}/{fileName}";
    }
}
