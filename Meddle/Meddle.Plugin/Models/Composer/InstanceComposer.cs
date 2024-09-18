using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Models.Composer;

public class InstanceComposer : IDisposable
{
    private readonly CancellationToken cancellationToken;
    private readonly CharacterComposer characterComposer;
    private readonly Configuration config;
    private readonly int count;
    private readonly ParsedInstance[] instances;
    private readonly ILogger log;
    private readonly Action<ProgressEvent>? progress;
    private readonly DataProvider dataProvider;
    private int countProgress;

    public InstanceComposer(
        ILogger log, 
        Configuration config,
        ParsedInstance[] instances,
        Action<ProgressEvent>? progress,
        CancellationToken cancellationToken,
        CharacterComposer characterComposer,
        DataProvider dataProvider)
    {
        this.instances = instances;
        this.log = log;
        this.config = config;
        this.progress = progress;
        this.cancellationToken = cancellationToken;
        this.characterComposer = characterComposer;
        this.count = instances.Length;
        this.dataProvider = dataProvider;
    }

    public void Dispose()
    {
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Iterate(Action<ParsedInstance> action, bool parallel)
    {
        if (parallel)
        {
            Parallel.ForEach(instances, new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount / 2, 1)
            }, action);
        }
        else
        {
            foreach (var instance in instances)
            {
                action(instance);
            }
        }
    }

    public void Compose(SceneBuilder scene)
    {
        progress?.Invoke(new ProgressEvent(scene.GetHashCode(), "Export", 0, count));
        Iterate(instance =>
        {
            try
            {
                var node = ComposeInstance(scene, instance);
                if (node != null)
                {
                    scene.AddNode(node);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to compose instance {instanceId} {instanceType}", instance.Id, instance.Type);
            }

            //countProgress++;
            Interlocked.Increment(ref countProgress);
            progress?.Invoke(new ProgressEvent(scene.GetHashCode(), "Export", countProgress, count));
        }, false);
    }

    public NodeBuilder? ComposeInstance(SceneBuilder scene, ParsedInstance parsedInstance)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        var root = new NodeBuilder();
        var transform = parsedInstance.Transform;
        if (parsedInstance is IPathInstance pathInstance)
        {
            root.Name = $"{parsedInstance.Type}_{Path.GetFileNameWithoutExtension(pathInstance.Path.GamePath)}";
        }
        else
        {
            root.Name = $"{parsedInstance.Type}_{parsedInstance.Id}";
        }

        var wasAdded = false;
        if (parsedInstance is ParsedBgPartsInstance {Path.FullPath: not null} bgPartsInstance)
        {
            var meshes = ComposeBgPartsInstance(bgPartsInstance);
            foreach (var mesh in meshes)
            {
                scene.AddRigidMesh(mesh.Mesh, root, Matrix4x4.Identity);
            }

            wasAdded = true;
        }

        if (parsedInstance is ParsedCharacterInstance {CharacterInfo: not null} characterInstance)
        {
            if (characterInstance.Kind == ObjectKind.Pc && !string.IsNullOrWhiteSpace(config.PlayerNameOverride))
            {
                root.Name = $"{characterInstance.Type}_{characterInstance.Kind}_{config.PlayerNameOverride}";
            }
            else
            {
                root.Name = $"{characterInstance.Type}_{characterInstance.Kind}_{characterInstance.Name}";
            }

            characterComposer.ComposeCharacterInstance(characterInstance, scene, root);
            wasAdded = true;
        }

        if (parsedInstance is ParsedLightInstance lightInstance)
        {
            if (lightInstance.Light.Range <= 0)
            {
                log.LogWarning("Light {LightId} has a range of 0 or less ({Range})", lightInstance.Id, lightInstance.Light.Range);
                return null;
            }
            
            // idk if its blender, sharpgltf or game engine stuff but flip the rotation for lights (only tested spot though)
            var rotation = transform.Rotation;
            rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
                    
            transform = transform with {Rotation = rotation};
            var light = lightInstance.Light;
            
            root.Name = $"{lightInstance.Type}_{light.LightType}_{lightInstance.Id}";

            LightBuilder? lightBuilder = null;
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
                    log.LogWarning("Unsupported light type: {LightType}", light.LightType);
                    break;
            }

            if (lightBuilder != null)
            {
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
                lightBuilder.Extras = JsonNode.Parse(JsonSerializer.Serialize(extras, MaterialSet.JsonOptions));
                
                root.Extras = JsonNode.Parse(JsonSerializer.Serialize(extras, MaterialSet.JsonOptions));
                scene.AddLight(lightBuilder, root);
                wasAdded = true;
            }
        }

        if (parsedInstance is ParsedTerrainInstance terrainInstance)
        {
            ComposeTerrainInstance(terrainInstance, scene, root);
            wasAdded = true;
        }

        if (parsedInstance is ParsedSharedInstance sharedInstance)
        {
            for (var i = 0; i < sharedInstance.Children.Count; i++)
            {          
                var child = sharedInstance.Children[i];      
                progress?.Invoke(new ProgressEvent(parsedInstance.GetHashCode(), "Shared Instance", countProgress, count, 
                                                   new ProgressEvent(child.GetHashCode(), root.Name, i, sharedInstance.Children.Count)));
                var childNode = ComposeInstance(scene, child);
                if (childNode != null)
                {
                    root.AddNode(childNode);
                    wasAdded = true;
                }
            }
        }

        if (wasAdded)
        {
            root.SetLocalTransform(transform.AffineTransform, true);
            return root;
        }

        return null;
    }
    
    private (float outer, float inner) FixSpotLightAngles(float outerConeAngle, float innerConeAngle)
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
    
    
    // directional lights use illuminance in lux (lm/ m2)
    private float LuxIntensity(float intensity)
    {
        return intensity;
    }
    
    // Point and spot lights use luminous intensity in candela (lm/ sr)
    private float CandelaIntensity(float intensity)
    {
        return intensity * 100f;
    }
    
    private float DegreesToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180f);
    }

    private void ComposeTerrainInstance(ParsedTerrainInstance terrainInstance, SceneBuilder scene, NodeBuilder root)
    {
        var teraPath = $"{terrainInstance.Path.GamePath}/bgplate/terrain.tera";
        var teraData = dataProvider.LookupData(teraPath);
        if (teraData == null) throw new Exception($"Failed to load terrain file: {teraPath}");
        var teraFile = new TeraFile(teraData);

        var processed = 0;
        for (var i = 0; i < teraFile.Header.PlateCount; i++)
        {
            if (cancellationToken.IsCancellationRequested) return;
            log.LogInformation("Parsing plate {i}", i);
            var platePos = teraFile.GetPlatePosition((int)i);
            var plateTransform = new Transform(new Vector3(platePos.X, 0, platePos.Y), Quaternion.Identity, Vector3.One);
            var mdlPath = $"{terrainInstance.Path.GamePath}/bgplate/{i:D4}.mdl";
            var mdlData = dataProvider.LookupData(mdlPath);
            if (mdlData == null) throw new Exception($"Failed to load model file: {mdlPath}");
            log.LogInformation("Loaded model {mdlPath}", mdlPath);
            var mdlFile = new MdlFile(mdlData);

            var materials = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();
            var materialBuilders = new List<MaterialBuilder>();
            foreach (var mtrlPath in materials)
            {
                var materialBuilder = ComposeMaterial(mtrlPath, terrainInstance);
                materialBuilders.Add(materialBuilder);
            }

            var model = new Model(mdlPath, mdlFile, null);
            var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, [], null);

            var plateRoot = new NodeBuilder(mdlPath);
            foreach (var mesh in meshes)
            {
                scene.AddRigidMesh(mesh.Mesh, plateRoot, plateTransform.AffineTransform);
            }

            root.AddNode(plateRoot);
            Interlocked.Increment(ref processed);
            progress?.Invoke(new ProgressEvent(terrainInstance.GetHashCode(), "Terrain Instance", countProgress, count, new ProgressEvent(root.GetHashCode(), root.Name, processed, (int)teraFile.Header.PlateCount)));
        }
    }

    private IReadOnlyList<ModelBuilder.MeshExport> ComposeBgPartsInstance(ParsedBgPartsInstance bgPartsInstance)
    {
        var mdlData = dataProvider.LookupData(bgPartsInstance.Path.FullPath);
        if (mdlData == null)
        {
            log.LogWarning("Failed to load model file: {Path}", bgPartsInstance.Path.FullPath);
            return [];
        }

        var mdlFile = new MdlFile(mdlData);
        var materials = mdlFile.GetMaterialNames().Select(x => x.Value).ToArray();

        var materialBuilders = new List<MaterialBuilder>();
        foreach (var mtrlPath in materials)
        {
            var output = ComposeMaterial(mtrlPath, bgPartsInstance);
            materialBuilders.Add(output);
        }

        var model = new Model(bgPartsInstance.Path.GamePath, mdlFile, null);
        var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, [], null);
        return meshes;
    }

    private MaterialBuilder ComposeMaterial(string path, ParsedInstance instance)
    {
        // TODO: Really not ideal but can't rely on just the path since material inputs can change
        var mtrlFile = dataProvider.GetMtrlFile(path);
        var shpkName = mtrlFile.GetShaderPackageName();
        var shpkPath = $"shader/sm5/shpk/{shpkName}";
        var shpkFile = dataProvider.GetShpkFile(shpkPath);
        var material = new MaterialSet(mtrlFile, path, shpkFile, shpkName, null, null);
        if (instance is IStainableInstance stainableInstance)
        {
            material.SetStainColor(stainableInstance.StainColor);
        }

        if (instance is ICharacterInstance characterInstance)
        {
            material.SetCustomizeParameters(characterInstance.CustomizeParameter);
            material.SetCustomizeData(characterInstance.CustomizeData);
        }

        return dataProvider.GetMaterialBuilder(material, path, shpkName);
    }
}
