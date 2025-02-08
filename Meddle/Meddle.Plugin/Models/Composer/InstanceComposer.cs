using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Plugin.Models.Composer.Materials;
using Meddle.Plugin.Models.Composer.Textures;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using SkiaSharp;

namespace Meddle.Plugin.Models.Composer;

public class ComposerCache
{
    private readonly PbdFile defaultPbdFile;
    private readonly ConcurrentDictionary<string, ShaderPackage> shpkCache = new();
    private readonly ConcurrentDictionary<string, MtrlFile> mtrlCache = new();
    private readonly ConcurrentDictionary<string, PbdFile> pbdCache = new();
    private readonly SqPack pack;
    private readonly string cacheDir;

    public ComposerCache(SqPack pack, string cacheDir)
    {
        this.pack = pack;
        this.cacheDir = cacheDir;
        var pbdData = pack.GetFileOrReadFromDisk("chara/xls/boneDeformer/human.pbd");
        if (pbdData == null) throw new InvalidOperationException("Failed to load default pbd file");
        defaultPbdFile = new PbdFile(pbdData);
    }
    
    public PbdFile GetDefaultPbdFile()
    {
        return defaultPbdFile;
    }
    
    public PbdFile GetPbdFile(string path)
    {
        return pbdCache.GetOrAdd(path, key =>
        {
            var pbdData = pack.GetFileOrReadFromDisk(path);
            if (pbdData == null) throw new Exception($"Failed to load pbd file: {path}");
            return new PbdFile(pbdData);
        });
    }
    
    public MtrlFile GetMtrlFile(string path)
    {
        return mtrlCache.GetOrAdd(path, key =>
        {
            var mtrlData = pack.GetFileOrReadFromDisk(path);
            if (mtrlData == null) throw new Exception($"Failed to load material file: {path}");
            return new MtrlFile(mtrlData);
        });
    }
    
    public ShaderPackage GetShaderPackage(string shpkName)
    {
        var shpkPath = $"shader/sm5/shpk/{shpkName}";
        return shpkCache.GetOrAdd(shpkPath, key =>
        {
            var shpkData = pack.GetFileOrReadFromDisk(shpkPath);
            if (shpkData == null) throw new Exception($"Failed to load shader package file: {shpkPath}");
            var shpkFile = new ShpkFile(shpkData);
            return new ShaderPackage(shpkFile, shpkName);
        });
    }

    private string CacheTexture(string fullPath)
    {
        var cleanPath = fullPath.TrimHandlePath();
        if (Path.IsPathRooted(cleanPath))
        {
            var pathRoot = Path.GetPathRoot(cleanPath) ?? string.Empty;
            cleanPath = cleanPath[pathRoot.Length..];
        }
        
        var cachePath = Path.Combine(cacheDir, cleanPath) + ".png";
        if (File.Exists(cachePath)) return cachePath;
        var texFile = pack.GetFileOrReadFromDisk(fullPath);
        if (texFile == null) throw new Exception($"Failed to load texture file: {fullPath}");
        
        var tex = new TexFile(texFile);
        var texture = tex.ToResource().ToTexture();
        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
            textureBytes = memoryStream.ToArray();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        File.WriteAllBytes(cachePath, textureBytes);
        return cachePath;
    }

    public MaterialBuilder ComposeMaterial(string mtrlPath, 
       ParsedMaterialInfo? materialInfo = null,
       ParsedInstance? instance = null, 
       ParsedCharacterInfo? characterInfo = null, 
       IColorTableSet? colorTableSet = null)
    {
        var mtrlFile = GetMtrlFile(mtrlPath);
        var shaderPackage = GetShaderPackage(mtrlFile.GetShaderPackageName());
        var material = new MaterialComposer(mtrlFile, mtrlPath, shaderPackage);
        if (instance != null)
        {
            material.SetPropertiesFromInstance(instance);
        }
        
        if (characterInfo != null)
        {
            material.SetPropertiesFromCharacterInfo(characterInfo);
        }
        
        if (colorTableSet != null)
        {
            material.SetPropertiesFromColorTable(colorTableSet);
        }
       
        var materialBuilder = new RawMaterialBuilder(mtrlPath);
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
            material.SetProperty($"{texture.Key}_PngCachePath", cachePath);
        }

        materialBuilder.Extras = material.ExtrasNode;
        return materialBuilder;
    }
}

public class MeddleComposer
{
    private readonly Configuration config;
    private readonly SqPack pack;
    private readonly string outDir;
    private readonly string cacheDir;
    private readonly ParsedInstance[] instances;
    private readonly CancellationToken cancellationToken;
    private readonly ComposerCache composerCache;
    public MeddleComposer(Configuration config, SqPack pack, string outDir, ParsedInstance[] instances, CancellationToken cancellationToken)
    {
        this.config = config;
        this.pack = pack;
        this.outDir = outDir;
        Directory.CreateDirectory(outDir);
        this.cacheDir = Path.Combine(outDir, "cache");
        Directory.CreateDirectory(cacheDir);
        this.instances = instances;
        this.cancellationToken = cancellationToken;
        this.composerCache = new ComposerCache(pack, cacheDir);
    }

    public void Compose()
    {
        SaveArrayTextures();

        var orderedInstances = instances.OrderBy(x => x.Transform.Translation.LengthSquared()).ToArray();
        var scenes = new List<SceneBuilder>();
        var scene = new SceneBuilder();
        
        for (var i = 0; i < orderedInstances.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                if (ComposeInstance(orderedInstances[i], scene) != null)
                {
                    if (scene.Instances.Count > 100)
                    {
                        scenes.Add(scene);
                        scene = new SceneBuilder();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(ex, "Failed to compose instance {instance} {instanceType}\n{Message}", orderedInstances[i].Id, orderedInstances[i].Type, ex.Message);
            }
        }
        
        if (scene.Instances.Count > 0)
        {
            scenes.Add(scene);
        }
        
        for (var i = 0; i < scenes.Count; i++)
        {
            var currentScene = scenes[i];
            var scenePath = Path.Combine(outDir, $"scene_{i:D4}.gltf");
            var modelRoot = currentScene.ToGltf2();
            modelRoot.SaveGLTF(scenePath, new WriteSettings
            {
                Validation = ValidationMode.TryFix,
                JsonIndented = false,
            });
        }
    }
    
    public void SaveArrayTextures()
    {
        Directory.CreateDirectory(cacheDir);
        ArrayTextureUtil.SaveSphereTextures(pack, cacheDir);
        ArrayTextureUtil.SaveTileTextures(pack, cacheDir);
        ArrayTextureUtil.SaveBgSphereTextures(pack, cacheDir);
        ArrayTextureUtil.SaveBgDetailTextures(pack, cacheDir);
    }

    public NodeBuilder? ComposeInstance(ParsedInstance parsedInstance, SceneBuilder scene)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        if (parsedInstance is ParsedLightInstance parsedLightInstance)
        {
            return ComposeLight(parsedLightInstance, scene);
        }
        
        if (parsedInstance is ParsedTerrainInstance parsedTerrainInstance)
        {
            return ComposeTerrain(parsedTerrainInstance, scene);
        }
        
        if (parsedInstance is ParsedBgPartsInstance parsedBgPartsInstance)
        {
            return ComposeBgPartsInstance(parsedBgPartsInstance, scene);
        }
        
        if (parsedInstance is ParsedSharedInstance parsedSharedInstance)
        {
            return ComposeSharedInstance(parsedSharedInstance, scene);
        }
        
        if (parsedInstance is ParsedCharacterInstance parsedCharacterInstance)
        {
            return ComposeCharacterInstance(parsedCharacterInstance, scene);
        }
        
        return null;
    }

    public NodeBuilder? ComposeCharacterInstance(ParsedCharacterInstance instance, SceneBuilder scene)
    {
        if (instance.CharacterInfo == null)
        {
            Plugin.Logger?.LogWarning("Character instance {InstanceId} has no character info", instance.Id);
            return null;
        }
        var characterComposer = new CharacterComposer(config, pack, composerCache, cancellationToken);
        var root = new NodeBuilder($"{instance.Type}_{instance.Name}_{instance.Id}");
        characterComposer.ComposeCharacterInfo(instance.CharacterInfo, null, scene, root);
        
        root.SetLocalTransform(instance.Transform.AffineTransform, true);
        return root;
    }
    
    public NodeBuilder? ComposeSharedInstance(ParsedSharedInstance instance, SceneBuilder scene)
    {
        var root = new NodeBuilder($"{instance.Type}_{instance.Path.GamePath}");
        bool validChild = false;
        foreach (var child in instance.Children)
        {
            var childNode = ComposeInstance(child, scene);
            if (childNode != null)
            {
                root.AddNode(childNode);
                validChild = true;
            }
        }
        
        if (!validChild) return null;
        root.SetLocalTransform(instance.Transform.AffineTransform, true);
        return root;
    }
    
    
    public NodeBuilder? ComposeBgPartsInstance(ParsedBgPartsInstance bgPartsInstance, SceneBuilder scene)
    {
        var mdlData = pack.GetFileOrReadFromDisk(bgPartsInstance.Path.FullPath);
        if (mdlData == null)
        {
            Plugin.Logger?.LogWarning("Failed to load model file: {Path}", bgPartsInstance.Path.FullPath);
            return null;
        }

        var mdlFile = new MdlFile(mdlData);
        var materials = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();

        var materialBuilders = new List<MaterialBuilder>();
        foreach (var mtrlPath in materials)
        {
            var output = composerCache.ComposeMaterial(mtrlPath, instance: bgPartsInstance);
            materialBuilders.Add(output);
        }

        var model = new Model(bgPartsInstance.Path.GamePath, mdlFile, null);
        var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, [], null);
        
        var root = new NodeBuilder($"{bgPartsInstance.Type}_{bgPartsInstance.Path.GamePath}");
        for (var meshIdx = 0; meshIdx < meshes.Count; meshIdx++)
        {
            var mesh = meshes[meshIdx];
            mesh.Mesh.Name = $"{bgPartsInstance.Path.GamePath}_{meshIdx}";
            scene.AddRigidMesh(mesh.Mesh, root, Matrix4x4.Identity);
        }
        
        root.SetLocalTransform(bgPartsInstance.Transform.AffineTransform, true);
        return root;
    }
    
    public NodeBuilder? ComposeTerrain(ParsedTerrainInstance terrainInstance, SceneBuilder scene)
    {
        var root = new NodeBuilder($"{terrainInstance.Type}_{terrainInstance.Path.GamePath}");
        var teraPath = $"{terrainInstance.Path.GamePath}/bgplate/terrain.tera";
        var teraData = pack.GetFileOrReadFromDisk(teraPath);
        if (teraData == null) throw new Exception($"Failed to load terrain file: {teraPath}");
        var teraFile = new TeraFile(teraData);

        for (var i = 0; i < teraFile.Header.PlateCount; i++)
        {
            Plugin.Logger?.LogInformation("Parsing plate {i}", i);
            var platePos = teraFile.GetPlatePosition((int)i);
            var plateTransform = new Transform(new Vector3(platePos.X, 0, platePos.Y), Quaternion.Identity, Vector3.One);
            var mdlPath = $"{terrainInstance.Path.GamePath}/bgplate/{i:D4}.mdl";
            var mdlData = pack.GetFileOrReadFromDisk(mdlPath);
            if (mdlData == null)
            {
                throw new Exception($"Failed to load model file {mdlPath} returned null");
            }
            
            Plugin.Logger?.LogInformation("Loaded model {mdlPath}", mdlPath);
            var mdlFile = new MdlFile(mdlData);

            var materials = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();
            var materialBuilders = new List<MaterialBuilder>();
            foreach (var mtrlPath in materials)
            {
                var materialBuilder = composerCache.ComposeMaterial(mtrlPath, instance: terrainInstance);
                materialBuilders.Add(materialBuilder);
            }

            var model = new Model(mdlPath, mdlFile, null);
            var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, [], null);

            var plateRoot = new NodeBuilder(mdlPath);
            for (var meshIdx = 0; meshIdx < meshes.Count; meshIdx++)
            {
                var mesh = meshes[meshIdx];
                mesh.Mesh.Name = $"{mdlPath}_{meshIdx}";
                scene.AddRigidMesh(mesh.Mesh, plateRoot);
            }

            plateRoot.SetLocalTransform(plateTransform.AffineTransform, true);
            root.AddNode(plateRoot);
        }
        
        root.SetLocalTransform(terrainInstance.Transform.AffineTransform, true);
        return root;
    }
    
    public NodeBuilder? ComposeLight(ParsedLightInstance instance, SceneBuilder scene)
    {
        if (instance.Light.Range <= 0)
        {
            Plugin.Logger?.LogWarning("Light {LightId} has a range of 0 or less ({Range})", instance.Id, instance.Light.Range);
            return null;
        }
        
        var root = new NodeBuilder();
        var transform = instance.Transform;
        
        // idk if its blender, sharpgltf or game engine stuff but flip the rotation for lights (only tested spot though)
        var rotation = transform.Rotation;
        rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
                
        transform = transform with {Rotation = rotation};
        var light = instance.Light;
        
        root.Name = $"{instance.Type}_{light.LightType}_{instance.Id}";

        LightBuilder? lightBuilder;
        switch (light.LightType)
        {
            case LightType.Directional:
                lightBuilder = new LightBuilder.Directional
                {
                    Color = light.Color.Rgb,
                    Intensity = LuxIntensity(light.Color.HdrIntensity),
                    Name = root.Name,
                };
                break;
            case LightType.PointLight:
            // TODO: Capsule and area dont belong here but there isn't a gltf equivalent
            case LightType.CapsuleLight:
            case LightType.AreaLight:
                lightBuilder = new LightBuilder.Point
                {
                    Color = light.Color.Rgb,
                    Intensity = CandelaIntensity(light.Color.HdrIntensity),
                    Range = light.Range,
                    Name = root.Name
                };
                break;
            case LightType.SpotLight:
                var (outerConeAngle, innerConeAngle) = FixSpotLightAngles(
                    DegreesToRadians(light.LightAngle + light.FalloffAngle), 
                    DegreesToRadians(light.LightAngle));
                
                lightBuilder = new LightBuilder.Spot
                {
                    Color = light.Color.Rgb,
                    Intensity = CandelaIntensity(light.Color.HdrIntensity),
                    Range = light.Range,
                    InnerConeAngle = innerConeAngle,
                    OuterConeAngle = outerConeAngle,
                    Name = root.Name
                };
                break;
            default:
                Plugin.Logger?.LogWarning("Unsupported light type: {LightType}", light.LightType);
                return null;
        }

        var extras = new Dictionary<string, object>()
        {
            { "LightType", light.LightType.ToString() },
            { "Range", light.Range },
            { "FalloffType", light.FalloffType },
            { "Falloff", light.Falloff },
            { "ShadowFar", light.ShadowFar },
            { "ShadowNear", light.ShadowNear },
            { "CharaShadowRange", light.CharaShadowRange },
            { "LightAngle", light.LightAngle },
            { "FalloffAngle", light.FalloffAngle },
            { "AreaAngle", light.AreaAngle },
            { "ColorHDR", light.Color._vec3 },
            { "ColorRGB", light.Color.Rgb },
            { "Intensity", light.Color.Intensity },
            { "BoundsMin", light.Bounds.Min },
            { "BoundsMax", light.Bounds.Max },
            { "Flags", light.Flags },
        };

        foreach (var lightFlag in Enum.GetValues<LightFlags>())
        {
            extras.Add(lightFlag.ToString(), light.Flags.HasFlag(lightFlag));
        }
        
        // doesn't appear to set extras on the light itself
        lightBuilder.Extras = JsonNode.Parse(JsonSerializer.Serialize(extras, MaterialComposer.JsonOptions));
        
        root.Extras = JsonNode.Parse(JsonSerializer.Serialize(extras, MaterialComposer.JsonOptions));
        
        scene.AddLight(lightBuilder, root);
        root.SetLocalTransform(transform.AffineTransform, true);
        
        return root;
        
        // directional lights use illuminance in lux (lm/ m2)
        float LuxIntensity(float intensity)
        {
            return intensity;
        }
    
        // Point and spot lights use luminous intensity in candela (lm/ sr)
        float CandelaIntensity(float intensity)
        {
            return intensity * 100f;
        }
    
        float DegreesToRadians(float degrees)
        {
            return degrees * (MathF.PI / 180f);
        }
        
        (float outer, float inner) FixSpotLightAngles(float outerConeAngle, float innerConeAngle)
        {                    
            // inner must be less than or equal to outer
            // outer (due to blender bug and sharpgltf removing if the value is equal to the default) needs to be greater than inner and not equal to pi / 4
            // TODO: https://github.com/KhronosGroup/glTF-Blender-IO/issues/2349
            if (innerConeAngle > outerConeAngle)
            {
                throw new Exception("Inner cone angle must be less than or equal to outer cone angle");
            }
        
            if (MathF.Abs(outerConeAngle - (MathF.PI / 4f)) < 0.0001f)
            {
                outerConeAngle = (MathF.PI / 4f) - 0.0001f;
            }
        
            outerConeAngle = Math.Clamp(outerConeAngle, 0.0002f, (MathF.PI / 2f) - 0.0001f);
            innerConeAngle = Math.Clamp(innerConeAngle, 0.0001f, outerConeAngle  - 0.0001f);
        
            return (outerConeAngle, innerConeAngle);
        }
    }
}
