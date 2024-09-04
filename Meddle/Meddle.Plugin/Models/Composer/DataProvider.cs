using System.Collections.Concurrent;
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
    
    public byte[]? LookupData(string fullPath)
    {
        fullPath = fullPath.TrimHandlePath();
        var shortPath = fullPath;
        if (Path.IsPathRooted(fullPath))
        {
            shortPath = Path.GetFileName(fullPath);
            shortPath = Path.Combine("Rooted", shortPath);
        }
        var diskPath = lookupCache.GetOrAdd(fullPath, key =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new Lazy<string?>(() => LookupDataInner(key, shortPath), LazyThreadSafetyMode.ExecutionAndPublication);
        });

        return diskPath.Value == null ? null : File.ReadAllBytes(diskPath.Value);
    }
    
    private string? LookupDataInner(string fullPath, string shortPath)
    {
        var outPath = Path.Combine(cacheDir, shortPath);
        var outDir = Path.GetDirectoryName(outPath);
        var data = dataManager.GetFileOrReadFromDisk(fullPath);
        if (data == null)
        {
            logger.LogError("Failed to load file: {path}", fullPath);
            return null;
        }

        if (!string.IsNullOrEmpty(outPath))
        {
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            File.WriteAllBytes(outPath, data);
            if (fullPath.EndsWith(".tex"))
            {
                try
                {
                    var texFile = new TexFile(data).ToResource().ToTexture();
                    CacheTexture(texFile, shortPath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to cache tex file: {path}", fullPath);
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
            throw new ArgumentException("Texture name cannot be rooted", nameof(texName));
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
