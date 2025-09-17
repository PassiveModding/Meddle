using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Models.Composer;

public class ExportProgress
{
    public ExportProgress(int total, string? name)
    {
        Total = total;
        Name = name;
    }

    private int progress;
    public int Progress { get { return progress; } }
    public void IncrementProgress(int amount = 1)
    {
        Interlocked.Add(ref progress, amount);
        Parent?.IncrementProgress(amount);
    }
    public int Total;
    public bool IsComplete;
    public string? Name;
    public ExportProgress? Parent;
    
    public readonly ConcurrentBag<ExportProgress> Children = [];
}

public class InstanceComposer
{
    private readonly SqPack pack;
    private readonly Configuration.ExportConfiguration exportConfig;
    private readonly string outDir;
    private readonly string cacheDir;
    private readonly CancellationToken cancellationToken;
    private readonly ComposerCache composerCache;
    
    public InstanceComposer(
        SqPack pack,
        Configuration.ExportConfiguration exportConfig,
        string outDir,
        CancellationToken cancellationToken)
    {
        this.pack = pack;
        this.exportConfig = exportConfig;
        this.outDir = outDir;
        Directory.CreateDirectory(outDir);
        this.cacheDir = Path.Combine(outDir, "cache");
        Directory.CreateDirectory(cacheDir);
        this.cancellationToken = cancellationToken;
        this.composerCache = new ComposerCache(pack, cacheDir, exportConfig);
    }

    private void SaveScene(SceneBuilder scene, string name)
    {
        var path = outDir;
        try
        {
            Plugin.Logger.LogInformation("Saving scene to {Path}", path);
            var modelRoot = scene.ToGltf2();
            ExportUtil.SaveAsType(modelRoot, exportConfig.ExportType, path, name);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError(ex, "Failed to save scene to {Path}\n{Message}", path, ex.Message);
        }
    }
    
    private void SaveRemainingScenes(ParsedInstanceType groupKey, int lastSceneIdx, int totalInstances, Dictionary<SceneBuilder, SceneStats> scenes)
    {
        foreach (var (sceneBuilder, stats) in scenes)
        {
            if (stats is {Saved: false, Instances: > 0})
            {
                SaveScene(sceneBuilder, $"{groupKey}_{lastSceneIdx:D4}-{totalInstances:D4}");
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
        ParsedInstanceType[] pagedGroups =
        [
            ParsedInstanceType.Character
        ];

        if (pagedGroups.Contains(group.Key))
        {
            var scenes = new Dictionary<SceneBuilder, SceneStats>();
            var scene = new SceneBuilder();
            scenes.Add(scene, new SceneStats());
            var lastSceneIdx = 0;
            var orderedInstances = group.OrderBy(x => x.Transform.Translation.LengthSquared()).ToArray();
            for (int i = 0; i < orderedInstances.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                progress.IncrementProgress();
                try
                {
                    if (ComposeInstance(orderedInstances[i], scene, progress) != null)
                    {
                        var stats = scenes[scene];
                        stats.Instances++;
                        if (stats.Instances > 100 || scene.Instances.Count > 100)
                        {
                            Plugin.Logger.LogDebug("Saving scene {key} {startIdx:D4}-{endIdx:D4} Instances: {instances} Nodes: {nodes}", 
                                                   group.Key, lastSceneIdx, i, stats.Instances, scene.Instances.Count);
                            SaveScene(scene, $"{group.Key}_{lastSceneIdx:D4}-{i:D4}");
                            scenes[scene].Saved = true;
                            lastSceneIdx = i;
        
                            scene = new SceneBuilder();
                            scenes.Add(scene, new SceneStats());
                        }
                    }
                }
                catch (Exception ex)
                { 
                    TryLogInstanceComposeError(orderedInstances[i], ex);
                }
            }
            
            SaveRemainingScenes(group.Key, lastSceneIdx, orderedInstances.Length, scenes);
        }
        else
        {
            var namedInstanceGroupings = group.GroupBy(x =>
            {
                if (x is ParsedBgPartsInstance bg)
                {
                    return bg.Path.GamePath;
                }
                if (x is ParsedSharedInstance sh)
                {
                    return sh.Path.GamePath;
                }
                if (x is ParsedTerrainInstance tr)
                {
                    return tr.Path.GamePath;
                }

                return string.Empty;
            }).ToList();
            
            var scenes = new Dictionary<SceneBuilder, SceneStats>();
            var scene = new SceneBuilder();
            scenes.Add(scene, new SceneStats());
            var lastSceneIdx = 0;
            for (var i = 0; i < namedInstanceGroupings.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                // compile all to scene, paging only at end of processing named groups
                var namedGroup = namedInstanceGroupings[i].ToArray();
                var stats = scenes[scene];
                for (var j = 0; j < namedGroup.Length; j++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    progress.IncrementProgress();
                    try
                    {
                        if (ComposeInstance(namedGroup[j], scene, progress) != null)
                        {
                            stats.Instances++;
                        }
                    }
                    catch (Exception ex)
                    { 
                        TryLogInstanceComposeError(namedGroup[j], ex);
                    }
                }

                if (sceneMeshCache.TryGetValue(scene, out var sceneMeshes))
                {
                    if (sceneMeshes.Count > 300)
                    {
                        SaveSceneGroup(i, ref scene, ref lastSceneIdx, stats, scenes);
                    }
                }
                else if (scene.Instances.Count > 100)
                {
                    SaveSceneGroup(i, ref scene, ref lastSceneIdx, stats, scenes);
                }
            }
            
            SaveRemainingScenes(group.Key, lastSceneIdx, namedInstanceGroupings.Count, scenes);
        }

        void SaveSceneGroup(int i, ref SceneBuilder scene, ref int lastSceneIdx, SceneStats stats, Dictionary<SceneBuilder, SceneStats> scenes)
        {
            Plugin.Logger.LogDebug("Saving scene {key} {startIdx:D4}-{endIdx:D4} Instances: {instances} Nodes: {nodes}", 
                                   group.Key, lastSceneIdx, i, stats.Instances, scene.Instances.Count);
            SaveScene(scene, $"{group.Key}_{lastSceneIdx:D4}-{i:D4}");
            scenes[scene].Saved = true;
            lastSceneIdx = i;

            scene = new SceneBuilder();
            scenes.Add(scene, new SceneStats());
        }
    }
    
    private void TryLogInstanceComposeError(ParsedInstance instance, Exception ex)
    {
        try
        {
            var blob = JsonSerializer.Serialize(instance, MaterialComposer.JsonOptions);
            Plugin.Logger.LogError(ex, 
                                   "Failed to compose instance {instance} {instanceType}\n{Message}\b{Blob}", 
                                   instance.Id, 
                                   instance.Type, 
                                   ex.Message, blob);
        }
        catch (Exception ex2)
        {
            Plugin.Logger.LogError(new AggregateException(ex, ex2), 
                                   "Failed to compose instance {instance} {instanceType}\n{Message}", 
                                   instance.Id, 
                                   instance.Type,
                                   ex.Message);
        }
    }
    
    public void Compose(ParsedInstance[] instances, ProgressWrapper wrapper)
    {
        try
        {
            var instanceBlob = JsonSerializer.Serialize(instances, MaterialComposer.JsonOptions);
            File.WriteAllText(Path.Combine(outDir, "instance_blob.json"), instanceBlob);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError(ex, "Failed to save instance blob\n{Message}", ex.Message);
        }
        
        composerCache.SaveArrayTextures();

        var instanceGroups = instances.GroupBy(x => x.Type);
        // place characters and shared last to reduce shifting in ui
        instanceGroups = instanceGroups
            .OrderByDescending(x => x.Key == ParsedInstanceType.Character)
            .ThenByDescending(x => x.Key == ParsedInstanceType.SharedGroup)
            .ThenBy(x => x.Key)
            .ToArray();
        
        wrapper.Progress = new ExportProgress(instances.Length, "Composing Instances");
        // Parallel.ForEach(instanceGroups, group =>
        // {
        //     if (cancellationToken.IsCancellationRequested) return;
        //     Plugin.Logger.LogInformation("Composing instances of type {InstanceType} ({InstanceCount})", group.Key, group.Count());
        //     var groupProgress = new ExportProgress(group.Count(), $"Composing {group.Key}")
        //     {
        //         Parent = wrapper.Progress
        //     };
        //     wrapper.Progress.Children.Add(groupProgress);
        //     try
        //     {
        //         ComposeInstanceGroup(group, groupProgress);
        //     }
        //     catch (Exception ex)
        //     {
        //         Plugin.Logger.LogError(ex, "Failed to compose instance group {InstanceType}\n{Message}", group.Key, ex.Message);
        //     }
        //     groupProgress.IsComplete = true;
        // });
        foreach (var group in instanceGroups)
        {
            if (cancellationToken.IsCancellationRequested) break;
            Plugin.Logger.LogInformation("Composing instances of type {InstanceType} ({InstanceCount})", group.Key, group.Count());
            var groupProgress = new ExportProgress(group.Count(), $"Composing {group.Key}")
            {
                Parent = wrapper.Progress
            };
            wrapper.Progress.Children.Add(groupProgress);
            try
            {
                ComposeInstanceGroup(group, groupProgress);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(ex, "Failed to compose instance group {InstanceType}\n{Message}", group.Key, ex.Message);
            }
            groupProgress.IsComplete = true;
        }
        
        Plugin.Logger.LogInformation("Finished composing instances");
    }

    private readonly Dictionary<SceneBuilder, HashSet<IMeshBuilder<MaterialBuilder>>> sceneMeshCache = new();
    public NodeBuilder? ComposeInstance(ParsedInstance parsedInstance, SceneBuilder scene, ExportProgress rootProgress)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        if (parsedInstance is ParsedLightInstance parsedLightInstance)
        {
            return ComposeLight(parsedLightInstance, scene);
        }

        if (parsedInstance is ParsedEnvLightInstance parsedEnvLightInstance)
        {
            return ComposeEnvLight(parsedEnvLightInstance, scene);
        }
        
        if (parsedInstance is ParsedTerrainInstance parsedTerrainInstance)
        {
            return ComposeTerrain(parsedTerrainInstance, scene, rootProgress);
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
        
        if (parsedInstance is ParsedCameraInstance parsedCameraInstance)
        {
            return ComposeCameraInstance(parsedCameraInstance, scene);
        }

        if (parsedInstance is ParsedWorldDecalInstance parsedDecalInstance)
        {
            return ComposeDecalInstance(parsedDecalInstance, scene);
        }
        
        return null;
    }
    private NodeBuilder ComposeDecalInstance(ParsedWorldDecalInstance parsedWorldDecalInstance, SceneBuilder scene)
    {
        var root = new NodeBuilder($"{parsedWorldDecalInstance.Type}_{parsedWorldDecalInstance.Id}");
        var cachedDiffuse = Path.GetRelativePath(cacheDir, composerCache.CacheTexture(parsedWorldDecalInstance.Diffuse.FullPath));
        var cachedNormal = Path.GetRelativePath(cacheDir, composerCache.CacheTexture(parsedWorldDecalInstance.Normal.FullPath));
        var cachedSpecular = Path.GetRelativePath(cacheDir, composerCache.CacheTexture(parsedWorldDecalInstance.Specular.FullPath));
        var decalData = new
        {
            DiffusePath = parsedWorldDecalInstance.Diffuse.FullPath,
            DiffuseCachePath = cachedDiffuse,
            NormalPath = parsedWorldDecalInstance.Normal.FullPath,
            NormalCachePath = cachedNormal,
            SpecularPath = parsedWorldDecalInstance.Specular.FullPath,
            SpecularCachePath = cachedSpecular,
        };
        
        root.Extras = JsonNode.Parse(JsonSerializer.Serialize(decalData, MaterialComposer.JsonOptions));
        root.SetLocalTransform(parsedWorldDecalInstance.Transform.AffineTransform, true);
        scene.AddNode(root);
        
        return root;
    }

    private NodeBuilder ComposeCameraInstance(ParsedCameraInstance parsedCameraInstance, SceneBuilder scene)
    {
        var perspective = new CameraBuilder.Perspective(aspectRatio: parsedCameraInstance.AspectRatio, fovy: parsedCameraInstance.FoV, znear: 0.01f, zfar: 1000f);
        var root = new NodeBuilder($"{parsedCameraInstance.Type}_{parsedCameraInstance.Id}")
        {
            LocalTransform = parsedCameraInstance.Transform.AffineTransform
                                                 .WithRotation(parsedCameraInstance.Rotation)
        };
        scene.AddCamera(perspective, root);
        
        // var target = new NodeBuilder($"{parsedCameraInstance.Type}_{parsedCameraInstance.Id}_Target")
        // {
        //     LocalTransform = new Transform(cam->LookAtVector, Quaternion.Identity, Vector3.One).AffineTransform
        // };
        // scene.AddNode(target);
        return root;
    }

    public NodeBuilder? ComposeCharacterInstance(ParsedCharacterInstance instance, SceneBuilder scene, ExportProgress rootProgress)
    {
        if (instance.CharacterInfo == null)
        {
            Plugin.Logger.LogWarning("Character instance {InstanceId} has no character info", instance.Id);
            return null;
        }
        var characterComposer = new CharacterComposer(composerCache, exportConfig, cancellationToken);
        var root = new NodeBuilder($"{instance.Type}_{instance.Name}_{instance.Id}");
        
        var characterProgress = new ExportProgress(instance.CharacterInfo.Models.Count, "Character Meshes");
        rootProgress.Children.Add(characterProgress);
        
        try
        {
            characterComposer.Compose(instance.CharacterInfo, scene, root, characterProgress);
            var cTransform = instance.Transform.AffineTransform;
            if (exportConfig.PoseMode != SkeletonUtils.PoseMode.None)
            {
                // set scale to 1 since exporting with pose should already set this on the root bone.
                cTransform = cTransform.WithScale(Vector3.One);
            }
            root.SetLocalTransform(cTransform, true);
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

        var extrasDict = new Dictionary<string, object>
        {
            { "Type", instance.Type.ToString() },
            { "Id", instance.Id },
            { "Path", instance.Path.GamePath },
        };
        
        string name = $"{instance.Type}";
        if (instance is ParsedHousingInstance ho)
        {
            name += $"_{ho.Name}";
            extrasDict.Add("Name", ho.Name);
        }
        else
        {
            name += $"_{instance.Path.GamePath}";
        }
        
        var root = new NodeBuilder(name);
        try 
        {        
            bool validChild = false;
            foreach (var child in instance.Children)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var childNode = ComposeInstance(child, scene, sharedGroupProgress);
                if (childNode != null)
                {
                    root.AddNode(childNode);
                    validChild = true;
                }
            }
        
            if (!validChild) return null;
            root.SetLocalTransform(instance.Transform.AffineTransform
                                           .WithScale(Vector3.One), true);
            
            
            if (instance.Transform.Scale != Vector3.One)
            {
                Plugin.Logger.LogDebug("Shared group {InstanceId} has non-unity scale {Scale}", instance.Id, instance.Transform.Scale);
                extrasDict.Add("RealScale", instance.Transform.Scale);
            }
            
            root.Extras = JsonNode.Parse(JsonSerializer.Serialize(extrasDict, MaterialComposer.JsonOptions));
            
            return root;
        } 
        finally
        {
            sharedGroupProgress.IsComplete = true;
        }
    }
    
    private readonly Dictionary<string, Dictionary<int, MaterialBuilder>> bgPartMaterialCache = new();
    private readonly Dictionary<nint, IMeshBuilder<MaterialBuilder>[]> meshBuilderCache = new();
    
    public NodeBuilder? ComposeBgPartsInstance(ParsedBgPartsInstance bgPartsInstance, SceneBuilder scene)
    {
        if (bgPartsInstance.IsVisible == false && exportConfig.SkipHiddenBgParts)
        {
            Plugin.Logger.LogDebug("BgParts instance {InstanceId} is not visible and export config is set to skip hidden", bgPartsInstance.Id);
            return null;
        }

        var sceneCache = sceneMeshCache.GetValueOrDefault(scene, []);
        sceneMeshCache[scene] = sceneCache;
        IMeshBuilder<MaterialBuilder>[]? meshes = null;
        if (bgPartsInstance is {ModelPtr: not null, Stain: null, BgChangeMaterial: null} && meshBuilderCache.TryGetValue(bgPartsInstance.ModelPtr.Value, out meshes))
        {
            Plugin.Logger.LogDebug("Using cached meshes for BgParts instance {InstanceId}", bgPartsInstance.Id);
        }

        if (meshes == null)
        {
            var mdlData = pack.GetFileOrReadFromDisk(bgPartsInstance.Path.FullPath);
            if (mdlData == null)
            {
                Plugin.Logger.LogWarning("Failed to load model file: {Path}", bgPartsInstance.Path.FullPath);
                return null;
            }

            var mdlFile = new MdlFile(mdlData);
            var bgChangeMaterial = bgPartsInstance.BgChangeMaterial;
            var fileMaterials = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();

            var materialBuilders = new List<MaterialBuilder>();
            for (var i = 0; i < fileMaterials.Length; i++)
            {
                string mtrlPath = fileMaterials[i];
                if (bgChangeMaterial != null && bgChangeMaterial.Value.BGChangeMaterialIndex == i)
                {
                    mtrlPath = bgChangeMaterial.Value.MaterialPath;
                }

                if (!bgPartMaterialCache.TryGetValue(mtrlPath, out var bgPartMtrlCache))
                {
                    bgPartMtrlCache = new Dictionary<int, MaterialBuilder>();
                    bgPartMaterialCache[mtrlPath] = bgPartMtrlCache;
                }

                if (bgPartMtrlCache.TryGetValue((int?)bgPartsInstance.Stain?.RowId ?? -1, out var cachedBuilder))
                {
                    materialBuilders.Add(cachedBuilder);
                }
                else
                {
                    var output = composerCache.ComposeMaterial(mtrlPath, stainInstance: bgPartsInstance);
                    materialBuilders.Add(output);
                }
            }

            var model = new Model(bgPartsInstance.Path.GamePath, mdlFile, null);
            var meshExports = ModelBuilder.BuildMeshes(model, materialBuilders, [], null);
            meshes = meshExports.Select(x => x.Mesh).ToArray();
        }
        
        var root = new NodeBuilder($"{bgPartsInstance.Type}_{bgPartsInstance.Path.GamePath}")
        {
            Extras = JsonNode.Parse(JsonSerializer.Serialize(new
            {
                ModelPath = bgPartsInstance.Path.GamePath,
                ModelName = Path.GetFileNameWithoutExtension(bgPartsInstance.Path.FullPath),
                ModelType = bgPartsInstance.Type,
                IsVisible = bgPartsInstance.IsVisible,
            }, MaterialComposer.JsonOptions))
        };
        if (meshes.Length == 1)
        {
            var mesh = meshes[0];
            scene.AddRigidMesh(mesh, root);
            sceneCache.Add(mesh);
        }
        else
        {
            foreach (var mesh in meshes)
            {
                var meshNode = new NodeBuilder(mesh.Name);
                scene.AddRigidMesh(mesh, meshNode);
                root.AddNode(meshNode);
                sceneCache.Add(mesh);
            }

            scene.AddNode(root);
        }
        
        if (bgPartsInstance is {ModelPtr: not null, Stain: null, BgChangeMaterial: null})
        {
            meshBuilderCache[bgPartsInstance.ModelPtr.Value] = meshes;
        }
        root.SetLocalTransform(bgPartsInstance.Transform.AffineTransform, false);
        return root;
    }
    
    
    private readonly Dictionary<ParsedTerrainInstance, Dictionary<string, MaterialBuilder>> terrainMaterialCache = new();
    public NodeBuilder ComposeTerrain(ParsedTerrainInstance terrainInstance, SceneBuilder scene, ExportProgress rootProgress)
    {
        var root = new NodeBuilder($"{terrainInstance.Type}_{terrainInstance.Path.GamePath}");
        var teraPath = $"{terrainInstance.Path.GamePath}/bgplate/terrain.tera";
        var teraData = pack.GetFileOrReadFromDisk(teraPath);
        if (teraData == null) throw new Exception($"Failed to load terrain file: {teraPath}");
        var teraFile = new TeraFile(teraData);
        
        var terrainProgress = new ExportProgress((int)teraFile.Header.PlateCount, "Terrain Plates");
        rootProgress.Children.Add(terrainProgress);
        var terrainPlates = Enumerable.Range(0, (int)teraFile.Header.PlateCount)
            .Select(i =>
            {
                var platePos = teraFile.GetPlatePosition(i);
                return (i, platePos, Vector2.Distance(new Vector2(terrainInstance.SearchOrigin.X, terrainInstance.SearchOrigin.Z), new Vector2(platePos.X, platePos.Y)));
            })
            .OrderBy(x => x.Item3)
            .ToArray();

        foreach (var (i, platePos, distance) in terrainPlates)
        {
            if (cancellationToken.IsCancellationRequested) break;
            Plugin.Logger.LogInformation("Parsing plate {i}", i);
            var plateTransform = new Transform(new Vector3(platePos.X, 0, platePos.Y), Quaternion.Identity, Vector3.One);
            if (exportConfig.LimitTerrainExportRange)
            {
                var searchOrigin = terrainInstance.SearchOrigin;
                if (distance > exportConfig.TerrainExportDistance)
                {
                    Plugin.Logger.LogDebug("Skipping plate {i} at distance {distance} from search origin {searchOrigin} (limit: {limit})",
                                            i, distance, searchOrigin, exportConfig.TerrainExportDistance);
                    terrainProgress.IncrementProgress();
                    continue;
                }
            }
            var mdlPath = $"{terrainInstance.Path.GamePath}/bgplate/{i:D4}.mdl";
            var mdlData = pack.GetFileOrReadFromDisk(mdlPath);
            if (mdlData == null)
            {
                throw new Exception($"Failed to load model file {mdlPath} returned null");
            }
            
            Plugin.Logger.LogInformation("Loaded model {mdlPath}", mdlPath);
            var mdlFile = new MdlFile(mdlData);

            var materials = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();
            var materialBuilders = new List<MaterialBuilder>();
            foreach (var mtrlPath in materials)
            {
                if (!terrainMaterialCache.TryGetValue(terrainInstance, out var terrainMtrlCache))
                {
                    terrainMtrlCache = new Dictionary<string, MaterialBuilder>();
                    terrainMaterialCache[terrainInstance] = terrainMtrlCache;
                }
                
                if (terrainMtrlCache.TryGetValue(mtrlPath, out var cachedBuilder))
                {
                    materialBuilders.Add(cachedBuilder);
                }
                else
                {
                    var materialBuilder = composerCache.ComposeMaterial(mtrlPath);
                    materialBuilders.Add(materialBuilder);
                }
            }

            var model = new Model(mdlPath, mdlFile, null);
            var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, [], null);

            var plateRoot = new NodeBuilder(mdlPath);
            for (var meshIdx = 0; meshIdx < meshes.Count; meshIdx++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var mesh = meshes[meshIdx];
                scene.AddRigidMesh(mesh.Mesh, plateRoot);
            }

            plateRoot.SetLocalTransform(plateTransform.AffineTransform, true);
            root.AddNode(plateRoot);
            terrainProgress.IncrementProgress();
        }
        
        terrainProgress.IsComplete = true;
        root.SetLocalTransform(terrainInstance.Transform.AffineTransform, true);
        return root;
    }

    public NodeBuilder ComposeEnvLight(ParsedEnvLightInstance instance, SceneBuilder sceneBuilder)
    {
        var root = new NodeBuilder($"{instance.Type}_{instance.Id}");
        var lt = instance.Lighting;
        var lights = new List<(Vector3, Vector3, float, string)>
        {
            (lt.SunLightColor.Rgb, lt.SunLightColor._vec3, lt.SunLightColor.HdrIntensity, "SunLight"),
            (lt.MoonLightColor.Rgb, lt.SunLightColor._vec3, lt.MoonLightColor.HdrIntensity, "MoonLight"),
            (lt.Ambient.Rgb, lt.SunLightColor._vec3, lt.Ambient.HdrIntensity, "AmbientLight")
        };
        var transform = instance.Transform with {Rotation = Quaternion.Identity};
        
        foreach (var (color, rawColor, intensity, name) in lights)
        {
            var lightRoot = new NodeBuilder($"Light_{instance.Type}_{name}_{instance.Id}");
            var lightBuilder = new LightBuilder.Point
            {
                Color = color,
                Intensity = intensity,
                Name = lightRoot.Name
            };
            
            var extras = new Dictionary<string, object>
            {
                { "ColorHDR", rawColor },
                { "ColorRGB", color },
                { "HDRIntensity", intensity },
                { "LightType", name },
            };
            lightBuilder.Extras = JsonNode.Parse(JsonSerializer.Serialize(extras, MaterialComposer.JsonOptions));
            lightRoot.Extras = JsonNode.Parse(JsonSerializer.Serialize(extras, MaterialComposer.JsonOptions));
            
            sceneBuilder.AddLight(lightBuilder, lightRoot);
            lightRoot.SetLocalTransform(transform.AffineTransform, true);
            root.AddNode(lightRoot);
        }

        return root;
    }
    
    public NodeBuilder? ComposeLight(ParsedLightInstance instance, SceneBuilder scene)
    {
        if (instance.Light.Range <= 0)
        {
            // Plugin.Logger.LogWarning("Light {LightId} has a range of 0 or less ({Range})", instance.Id, instance.Light.Range);
            return null;
        }
        
        var root = new NodeBuilder();
        var transform = instance.Transform;
        
        // idk if its blender, sharpgltf or game engine stuff but flip the rotation for lights (only tested spot though)
        var rotation = transform.Rotation;
        rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
                
        transform = transform with {Rotation = rotation};
        var light = instance.Light;
        
        root.Name = $"Light_{instance.Type}_{light.LightType}_{instance.Id}";
        
        LightBuilder? lightBuilder;
        switch (light.LightType)
        {
            case LightType.Directional:
                lightBuilder = new LightBuilder.Directional
                {
                    Color = light.Color.Rgb,
                    Intensity = light.Color.HdrIntensity,
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
                    Intensity = light.Color.HdrIntensity,
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
                    Intensity = light.Color.HdrIntensity,
                    Range = light.Range,
                    InnerConeAngle = innerConeAngle,
                    OuterConeAngle = outerConeAngle,
                    Name = root.Name
                };
                break;
            default:
                Plugin.Logger.LogWarning("Unsupported light type: {LightType}", light.LightType);
                return null;
        }

        var extras = new Dictionary<string, object>()
        {
            { "LightType", light.LightType.ToString() },
            { "Range", light.Range },
            { "FalloffType", light.FalloffType.ToString() },
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
            { "HDRIntensity", light.Color.HdrIntensity },
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
