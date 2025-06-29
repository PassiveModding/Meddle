using System.Collections.Concurrent;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SkiaSharp;

namespace Meddle.Plugin.Models.Composer;

public class ComposerCache
{
    private readonly PbdFile defaultPbdFile;
    private readonly ConcurrentDictionary<string, ShaderPackage> shpkCache = new();
    private readonly ConcurrentDictionary<string, RefCounter<MtrlFile>> mtrlCache = new();
    private readonly ConcurrentDictionary<string, string> mtrlPathCache = new();
    private readonly ConcurrentDictionary<string, PbdFile> pbdCache = new();
    private readonly ConcurrentDictionary<string, RefCounter<MdlFile>> mdlCache = new();
    
    private sealed class RefCounter<T>(T obj)
    {
        public T Object { get; } = obj;
        public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    }
    
    private readonly SqPack pack;
    private readonly string cacheDir;
    private readonly Configuration.ExportConfiguration exportConfig;

    public ComposerCache(SqPack pack, string cacheDir, Configuration.ExportConfiguration exportConfig)
    {
        this.pack = pack;
        this.cacheDir = cacheDir;
        this.exportConfig = exportConfig;
        defaultPbdFile = GetPbdFile("chara/xls/boneDeformer/human.pbd");
    }
    
    public PbdFile GetDefaultPbdFile()
    {
        return defaultPbdFile;
    }
    
    public PbdFile GetPbdFile(string path)
    {
        var item = pbdCache.GetOrAdd(path, key =>
        {
            var pbdData = pack.GetFileOrReadFromDisk(key);
            if (pbdData == null) throw new Exception($"Failed to load pbd file: {key}");

            if (exportConfig.CacheFileTypes.HasFlag(CacheFileType.Pbd))
            {
                CacheFile(key);
            }
            
            return new PbdFile(pbdData);
        });
        
        return item;
    }
    
    
    private bool arrayTexturesSaved;
    public void SaveArrayTextures()
    {
        if (arrayTexturesSaved) return;
        arrayTexturesSaved = true;
        
        Directory.CreateDirectory(cacheDir);
        ArrayTextureUtil.SaveSphereTextures(pack, cacheDir);
        ArrayTextureUtil.SaveTileTextures(pack, cacheDir);
        ArrayTextureUtil.SaveBgSphereTextures(pack, cacheDir);
        ArrayTextureUtil.SaveBgDetailTextures(pack, cacheDir);
    }
    
    public MdlFile GetMdlFile(string path)
    {
        var item = mdlCache.GetOrAdd(path, key =>
        {
            var mdlData = pack.GetFileOrReadFromDisk(path);
            if (mdlData == null) throw new Exception($"Failed to load model file: {path}");
            var mdlFile = new MdlFile(mdlData);
            
            if (exportConfig.CacheFileTypes.HasFlag(CacheFileType.Mdl))
            {
                CacheFile(path);
            }
            
            if (mdlCache.Count > 100)
            {
                var toRemove = mdlCache.OrderBy(x => x.Value.LastAccess).First();
                mdlCache.TryRemove(toRemove.Key, out _);
                Plugin.Logger?.LogDebug($"Evicting model file: {toRemove.Key}");
            }
            
            return new RefCounter<MdlFile>(mdlFile);
        });
        
        item.LastAccess = DateTime.UtcNow;
        return item.Object;
    }
    
    public MtrlFile GetMtrlFile(string path, out string? cachePath)
    {
        var item = mtrlCache.GetOrAdd(path, key =>
        {
            var mtrlData = pack.GetFileOrReadFromDisk(path);
            if (mtrlData == null) throw new Exception($"Failed to load material file: {path}");
            var mtrlFile = new MtrlFile(mtrlData);
            
            if (exportConfig.CacheFileTypes.HasFlag(CacheFileType.Mtrl))
            {
                var cachePath = CacheFile(path);
                mtrlPathCache.TryAdd(path, cachePath);
            }
            
            if (mtrlCache.Count > 100)
            {
                // evict least recently accessed
                var toRemove = mtrlCache.OrderBy(x => x.Value.LastAccess).First();
                mtrlCache.TryRemove(toRemove.Key, out _);
                Plugin.Logger?.LogDebug("Evicting material file: {toRemove}", toRemove.Key);
            }
            
            return new RefCounter<MtrlFile>(mtrlFile);
        });
        
        cachePath = mtrlPathCache.GetValueOrDefault(path);
        
        item.LastAccess = DateTime.UtcNow;
        return item.Object;
    }
    
    public ShaderPackage GetShaderPackage(string shpkName)
    {
        var shpkPath = $"shader/sm5/shpk/{shpkName}";
        return shpkCache.GetOrAdd(shpkPath, key =>
        {
            var shpkData = pack.GetFileOrReadFromDisk(key);
            if (shpkData == null) throw new Exception($"Failed to load shader package file: {key}");
            var shpkFile = new ShpkFile(shpkData);
            
            if (exportConfig.CacheFileTypes.HasFlag(CacheFileType.Shpk))
            {
                CacheFile(key);
            }
            
            return new ShaderPackage(shpkFile, shpkName);
        });
    }

    private string GetCacheFilePath(string fullPath)
    {
        var cleanPath = fullPath.TrimHandlePath();
        var rooted = Path.IsPathRooted(cleanPath);

        if (rooted)
        {
            var pathRoot = Path.GetPathRoot(cleanPath) ?? string.Empty;
            cleanPath = cleanPath[pathRoot.Length..];
        }

        // modded files are stored in a separate directory to prevent conflict if the same file is modded and unmodded.
        var basePath = rooted ? Path.Combine(cacheDir, "modded") : cacheDir;

        const int maxCharacters = 255;
        var charactersAvailable = maxCharacters - basePath.Length;
        var len = cleanPath.Length + 5; // +5 is only because we may add a suffix.
        if (len >= charactersAvailable)
        {
            // Trim path to a suitable length for the cache directory if it exceeds the max length.
            var parts = cleanPath.Replace('\\', '/').Split('/');
            var dirHash = Crc32.GetHash(string.Join("/", parts[..^1]));
            var fileName = parts[^1];
            
            var trimmed = $"{dirHash}/{fileName}";
            Plugin.Logger?.LogDebug("Cache path too long ({len} > {available}), using hash: {trimmed} for {fullPath}", len, charactersAvailable, trimmed, fullPath);
            cleanPath = trimmed;
        }
        
        var cachePath = Path.Combine(basePath, cleanPath);
        return cachePath;
    }
    
    private string CacheFile(string fullPath)
    {
       var cachePath = GetCacheFilePath(fullPath);
        
        if (File.Exists(cachePath)) return cachePath;
        
        var fileData = pack.GetFileOrReadFromDisk(fullPath);
        if (fileData == null) throw new Exception($"Failed to load file: {fullPath}");
        
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.WriteAllBytes(cachePath, fileData);
        return cachePath;
    }
    
    private string CacheTexture(string fullPath)
    {
        var cachePath = GetCacheFilePath(fullPath);
        var pngCachePath = cachePath + ".png";
        
        // inner skip if the png cache exists.
        if (File.Exists(pngCachePath)) return pngCachePath;
        
        var fileData = pack.GetFileOrReadFromDisk(fullPath);
        if (fileData == null) throw new Exception($"Failed to load file: {fullPath}");
        
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        if (exportConfig.CacheFileTypes.HasFlag(CacheFileType.Tex))
        {
            File.WriteAllBytes(cachePath, fileData);
        }
        
        var tex = new TexFile(fileData);
        var texture = tex.ToResource().ToTexture();
        using var memoryStream = new MemoryStream();
        texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
        var textureBytes = memoryStream.ToArray();
        File.WriteAllBytes(pngCachePath, textureBytes);
        return pngCachePath;
    }

    public MaterialBuilder ComposeMaterial(string mtrlPath, 
                                           ParsedMaterialInfo? materialInfo = null,
                                           ParsedInstance? instance = null, 
                                           ParsedCharacterInfo? characterInfo = null, 
                                           IColorTableSet? colorTableSet = null)
    {
        var mtrlFile = GetMtrlFile(mtrlPath, out var mtrlCachePath);
        var shaderPackage = GetShaderPackage(mtrlFile.GetShaderPackageName());
        var material = new MaterialComposer(mtrlFile, mtrlPath, shaderPackage);
        if (instance != null)
        {
            material.SetPropertiesFromInstance(instance);
        }
        
        if (mtrlCachePath != null)
        {
            material.SetProperty("MtrlCachePath", Path.GetRelativePath(cacheDir, mtrlCachePath));
        }
        
        if (characterInfo != null)
        {
            material.SetPropertiesFromCharacterInfo(characterInfo);
        }
        
        if (colorTableSet != null)
        {
            material.SetPropertiesFromColorTable(colorTableSet);
        }

        if (materialInfo != null)
        {
            material.SetPropertiesFromMaterialInfo(materialInfo);
        }

        string materialName;
        if (materialInfo != null)
        {
            materialName = $"{Path.GetFileNameWithoutExtension(materialInfo.Path.GamePath)}_{materialInfo.Shpk}";
        }
        else
        {
            materialName = $"{Path.GetFileNameWithoutExtension(mtrlPath)}_{mtrlFile.GetShaderPackageName()}";
        }

        var materialBuilder = new RawMaterialBuilder(materialName);
        foreach (var texture in material.TextureUsageDict)
        {
            // ensure texture gets saved to cache dir.
            var fullPath = texture.Value.FullPath;
            if (materialInfo != null)
            {
                var match = materialInfo.Textures.FirstOrDefault(x => x.Path.GamePath == texture.Value.GamePath);
                if (match != null)
                {
                    fullPath = match.Path.FullPath;
                }
            }
            
            var cachePath = CacheTexture(fullPath);
            
            // remove full path prefix, get only dir below cache dir.
            material.SetProperty($"{texture.Key}_PngCachePath", Path.GetRelativePath(cacheDir, cachePath));
        }

        materialBuilder.Extras = material.ExtrasNode;
        return materialBuilder;
    }
}
