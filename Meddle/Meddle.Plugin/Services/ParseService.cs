using System.Collections.Concurrent;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Services;

public class ParseService : IDisposable, IService
{
    private readonly EventLogger<ParseService> logger;

    public readonly ConcurrentDictionary<string, ShpkFile> ShpkCache = new();
    public readonly ConcurrentDictionary<string, MdlFile> MdlCache = new();
    public readonly ConcurrentDictionary<string, MtrlFile> MtrlCache = new();
    public readonly ConcurrentDictionary<string, TexFile> TexCache = new();
    public void ClearCaches()
    {
        ShpkCache.Clear();
        MdlCache.Clear();
        MtrlCache.Clear();
        TexCache.Clear();
    }

    public ParseService(ILogger<ParseService> logger)
    {
        this.logger = new EventLogger<ParseService>(logger);
    }

    public void Dispose()
    {
        logger.LogDebug("Disposing ParseUtil");
    }
}
