using System.Collections.Concurrent;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SkiaSharp;

namespace Meddle.Plugin.Models.Composer;

public class DataProvider
{
    private readonly string cacheDir;
    private readonly SqPack dataManager;
    private readonly ILogger logger;
    private readonly CancellationToken cancellationToken;
    private readonly ConcurrentDictionary<string, Lazy<string?>> lookupCache = new();
    private readonly ConcurrentDictionary<int, Lazy<MaterialBuilder>> mtrlCache = new();
    private readonly ConcurrentDictionary<string, Lazy<MtrlFile>> mtrlFileCache = new();
    private readonly ConcurrentDictionary<string, Lazy<ShpkFile>> shpkFileCache = new();

    public DataProvider(string cacheDir, SqPack dataManager, ILogger logger, CancellationToken cancellationToken)
    {
        this.cacheDir = cacheDir;
        this.dataManager = dataManager;
        this.logger = logger;
        this.cancellationToken = cancellationToken;
    }
    
    public MaterialBuilder GetMaterialBuilder(MaterialSet material, string path, string shpkName)
    {
        return mtrlCache.GetOrAdd(material.Uid(), key =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new Lazy<MaterialBuilder>(() =>
            {
                logger.LogInformation("[{shpkName}] Composing material {path}", shpkName, path);
                return material.Compose(this);
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }).Value;
    }
    
    public MtrlFile GetMtrlFile(string path)
    {
        return mtrlFileCache.GetOrAdd(path, key =>
        {
            var mtrlData = LookupData(key);
            if (mtrlData == null) throw new Exception($"Failed to load material file: {key}");
            return new Lazy<MtrlFile>(() => new MtrlFile(mtrlData), LazyThreadSafetyMode.ExecutionAndPublication);
        }).Value;
    }

    public ShpkFile GetShpkFile(string fullPath)
    {
        return shpkFileCache.GetOrAdd(fullPath, key =>
        {
            var shpkData = LookupData(key);
            if (shpkData == null) throw new Exception($"Failed to load shader package file: {key}");
            return new Lazy<ShpkFile>(() => new ShpkFile(shpkData), LazyThreadSafetyMode.ExecutionAndPublication);
        }).Value;
    }
    
    private ConcurrentDictionary<string, Lazy<ImageBuilder?>> textureCache = new();
    public ImageBuilder? LookupTexture(string gamePath)
    {
        gamePath = gamePath.TrimHandlePath();
        if (!gamePath.EndsWith(".tex")) throw new ArgumentException("Texture path must end with .tex", nameof(gamePath));
        if (Path.IsPathRooted(gamePath))
        {
            throw new ArgumentException("Texture path must be a game path", nameof(gamePath));
        }
        
        return textureCache.GetOrAdd(gamePath, key =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new Lazy<ImageBuilder?>(() =>
            {
                var data = LookupData(key, false);
                if (data == null) return null;
                var texFile = new TexFile(data);
                return CacheTexture(texFile.ToResource().ToTexture(), key);
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }).Value;
    }
    
    public byte[]? LookupData(string fullPath, bool cacheIfTexture = true)
    {
        fullPath = fullPath.TrimHandlePath();
        if (Path.IsPathRooted(fullPath))
        {
            return File.ReadAllBytes(fullPath);
        }
        var diskPath = lookupCache.GetOrAdd(fullPath, key =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new Lazy<string?>(() => LookupDataInner(key, cacheIfTexture), LazyThreadSafetyMode.ExecutionAndPublication);
        });

        return diskPath.Value == null ? null : File.ReadAllBytes(diskPath.Value);
    }
    
    private string? LookupDataInner(string gamePath, bool cacheIfTexture)
    {
        var outPath = Path.Combine(cacheDir, gamePath);
        var outDir = Path.GetDirectoryName(outPath);
        var data = dataManager.GetFileOrReadFromDisk(gamePath);
        if (data == null)
        {
            logger.LogError("Failed to load file: {path}", gamePath);
            return null;
        }

        if (!string.IsNullOrEmpty(outPath))
        {
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            File.WriteAllBytes(outPath, data);
            if (gamePath.EndsWith(".tex") && cacheIfTexture)
            {
                try
                {
                    var texFile = new TexFile(data).ToResource().ToTexture();
                    CacheTexture(texFile, gamePath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to cache tex file: {path}", gamePath);
                }
            }
        }

        return outPath;
    }

    public ImageBuilder CacheTexture(SKTexture texture, string texName)
    {
        texName = texName.TrimHandlePath();
        if (Path.IsPathRooted(texName))
        {
            texName = Path.GetFileName(texName);
        }
        
        var outPath = Path.Combine(cacheDir, $"{texName}.png");
        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
            textureBytes = memoryStream.ToArray();
        }

        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        File.WriteAllBytes(outPath, textureBytes);
        var outImage = new MemoryImage(() => File.ReadAllBytes(outPath));

        var name = Path.GetFileNameWithoutExtension(texName.Replace('.', '_'));
        var builder = ImageBuilder.From(outImage, name);
        builder.AlternateWriteFileName = $"{name}.*";
        return builder;
    }
}
