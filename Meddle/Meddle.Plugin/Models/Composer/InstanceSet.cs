using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Models.Layout;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SkiaSharp;

namespace Meddle.Plugin.Models.Composer;

public static class InstanceCache
{
    public static ConcurrentDictionary<string, ShaderPackage> ShpkCache { get; } = new();
}

public class InstanceSet
{
    public InstanceSet(ILogger log, IDataManager manager, ParsedInstance[] instances, string? cacheDir = null, 
                       Action<ProgressEvent>? progress = null, CancellationToken cancellationToken = default)
    {
        CacheDir = cacheDir ?? Path.GetTempPath();
        Directory.CreateDirectory(CacheDir);
        this.instances = instances;
        this.log = log;
        this.dataManager = manager;
        this.progress = progress;
        this.cancellationToken = cancellationToken;
        this.size = instances.Select(x => x.Flatten().Length).Sum();
        this.count = instances.Length;
    }

    private readonly ILogger log;
    private readonly IDataManager dataManager;
    private readonly Action<ProgressEvent>? progress;
    private readonly CancellationToken cancellationToken;
    private readonly int size;
    private readonly int count;
    private int sizeProgress;
    private int countProgress;
    public string CacheDir { get; }
    private readonly ParsedInstance[] instances;
    private readonly Dictionary<string, (string PathOnDisk, MemoryImage MemoryImage)> imageCache = new();

    public void Compose(SceneBuilder scene)
    {
        progress?.Invoke(new ProgressEvent(0, count));
        foreach (var instance in instances)
        {
            var node = ComposeInstance(scene, instance);
            if (node != null)
            {
                scene.AddNode(node);
            }
            
            countProgress++;
            progress?.Invoke(new ProgressEvent(countProgress, count));
        }
    }

    public NodeBuilder? ComposeInstance(SceneBuilder scene, ParsedInstance parsedInstance)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        
        var root = new NodeBuilder();
        if (parsedInstance is ParsedHousingInstance housingInstance)
        {
            root.Name = housingInstance.Name;
        }
        else
        {
            root.Name = $"{parsedInstance.Type}_{parsedInstance.Id}";
        }
        root.SetLocalTransform(parsedInstance.Transform.AffineTransform, false);
        bool added = false;
        
        if (parsedInstance is ParsedBgPartsInstance {Path: not null} bgPartsInstance)
        {
            var meshes = ComposeBgPartsInstance(bgPartsInstance);
            foreach (var mesh in meshes)
            {
                scene.AddRigidMesh(mesh.Mesh, root, Matrix4x4.Identity);
            }
            
            added = true;
        }
        else if (parsedInstance is ParsedLightInstance lightInstance)
        {
            sizeProgress++;
            return null;
            var lightBuilder = new LightBuilder.Point
            {
                Name = $"light_{lightInstance.Id}"
            };
            scene.AddLight(lightBuilder, root);
        }
        else if (parsedInstance is ParsedCharacterInstance characterInstance)
        {
            sizeProgress++;
            return null;
        }
        else
        {
            sizeProgress++;
            return null;
        }
        
        foreach (var child in parsedInstance.Children)
        {
            var childNode = ComposeInstance(scene, child);
            if (childNode != null)
            {
                root.AddNode(childNode);
                added = true;
            }
        }
        
        sizeProgress++;
        
        if (!added) return null;
        return root;
    }

    private IReadOnlyList<ModelBuilder.MeshExport> ComposeBgPartsInstance(ParsedBgPartsInstance bgPartsInstance)
    {
        if (bgPartsInstance.Path == null)
        {
            return [];
        }
        
        var mdlData = dataManager.GetFile(bgPartsInstance.Path);
        if (mdlData == null)
        {
            log.LogWarning("Failed to load model file: {bgPartsInstance.Path}", bgPartsInstance.Path);
            return [];
        }

        var mdlFile = new MdlFile(mdlData.Data);
        var materials = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();

        var materialBuilders = new List<MaterialBuilder>();
        foreach (var mtrlPath in materials)
        {
            var mtrlData = dataManager.GetFile(mtrlPath);
            if (mtrlData == null)
            {
                log.LogWarning("Failed to load material file: {materialPath}", mtrlPath);
                // TODO: Stub material
                continue;
            }

            var mtrlFile = new MtrlFile(mtrlData.Data);
            var texturePaths = mtrlFile.GetTexturePaths();
            var shpkPath = $"shader/sm5/shpk/{mtrlFile.GetShaderPackageName()}";
            if (!InstanceCache.ShpkCache.TryGetValue(shpkPath, out var shaderPackage))
            {
                var shpkData = dataManager.GetFile(shpkPath);
                if (shpkData == null)
                    throw new Exception($"Failed to load shader package file: {shpkPath}");
                var shpkFile = new ShpkFile(shpkData.Data);
                shaderPackage = new ShaderPackage(shpkFile, null!);
                InstanceCache.ShpkCache.TryAdd(shpkPath, shaderPackage);
                log.LogInformation("Loaded shader package {shpkPath}", shpkPath);
            }
            else
            {
                log.LogDebug("Reusing shader package {shpkPath}", shpkPath);
            }
            
            var output = new MaterialBuilder(Path.GetFileNameWithoutExtension(mtrlPath))
                     .WithMetallicRoughnessShader()
                     .WithBaseColor(Vector4.One);
            
            foreach (var (offset, texPath) in texturePaths)
            {
                if (imageCache.ContainsKey(texPath)) continue;
                ComposeTexture(texPath);
            }
            
            var setTypes = new HashSet<TextureUsage>();
            foreach (var sampler in mtrlFile.Samplers)
            {
                if (sampler.TextureIndex == byte.MaxValue) continue;
                var textureInfo = mtrlFile.TextureOffsets[sampler.TextureIndex];
                var texturePath = texturePaths[textureInfo.Offset];
                if (!imageCache.TryGetValue(texturePath, out var tex)) continue;
                // bg textures can have additional textures, which may be dummy textures, ignore them
                if (texturePath.Contains("dummy_")) continue;
                if (!shaderPackage.TextureLookup.TryGetValue(sampler.SamplerId, out var usage))
                {
                    log.LogWarning("Unknown texture usage for texture {texturePath} ({textureUsage})", texturePath, (TextureUsage)sampler.SamplerId);
                    continue;
                }
            
                var channel = MaterialUtility.MapTextureUsageToChannel(usage);
                if (channel != null && setTypes.Add(usage))
                {
                    var fileName = $"{Path.GetFileNameWithoutExtension(texturePath)}_{usage}_{shaderPackage.Name}";
                    var imageBuilder = ImageBuilder.From(tex.MemoryImage, fileName);
                    imageBuilder.AlternateWriteFileName = $"{fileName}.*";
                    output.WithChannelImage(channel.Value, imageBuilder);
                }
                else if (channel != null)
                {
                    log.LogWarning("Ignoring texture {texturePath} with usage {usage}", texturePath, usage);
                }
                else
                {
                    log.LogWarning("Unknown texture usage {usage} for texture {texturePath}", usage, texturePath);
                }
            }
            
            materialBuilders.Add(output);
        }

        var model = new Model(bgPartsInstance.Path, mdlFile, null);
        var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, [], null);
        return meshes;
    }
    
    private void ComposeTexture(string texPath)
    {
        var texData = dataManager.GetFile(texPath);
        if (texData == null) throw new Exception($"Failed to load texture file: {texPath}");
        log.LogInformation("Loaded texture {texPath}", texPath);
        var texFile = new TexFile(texData.Data);
        var diskPath = Path.Combine(CacheDir, Path.GetDirectoryName(texPath) ?? "",
                                    Path.GetFileNameWithoutExtension(texPath)) + ".png";
        var texture = Texture.GetResource(texFile).ToTexture();
        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
            textureBytes = memoryStream.ToArray();
        }

        var dirPath = Path.GetDirectoryName(diskPath);
        if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

        File.WriteAllBytes(diskPath, textureBytes);
        imageCache.TryAdd(texPath, (diskPath, new MemoryImage(() => File.ReadAllBytes(diskPath))));
    }
}
