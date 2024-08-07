using Dalamud.Interface.Textures.TextureWraps;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class CachedTexture : IDisposable
{
    public CachedTexture(IDalamudTextureWrap wrap)
    {
        Wrap = wrap;
        LastAccessTime = DateTime.Now;
    }

    public IDalamudTextureWrap Wrap { get; set; }
    public DateTime LastAccessTime { get; set; }
    
    public void Dispose()
    {
        Wrap.Dispose();
    }
}

public sealed class TextureCache : IDisposable
{
    private readonly Dictionary<string, CachedTexture> cache = new();
    private readonly TimeSpan expirationTime;
    private readonly ILogger<TextureCache> logger;
    private readonly Timer cleanupTimer;

    public TextureCache(ILogger<TextureCache> logger)
    {
        this.expirationTime = TimeSpan.FromSeconds(10);
        this.logger = logger;
        cleanupTimer = new Timer(CleanupExpiredTextures, null, expirationTime, expirationTime);
    }

    public IDalamudTextureWrap GetOrAdd(string key, Func<IDalamudTextureWrap> createWrap)
    {
        if (cache.TryGetValue(key, out var cachedTexture))
        {
            cachedTexture.LastAccessTime = DateTime.Now;
            return cachedTexture.Wrap;
        }

        var wrap = createWrap();
        cache[key] = new CachedTexture(wrap);
        return wrap;
    }

    private void CleanupExpiredTextures(object? state)
    {
        var now = DateTime.Now;
        var keysToRemove = new List<string>();

        foreach (var kvp in cache)
        {
            if (now - kvp.Value.LastAccessTime > expirationTime)
            {
                keysToRemove.Add(kvp.Key);
                logger.LogDebug("Removing expired texture: {Key}", kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            cache[key].Dispose();
            cache.Remove(key);
        }
    }

    private void ReleaseUnmanagedResources()
    {
        foreach (var kvp in cache)
        {
            kvp.Value.Dispose();
        }
    }

    private void Dispose(bool disposing)
    {
        ReleaseUnmanagedResources();
        if (disposing)
        {
            cleanupTimer.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    ~TextureCache()
    {
        Dispose(false);
    }
}
