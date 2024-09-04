using System.Collections.Concurrent;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Models.Composer;

public class InstanceComposer : IDisposable
{
    private readonly CancellationToken cancellationToken;
    private readonly Configuration config;
    private readonly int count;
    private readonly SqPack dataManager;
    private readonly ParsedInstance[] instances;
    private readonly ILogger log;
    private readonly Action<ProgressEvent>? progress;
    private int countProgress;
    private readonly DataProvider dataProvider;

    public InstanceComposer(
        ILogger log, SqPack manager,
        Configuration config,
        ParsedInstance[] instances,
        string? cacheDir = null,
        Action<ProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CacheDir = cacheDir ?? Path.GetTempPath();
        Directory.CreateDirectory(CacheDir);
        this.instances = instances;
        this.log = log;
        dataManager = manager;
        this.config = config;
        this.progress = progress;
        this.cancellationToken = cancellationToken;
        count = instances.Length;
        dataProvider = new DataProvider(CacheDir, dataManager, log, cancellationToken);
    }

    public string CacheDir { get; }

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
        progress?.Invoke(new ProgressEvent("Export", 0, count));
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
            progress?.Invoke(new ProgressEvent("Export", countProgress, count));
        }, false);

        try
        {
            var computeDir = Path.Combine(CacheDir, "Computed");
            Directory.Delete(computeDir, true);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to delete computed directory");
        }
    }

    public NodeBuilder? ComposeInstance(SceneBuilder scene, ParsedInstance parsedInstance)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        var root = new NodeBuilder();
        if (parsedInstance is IPathInstance pathInstance)
        {
            root.Name = $"{parsedInstance.Type}_{Path.GetFileNameWithoutExtension(pathInstance.Path)}";
        }
        else
        {
            root.Name = $"{parsedInstance.Type}_{parsedInstance.Id}";
        }

        var wasAdded = false;
        if (parsedInstance is ParsedBgPartsInstance {Path: not null} bgPartsInstance)
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

            var characterComposer = new CharacterComposer(log, dataProvider);
            characterComposer.ComposeCharacterInstance(characterInstance, scene, root);
            wasAdded = true;
        }

        if (parsedInstance is ParsedLightInstance lightInstance)
        {
            // TODO: Probably can fill some parts here given more info
            root.Name = $"{lightInstance.Type}_{lightInstance.Id}";
            var lightBuilder = new LightBuilder.Point();
            scene.AddLight(lightBuilder, root);
            wasAdded = true;
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
                var childNode = ComposeInstance(scene, child);
                if (childNode != null)
                {
                    root.AddNode(childNode);
                    wasAdded = true;
                }

                progress?.Invoke(new ProgressEvent("Shared Instance", countProgress, count,
                                                   new ProgressEvent(root.Name, i, sharedInstance.Children.Count)));
            }
        }

        if (wasAdded)
        {
            root.SetLocalTransform(parsedInstance.Transform.AffineTransform, true);
            return root;
        }

        return null;
    }

    private void ComposeTerrainInstance(ParsedTerrainInstance terrainInstance, SceneBuilder scene, NodeBuilder root)
    {
        var teraPath = $"{terrainInstance.Path}/bgplate/terrain.tera";
        var teraData = dataProvider.LookupData(teraPath);
        if (teraData == null) throw new Exception($"Failed to load terrain file: {teraPath}");
        var teraFile = new TeraFile(teraData);

        //for (var i = 0; i < teraFile.Header.PlateCount; i++)
        var processed = 0;
        Parallel.For(0, teraFile.Header.PlateCount, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount / 2, 1)
        }, i =>
        {
            if (cancellationToken.IsCancellationRequested) return;
            log.LogInformation("Parsing plate {i}", i);
            var platePos = teraFile.GetPlatePosition((int)i);
            var plateTransform = new Transform(new Vector3(platePos.X, 0, platePos.Y), Quaternion.Identity, Vector3.One);
            var mdlPath = $"{terrainInstance.Path}/bgplate/{i:D4}.mdl";
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

            var plateRoot = new NodeBuilder($"Plate{i:D4}");
            foreach (var mesh in meshes)
            {
                scene.AddRigidMesh(mesh.Mesh, plateRoot, plateTransform.AffineTransform);
            }

            root.AddNode(plateRoot);
            Interlocked.Increment(ref processed);
            progress?.Invoke(new ProgressEvent("Terrain Instance", countProgress, count,
                                               new ProgressEvent(root.Name, processed,
                                                                 (int)teraFile.Header.PlateCount)));
        });
    }

    private IReadOnlyList<ModelBuilder.MeshExport> ComposeBgPartsInstance(ParsedBgPartsInstance bgPartsInstance)
    {
        var mdlData = dataProvider.LookupData(bgPartsInstance.Path);
        if (mdlData == null)
        {
            log.LogWarning("Failed to load model file: {bgPartsInstance.Path}", bgPartsInstance.Path);
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

        var model = new Model(bgPartsInstance.Path, mdlFile, null);
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

        Dictionary<string, string> textureMap = new();
        if (instance is ITextureMappableInstance mappableInstance)
        {
            textureMap = mappableInstance.TextureMap;
        }
        
        var material = new MaterialSet(mtrlFile, path, shpkFile, shpkName);
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
