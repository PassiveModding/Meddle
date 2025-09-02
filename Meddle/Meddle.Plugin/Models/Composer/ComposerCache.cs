using System.Collections.Concurrent;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Constants;
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
            
            if (mdlCache.Count > 1000)
            {
                var toRemove = mdlCache.OrderBy(x => x.Value.LastAccess).First();
                mdlCache.TryRemove(toRemove.Key, out _);
                Plugin.Logger.LogDebug($"Evicting model file: {toRemove.Key}");
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
            
            if (mtrlCache.Count > 1000)
            {
                // evict least recently accessed
                var toRemove = mtrlCache.OrderBy(x => x.Value.LastAccess).First();
                mtrlCache.TryRemove(toRemove.Key, out _);
                Plugin.Logger.LogDebug("Evicting material file: {toRemove}", toRemove.Key);
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
            Plugin.Logger.LogDebug("Cache path too long ({len} > {available}), using hash: {trimmed} for {fullPath}", len, charactersAvailable, trimmed, fullPath);
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
    
    public string CacheTexture(string fullPath)
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

    private static readonly IReadOnlyList<string> SkinSlotShaders =
    [
        "characterstockings.shpk"
    ];
    
    public MaterialBuilder ComposeMaterial(string mtrlPath, 
                                           ParsedMaterialInfo? materialInfo = null,
                                           IStainableInstance? stainInstance = null, 
                                           ParsedCharacterInfo? characterInfo = null)
    {
        var mtrlFile = GetMtrlFile(mtrlPath, out var mtrlCachePath);
        var shaderPackage = GetShaderPackage(mtrlFile.GetShaderPackageName());
        var material = new MaterialComposer(mtrlFile, mtrlPath, shaderPackage);
        if (stainInstance != null)
        {
            material.SetPropertiesFromInstance(stainInstance);
        }
        
        if (mtrlCachePath != null)
        {
            material.SetProperty("MtrlCachePath", Path.GetRelativePath(cacheDir, mtrlCachePath));
        }
        
        if (characterInfo != null)
        {
            material.SetPropertiesFromCharacterInfo(characterInfo);
        }

        if (materialInfo != null)
        {
            // kinda janky, but we set skin data first so that the keys are still available to be overridden by the main material info.
            if (materialInfo.SkinSlotMaterial != null && SkinSlotShaders.Contains(materialInfo.Shpk))
            {
                var skinMtrlFile = GetMtrlFile(materialInfo.SkinSlotMaterial.Path.FullPath, out var skinMtrlCachePath);
                if (skinMtrlCachePath != null)
                {
                    material.SetProperty("SkinMtrlCachePath", Path.GetRelativePath(cacheDir, skinMtrlCachePath));
                }
                var skinShaderPackage = GetShaderPackage(skinMtrlFile.GetShaderPackageName());
                var skinMaterial = new MaterialComposer(skinMtrlFile, materialInfo.SkinSlotMaterial.Path.FullPath, skinShaderPackage);
                var constants = Names.GetConstants();
                foreach (var (key, value) in skinMaterial.ShaderKeyDict)
                {
                    var category = (uint)key;
                    var keyMatch = constants.GetValueOrDefault(category);
                    var valMatch = constants.GetValueOrDefault(value);
                    material.SetProperty(keyMatch != null ? keyMatch.Value : $"0x{category:X8}", valMatch != null ? valMatch.Value : $"0x{value:X8}");
                }
                foreach (var texture in skinMaterial.TextureUsageDict)
                {
                    var fullPath = texture.Value.FullPath;
                    var match = materialInfo.Textures.FirstOrDefault(x => x.Path.GamePath == texture.Value.GamePath);
                    if (match != null)
                    {
                        fullPath = match.Path.FullPath;
                    }

                    var cachePath = CacheTexture(fullPath);
                    var keyUsage = $"{texture.Key}".Replace("g_Sampler", "g_SamplerSkin");
                    material.SetProperty($"{keyUsage}", texture.Value.GamePath);
                    material.SetProperty($"{keyUsage}_PngCachePath", Path.GetRelativePath(cacheDir, cachePath));
                }
            }
            
            material.SetPropertiesFromMaterialInfo(materialInfo);
            if (materialInfo.ColorTable != null)
            {
                material.SetPropertiesFromColorTable(materialInfo.ColorTable);
                // since colortables are purely in-memory, they dont have a path.
                // going to store them in the 'ColorTables' directory in the cache with a unique name based on the hash.
                if (materialInfo.ColorTable is ColorTableSet colorTableSet)
                {
                    var tex = colorTableSet.ColorTable.ToTexture();
                    var colorTablePath = SaveColorTableTex(tex);
                    material.SetProperty("ColorTable_PngCachePath", Path.GetRelativePath(cacheDir, colorTablePath));
                }
                else if (materialInfo.ColorTable is LegacyColorTableSet legacyColorTableSet)
                {
                    var tex = legacyColorTableSet.ColorTable.ToTexture();
                    var colorTablePath = SaveColorTableTex(tex);
                    material.SetProperty("LegacyColorTable_PngCachePath", Path.GetRelativePath(cacheDir, colorTablePath));
                }
            }
            
            string SaveColorTableTex(SkTexture tex)
            {
                var colorTableCacheDir = Path.Combine(cacheDir, "color_tables");
                Directory.CreateDirectory(colorTableCacheDir);
                var buf = tex.Bitmap.Bytes;
                var hash = System.Security.Cryptography.SHA256.HashData(buf);
                var hashStr = Convert.ToHexStringLower(hash);
                // truncate the hash to 8 characters for the filename.
                if (hashStr.Length > 8)
                {
                    hashStr = hashStr[..8];
                }
                var mtrlPathWithoutExtension = Path.GetFileNameWithoutExtension(mtrlPath);
                var colorTablePath = Path.Combine(colorTableCacheDir, $"{mtrlPathWithoutExtension}_{materialInfo.Shpk}_{hashStr}.png");
                if (!File.Exists(colorTablePath))
                {
                    using var fileStream = new FileStream(colorTablePath, FileMode.Create, FileAccess.Write);
                    using var skiaStream = new SKManagedWStream(fileStream);
                    tex.Bitmap.Encode(skiaStream, SKEncodedImageFormat.Png, 100);
                }
                return colorTablePath;
            }
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

        var materialBuilder = new MaterialBuilder(materialName);
        foreach (var texture in material.TextureUsageDict)
        {
            // ensure texture gets saved to cache dir.
            var fullPath = texture.Value.FullPath;
            var match = materialInfo?.Textures.FirstOrDefault(x => x.Path.GamePath == texture.Value.GamePath);
            if (match != null)
            {
                fullPath = match.Path.FullPath;
            }

            var cachePath = CacheTexture(fullPath);
            
            // remove full path prefix, get only dir below cache dir.
            material.SetProperty($"{texture.Key}_PngCachePath", Path.GetRelativePath(cacheDir, cachePath));
        }
        
        if (characterInfo != null)
        {
            if (characterInfo.CustomizeData.DecalPath != null)
            {
                var decalCachePath = CacheTexture(characterInfo.CustomizeData.DecalPath);
                material.SetProperty("Decal_PngCachePath", Path.GetRelativePath(cacheDir, decalCachePath));
            }
            
            if (characterInfo.CustomizeData.LegacyBodyDecalPath != null)
            {
                var legacyDecalCachePath = CacheTexture(characterInfo.CustomizeData.LegacyBodyDecalPath);
                material.SetProperty("LegacyBodyDecal_PngCachePath", Path.GetRelativePath(cacheDir, legacyDecalCachePath));
            }
        }

        materialBuilder.Extras = material.ExtrasNode;
        
        return materialBuilder;
    }
}
