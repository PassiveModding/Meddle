using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Plugin.Models.Composer.Textures;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace Meddle.Plugin.Models.Composer;

public class ExportProgress
{
    public ExportProgress(int total, string? name)
    {
        Total = total;
        Name = name;
    }
    
    public int Progress;
    public int Total;
    public bool IsComplete;
    public string? Name;
    
    public List<ExportProgress> Children = [];
}

public class InstanceComposer
{
    private readonly Configuration config;
    private readonly SqPack pack;
    private readonly Configuration.ExportConfiguration exportConfig;
    private readonly string outDir;
    private readonly string cacheDir;
    private readonly CancellationToken cancellationToken;
    private readonly ComposerCache composerCache;
    
    public InstanceComposer(
        Configuration config,
        SqPack pack,
        Configuration.ExportConfiguration exportConfig,
        string outDir,
        CancellationToken cancellationToken)
    {
        this.config = config;
        this.pack = pack;
        this.exportConfig = exportConfig;
        this.outDir = outDir;
        Directory.CreateDirectory(outDir);
        this.cacheDir = Path.Combine(outDir, "cache");
        Directory.CreateDirectory(cacheDir);
        this.cancellationToken = cancellationToken;
        this.composerCache = new ComposerCache(pack, cacheDir, exportConfig);
    }

    private void SaveScene(SceneBuilder scene, string path)
    {
        try
        {
            Plugin.Logger?.LogInformation("Saving scene to {Path}", path);
            var modelRoot = scene.ToGltf2();
            modelRoot.SaveGLTF(path, new WriteSettings
            {
                Validation = ValidationMode.TryFix,
                JsonIndented = false,
            });
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError(ex, "Failed to save scene to {Path}\n{Message}", path, ex.Message);
        }
    }
    
    private void SaveRemainingScenes(ParsedInstanceType groupKey, int lastSceneIdx, int totalInstances, Dictionary<SceneBuilder, SceneStats> scenes)
    {
        foreach (var (sceneBuilder, stats) in scenes)
        {
            if (stats is {Saved: false, Instances: > 0})
            {
                SaveScene(sceneBuilder, Path.Combine(outDir, $"{groupKey}_{lastSceneIdx:D4}-{totalInstances:D4}.gltf"));
                stats.Saved = true;
            }
        }
    }

    private sealed class SceneStats
    {
        public int Instances { get; set; }
        public bool Saved { get; set; }
    }
    
    private void ComposeInstanceGroup(IGrouping<ParsedInstanceType, ParsedInstance> group, ExportProgress progress)
    {
        var scenes = new Dictionary<SceneBuilder, SceneStats>();
        var scene = new SceneBuilder();
        scenes.Add(scene, new SceneStats());

        var orderedInstances = group.OrderBy(x => x.Transform.Translation.LengthSquared()).ToArray();
        var lastSceneIdx = 0;

        for (var i = 0; i < orderedInstances.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            progress.Progress++;
            try
            {
                if (ComposeInstance(orderedInstances[i], scene, progress) != null)
                {
                    var stats = scenes[scene];
                    stats.Instances++;
                    if (stats.Instances > 100 || scene.Instances.Count > 100)
                    {
                        Plugin.Logger?.LogDebug("Saving scene {key} {startIdx:D4}-{endIdx:D4} Instances: {instances} Nodes: {nodes}", 
                                                group.Key, lastSceneIdx, i, stats.Instances, scene.Instances.Count);
                        SaveScene(scene, Path.Combine(outDir, $"{group.Key}_{lastSceneIdx:D4}-{i:D4}.gltf"));
                        scenes[scene].Saved = true;
                        lastSceneIdx = i;
        
                        scene = new SceneBuilder();
                        scenes.Add(scene, new SceneStats());
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var blob = JsonSerializer.Serialize(orderedInstances[i], MaterialComposer.JsonOptions);
                    Plugin.Logger?.LogError(ex, 
                                            "Failed to compose instance {instance} {instanceType}\n{Message}\b{Blob}", 
                                            orderedInstances[i].Id, 
                                            orderedInstances[i].Type, 
                                            ex.Message, blob);
                }
                catch (Exception ex2)
                {
                    Plugin.Logger?.LogError(new AggregateException(ex, ex2), 
                                            "Failed to compose instance {instance} {instanceType}\n{Message}", 
                                            orderedInstances[i].Id, 
                                            orderedInstances[i].Type,
                        ex.Message);
                }
            }
        }

        SaveRemainingScenes(group.Key, lastSceneIdx, orderedInstances.Length, scenes);
    }
    
    public void Compose(ParsedInstance[] instances, ExportProgress progress)
    {
        progress.Total = instances.Length;
        progress.Progress = 0;
        try
        {
            var instanceBlob = JsonSerializer.Serialize(instances, MaterialComposer.JsonOptions);
            File.WriteAllText(Path.Combine(outDir, "instance_blob.json"), instanceBlob);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError(ex, "Failed to save instance blob\n{Message}", ex.Message);
        }
        
        SaveArrayTextures();

        var instanceGroups = instances.GroupBy(x => x.Type);
        
        foreach (var group in instanceGroups)
        {
            ComposeInstanceGroup(group, progress);
        }
        
        Plugin.Logger?.LogInformation("Finished composing instances");
    }
    
    public void SaveArrayTextures()
    {
        Directory.CreateDirectory(cacheDir);
        ArrayTextureUtil.SaveSphereTextures(pack, cacheDir);
        ArrayTextureUtil.SaveTileTextures(pack, cacheDir);
        ArrayTextureUtil.SaveBgSphereTextures(pack, cacheDir);
        ArrayTextureUtil.SaveBgDetailTextures(pack, cacheDir);
    }

    public NodeBuilder? ComposeInstance(ParsedInstance parsedInstance, SceneBuilder scene, ExportProgress rootProgress)
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
            return ComposeSharedInstance(parsedSharedInstance, scene, rootProgress);
        }
        
        if (parsedInstance is ParsedCharacterInstance parsedCharacterInstance)
        {
            return ComposeCharacterInstance(parsedCharacterInstance, scene, rootProgress);
        }
        
        return null;
    }

    public NodeBuilder? ComposeCharacterInstance(ParsedCharacterInstance instance, SceneBuilder scene, ExportProgress rootProgress)
    {
        if (instance.CharacterInfo == null)
        {
            Plugin.Logger?.LogWarning("Character instance {InstanceId} has no character info", instance.Id);
            return null;
        }
        var characterComposer = new CharacterComposer(config, pack, composerCache, exportConfig, cancellationToken);
        var root = new NodeBuilder($"{instance.Type}_{instance.Name}_{instance.Id}");
        
        var characterProgress = new ExportProgress(instance.CharacterInfo.Models.Length, "Character Meshes");
        rootProgress.Children.Add(characterProgress);
        
        try
        {
            characterComposer.Compose(instance.CharacterInfo, scene, root, characterProgress);
            root.SetLocalTransform(instance.Transform.AffineTransform, true);
        } 
        finally
        {
            characterProgress.IsComplete = true;
        }
        return root;
    }
    
    public NodeBuilder? ComposeSharedInstance(ParsedSharedInstance instance, SceneBuilder scene, ExportProgress rootProgress)
    {
        var sharedGroupProgress = new ExportProgress(instance.Children.Count, "Shared Group");
        rootProgress.Children.Add(sharedGroupProgress);
        
        var root = new NodeBuilder($"{instance.Type}_{instance.Path.GamePath}");
        try 
        {        
            bool validChild = false;
            foreach (var child in instance.Children)
            {
                var childNode = ComposeInstance(child, scene, sharedGroupProgress);
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
        finally
        {
            sharedGroupProgress.IsComplete = true;
        }
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
    
    public NodeBuilder ComposeTerrain(ParsedTerrainInstance terrainInstance, SceneBuilder scene)
    {
        var root = new NodeBuilder($"{terrainInstance.Type}_{terrainInstance.Path.GamePath}");
        var teraPath = $"{terrainInstance.Path.GamePath}/bgplate/terrain.tera";
        var teraData = pack.GetFileOrReadFromDisk(teraPath);
        if (teraData == null) throw new Exception($"Failed to load terrain file: {teraPath}");
        var teraFile = new TeraFile(teraData);

        for (var i = 0; i < teraFile.Header.PlateCount; i++)
        {
            Plugin.Logger?.LogInformation("Parsing plate {i}", i);
            var platePos = teraFile.GetPlatePosition(i);
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
