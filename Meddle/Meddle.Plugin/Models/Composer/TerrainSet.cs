using System.Numerics;
using Dalamud.Plugin.Services;
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

public class TerrainSet
{
    public TerrainSet(ILogger log, IDataManager manager, string terrainDir, string? cacheDir = null, Action<ProgressEvent>? progress = null, CancellationToken cancellationToken = default)
    {
        CacheDir = cacheDir ?? Path.GetTempPath();
        Directory.CreateDirectory(CacheDir);
        TerrainDir = terrainDir;
        this.log = log;
        dataManager = manager;
        this.progress = progress;
        this.cancellationToken = cancellationToken;
        var teraPath = $"{TerrainDir}/bgplate/terrain.tera";
        var teraData = dataManager.GetFile(teraPath);
        if (teraData == null) throw new Exception($"Failed to load terrain file: {teraPath}");
        teraFile = new TeraFile(teraData.Data);
    }

    private readonly ILogger log;
    private readonly IDataManager dataManager;
    private readonly Action<ProgressEvent>? progress;
    private readonly CancellationToken cancellationToken;
    public int PlateCount => (int)teraFile.Header.PlateCount;
    public int Progress { get; private set; }

    public string TerrainDir { get; }
    public string CacheDir { get; }

    private readonly Dictionary<string, (string PathOnDisk, MemoryImage MemoryImage)> imageCache = new();
    private TeraFile teraFile;

    public void Compose(SceneBuilder scene)
    {
        progress?.Invoke(new ProgressEvent(0, PlateCount));
        var terrainRoot = new NodeBuilder(TerrainDir);
        scene.AddNode(terrainRoot);

        for (var i = 0; i < teraFile.Header.PlateCount; i++)
        {
            if (cancellationToken.IsCancellationRequested) return;
            log.LogInformation("Parsing plate {i}", i);
            var platePos = teraFile.GetPlatePosition(i);
            var plateTransform =
                new Transform(new Vector3(platePos.X, 0, platePos.Y), Quaternion.Identity, Vector3.One);
            var meshes = ComposePlate(i);
            var plateRoot = new NodeBuilder($"Plate{i:D4}");
            foreach (var mesh in meshes)
            {
                scene.AddRigidMesh(mesh.Mesh, plateRoot, plateTransform.AffineTransform);
            }

            terrainRoot.AddNode(plateRoot);
            Progress = i;
            progress?.Invoke(new ProgressEvent(i, PlateCount));
        }
    }

    private IReadOnlyList<ModelBuilder.MeshExport> ComposePlate(int i)
    {
        var mdlPath = $"{TerrainDir}/bgplate/{i:D4}.mdl";
        var mdlData = dataManager.GetFile(mdlPath);
        if (mdlData == null) throw new Exception($"Failed to load model file: {mdlPath}");
        log.LogInformation("Loaded model {mdlPath}", mdlPath);
        var mdlFile = new MdlFile(mdlData.Data);
        var materials = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();

        var materialBuilders = new List<MaterialBuilder>();
        foreach (var mtrlPath in materials)
        {
            var materialBuilder = ComposeMaterial(mtrlPath);
            materialBuilders.Add(materialBuilder);
        }

        var model = new Model(mdlPath, mdlFile, null);
        var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, [], null);
        return meshes;
    }

    private MaterialBuilder ComposeMaterial(string path)
    {
        var mtrlData = dataManager.GetFile(path);
        if (mtrlData == null) throw new Exception($"Failed to load material file: {path}");
        log.LogInformation("Loaded material {path}", path);

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

        var output = new MaterialBuilder(Path.GetFileNameWithoutExtension(path))
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
        
        return output;
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
