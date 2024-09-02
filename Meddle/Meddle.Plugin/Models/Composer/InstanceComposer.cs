using System.Collections.Concurrent;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SkiaSharp;

namespace Meddle.Plugin.Models.Composer;

public class InstanceComposer : IDisposable
{
    
    
    public InstanceComposer(ILogger log, SqPack manager, 
                            Configuration config, 
                            ParsedInstance[] instances, 
                            string? cacheDir = null, 
                            Action<ProgressEvent>? progress = null, 
                            bool bakeMaterials = true,
                            CancellationToken cancellationToken = default)
    {
        CacheDir = cacheDir ?? Path.GetTempPath();
        Directory.CreateDirectory(CacheDir);
        this.instances = instances;
        this.log = log;
        this.dataManager = manager;
        this.config = config;
        this.progress = progress;
        this.bakeMaterials = bakeMaterials;
        this.cancellationToken = cancellationToken;
        this.count = instances.Length;
    }

    private readonly ILogger log;
    private readonly SqPack dataManager;
    private readonly Configuration config;
    private readonly Action<ProgressEvent>? progress;
    private readonly bool bakeMaterials;
    private readonly CancellationToken cancellationToken;
    private readonly int count;
    private int countProgress;
    public string CacheDir { get; }
    private readonly ParsedInstance[] instances;
    private readonly ConcurrentDictionary<string, (string PathOnDisk, MemoryImage MemoryImage)> imageCache = new();
    private readonly ConcurrentDictionary<string, (ShpkFile File, ShaderPackage Package)> shpkCache = new();
    private readonly ConcurrentDictionary<string, MaterialBuilder> mtrlCache = new();
    
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
        
        bool wasAdded = false;
        if (parsedInstance is ParsedBgPartsInstance {Path: not null} bgPartsInstance)
        {
            var meshes = ComposeBgPartsInstance(bgPartsInstance);
            foreach (var mesh in meshes)
            {
                scene.AddRigidMesh(mesh.Mesh, root, Matrix4x4.Identity);
            }
            
            wasAdded = true;
        }

        if (parsedInstance is ParsedCharacterInstance { CharacterInfo: not null } characterInstance)
        {
            if (characterInstance.Kind == ObjectKind.Pc && !string.IsNullOrWhiteSpace(config.PlayerNameOverride))
            {
                root.Name = $"{characterInstance.Type}_{characterInstance.Kind}_{config.PlayerNameOverride}";
            }
            else
            {                
                root.Name = $"{characterInstance.Type}_{characterInstance.Kind}_{characterInstance.Name}";
            }
            ComposeCharacterInstance(characterInstance, scene, root);
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
                
                progress?.Invoke(new ProgressEvent("Shared Instance", countProgress, count, new ProgressEvent(root.Name, i, sharedInstance.Children.Count)));
            }
        }

        if (wasAdded)
        {
            root.SetLocalTransform(parsedInstance.Transform.AffineTransform, true);
            return root;
        }
        
        return null;
    }

    private TexFile? cubeMapTex;
    private PbdFile? pbdFile;

    private void EnsureBonesExist(Model model, List<BoneNodeBuilder> bones, BoneNodeBuilder? root)
    {
        foreach (var mesh in model.Meshes)
        {
            if (mesh.BoneTable == null) continue;

            foreach (var boneName in mesh.BoneTable)
            {
                if (bones.All(b => !b.BoneName.Equals(boneName, StringComparison.Ordinal)))
                {
                    log.LogInformation("Adding bone {BoneName} from mesh {MeshPath}", boneName,
                                          model.Path);
                    var bone = new BoneNodeBuilder(boneName);
                    if (root == null) throw new InvalidOperationException("Root bone not found");
                    root.AddNode(bone);
                    log.LogInformation("Added bone {BoneName} to {ParentBone}", boneName, root.BoneName);

                    bones.Add(bone);
                }
            }
        }
    }
    
    private void ComposeCharacterInstance(ParsedCharacterInstance characterInstance, SceneBuilder scene, NodeBuilder root)
    {
        if (cubeMapTex == null)
        {
            var catchlight = dataManager.GetFileOrReadFromDisk("chara/common/texture/sphere_d_array.tex");
            if (catchlight == null) throw new Exception("Failed to load catchlight texture");
            cubeMapTex = new TexFile(catchlight);
        }

        if (pbdFile == null)
        {
            var pbdData = dataManager.GetFileOrReadFromDisk("chara/xls/boneDeformer/human.pbd");
            if (pbdData == null) throw new Exception("Failed to load human.pbd");
            pbdFile = new PbdFile(pbdData);
        }
        
        var characterInfo = characterInstance.CharacterInfo;
        if (characterInfo == null) return;

        var bones = SkeletonUtils.GetBoneMap(characterInfo.Skeleton, true, out var rootBone);
        if (rootBone != null)
        {
            root.AddNode(rootBone);
        }

        for (var i = 0; i < characterInfo.Models.Count; i++)
        {
            var modelInfo = characterInfo.Models[i];
            if (modelInfo.PathFromCharacter.Contains("b0003_top")) continue;
            var mdlData = dataManager.GetFileOrReadFromDisk(modelInfo.Path);
            if (mdlData == null)
            {
                log.LogWarning("Failed to load model file: {modelPath}", modelInfo.Path);
                continue;
            }

            log.LogInformation("Loaded model {modelPath}", modelInfo.Path);
            var mdlFile = new MdlFile(mdlData);
            var materialBuilders = new List<MaterialBuilder>();
            foreach (var materialInfo in modelInfo.Materials)
            {
                var mtrlData = dataManager.GetFileOrReadFromDisk(materialInfo.Path);
                if (mtrlData == null)
                {
                    log.LogWarning("Failed to load material file: {mtrlPath}", materialInfo.Path);
                    throw new Exception($"Failed to load material file: {materialInfo.Path}");
                }

                log.LogInformation("Loaded material {mtrlPath}", materialInfo.Path);
                var mtrlFile = new MtrlFile(mtrlData);
                if (materialInfo.ColorTable != null)
                {
                    mtrlFile.ColorTable = materialInfo.ColorTable.Value;
                }
                
                var shpkName = mtrlFile.GetShaderPackageName();
                var shpkPath = $"shader/sm5/shpk/{shpkName}";
                if (!shpkCache.TryGetValue(shpkPath, out var shader))
                {
                    var shpkData = dataManager.GetFileOrReadFromDisk(shpkPath);
                    if (shpkData == null) throw new Exception($"Failed to load shader package file: {shpkPath}");
                    var shpkFile = new ShpkFile(shpkData);
                    shader = (shpkFile, new ShaderPackage(shpkFile, null!));
                    shpkCache.TryAdd(shpkPath, shader);
                    log.LogInformation("Loaded shader package {shpkPath}", shpkPath);
                }

                var texDict = new Dictionary<string, TextureResource>();

                foreach (var textureInfo in materialInfo.Textures)
                {
                    texDict[textureInfo.PathFromMaterial] = textureInfo.Resource;
                }

                var material = new Material(materialInfo.Path, mtrlFile, texDict, shader.File);
                var customizeParams = characterInfo.CustomizeParameter;
                var customizeData = characterInfo.CustomizeData;
                var name = $"{Path.GetFileNameWithoutExtension(materialInfo.PathFromModel)}_{Path.GetFileNameWithoutExtension(shpkName)}_{characterInstance.Id}";
                var builder = material.ShaderPackageName switch
                {
                    "bg.shpk" => MaterialUtility.BuildBg(material, name),
                    "bgprop.shpk" => MaterialUtility.BuildBgProp(material, name),
                    "character.shpk" => MaterialUtility.BuildCharacter(material, name),
                    "characterocclusion.shpk" => MaterialUtility.BuildCharacterOcclusion(material, name),
                    "characterlegacy.shpk" => MaterialUtility.BuildCharacterLegacy(material, name),
                    "charactertattoo.shpk" => MaterialUtility.BuildCharacterTattoo(
                        material, name, customizeParams, customizeData),
                    "hair.shpk" => MaterialUtility.BuildHair(material, name, customizeParams, customizeData),
                    "skin.shpk" => MaterialUtility.BuildSkin(material, name, customizeParams, customizeData),
                    "iris.shpk" =>
                        MaterialUtility.BuildIris(material, name, cubeMapTex, customizeParams, customizeData),
                    "water.shpk" => MaterialUtility.BuildWater(material, name),
                    "lightshaft.shpk" => MaterialUtility.BuildLightShaft(material, name),
                    _ => ComposeMaterial(materialInfo.Path, characterInstance)
                };

                materialBuilders.Add(builder);
            }

            var model = new Model(modelInfo.Path, mdlFile, modelInfo.ShapeAttributeGroup);
            EnsureBonesExist(model, bones, rootBone);
            (GenderRace from, GenderRace to, RaceDeformer deformer)? deform;
            if (modelInfo.Deformer != null)
            {
                // custom pbd may exist
                var pbdFileData = dataManager.GetFileOrReadFromDisk(modelInfo.Deformer.Value.PbdPath);
                if (pbdFileData == null)
                    throw new InvalidOperationException(
                        $"Failed to get deformer pbd {modelInfo.Deformer.Value.PbdPath}");
                deform = ((GenderRace)modelInfo.Deformer.Value.DeformerId,
                             (GenderRace)modelInfo.Deformer.Value.RaceSexId,
                             new RaceDeformer(new PbdFile(pbdFileData), bones));
                log.LogDebug("Using deformer pbd {Path}", modelInfo.Deformer.Value.PbdPath);
            }
            else
            {
                var parsed = RaceDeformer.ParseRaceCode(modelInfo.PathFromCharacter);
                if (Enum.IsDefined(parsed))
                {
                    deform = (parsed, characterInfo.GenderRace, new RaceDeformer(pbdFile, bones));
                }
                else
                {
                    deform = null;
                }
            }

            var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, bones, deform);
            foreach (var mesh in meshes)
            {
                InstanceBuilder instance;
                if (bones.Count > 0)
                {
                    instance = scene.AddSkinnedMesh(mesh.Mesh, Matrix4x4.Identity, bones.Cast<NodeBuilder>().ToArray());
                }
                else
                {
                    instance = scene.AddRigidMesh(mesh.Mesh, Matrix4x4.Identity);
                }

                if (model.Shapes.Count != 0 && mesh.Shapes != null)
                {
                    // This will set the morphing value to 1 if the shape is enabled, 0 if not
                    var enabledShapes = Model.GetEnabledValues(model.EnabledShapeMask, model.ShapeMasks)
                                             .ToArray();
                    var shapes = model.Shapes
                                      .Where(x => mesh.Shapes.Contains(x.Name))
                                      .Select(x => (x, enabledShapes.Contains(x.Name)));
                    instance.Content.UseMorphing().SetValue(shapes.Select(x => x.Item2 ? 1f : 0).ToArray());
                }
                
                if (mesh.Submesh != null)
                {
                    // Remove subMeshes that are not enabled
                    var enabledAttributes = Model.GetEnabledValues(model.EnabledAttributeMask, model.AttributeMasks);
                    if (!mesh.Submesh.Attributes.All(enabledAttributes.Contains))
                    {
                        instance.Remove();
                    }
                }
            }
            
            progress?.Invoke(new ProgressEvent("Character Instance", countProgress, count, new ProgressEvent(root.Name, i, characterInfo.Models.Count)));
        }
    }

    private void ComposeTerrainInstance(ParsedTerrainInstance terrainInstance, SceneBuilder scene, NodeBuilder root)
    {
        var teraPath = $"{terrainInstance.Path}/bgplate/terrain.tera";
        var teraData = dataManager.GetFileOrReadFromDisk(teraPath);
        if (teraData == null) throw new Exception($"Failed to load terrain file: {teraPath}");
        var teraFile = new TeraFile(teraData);
        
        for (var i = 0; i < teraFile.Header.PlateCount; i++)
        {
            if (cancellationToken.IsCancellationRequested) return;
            log.LogInformation("Parsing plate {i}", i);
            var platePos = teraFile.GetPlatePosition(i);
            var plateTransform = new Transform(new Vector3(platePos.X, 0, platePos.Y), Quaternion.Identity, Vector3.One);
            var mdlPath = $"{terrainInstance.Path}/bgplate/{i:D4}.mdl";
            var mdlData = dataManager.GetFileOrReadFromDisk(mdlPath);
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
            progress?.Invoke(new ProgressEvent("Terrain Instance", countProgress, count, new ProgressEvent(root.Name, i, (int)teraFile.Header.PlateCount)));
        }
    }

    private IReadOnlyList<ModelBuilder.MeshExport> ComposeBgPartsInstance(ParsedBgPartsInstance bgPartsInstance)
    {
        var mdlData = dataManager.GetFileOrReadFromDisk(bgPartsInstance.Path);
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
        if (mtrlCache.TryGetValue(path, out var cached))
        {
            return cached;
        }
        
        var mtrlData = dataManager.GetFileOrReadFromDisk(path);
        if (mtrlData == null) throw new Exception($"Failed to load material file: {path}");
        log.LogInformation("Loaded material {path}", path);
        
        var mtrlFile = new MtrlFile(mtrlData);
        var shpkName = mtrlFile.GetShaderPackageName();
        var shpkPath = $"shader/sm5/shpk/{shpkName}";
        if (!shpkCache.TryGetValue(shpkPath, out var shader))
        {
            var shpkData = dataManager.GetFileOrReadFromDisk(shpkPath);
            if (shpkData == null)
                throw new Exception($"Failed to load shader package file: {shpkPath}");
            var shpkFile = new ShpkFile(shpkData);
            shader = (shpkFile, new ShaderPackage(shpkFile, null!));
            shpkCache.TryAdd(shpkPath, shader);
            log.LogInformation("Loaded shader package {shpkPath}", shpkPath);
        }
        var material = new MaterialSet(mtrlFile, path, shader.File, shpkName);
        
        var materialName = $"{Path.GetFileNameWithoutExtension(path)}_{Path.GetFileNameWithoutExtension(shpkName)}";

        if (bakeMaterials)
        {
            if (shpkName == "lightshaft.shpk")
            {
                return new LightshaftMaterialBuilder(materialName,
                                                     material, 
                                                     dataManager.GetFileOrReadFromDisk,
                                                     CacheTexture)
                    .WithLightShaft();
            }

            if (shpkName is "bg.shpk" or "bgprop.shpk")
            {
                return new BgMaterialBuilder(materialName, shpkName, material, dataManager.GetFileOrReadFromDisk,
                                             CacheTexture)
                    .WithBg();
            }

            if (shpkName == "bgcolorchange.shpk")
            {
                Vector4? stainColor = instance switch
                {
                    IStainableInstance stainable => stainable.StainColor,
                    _ => null
                };

                return new BgMaterialBuilder(materialName, shpkName, material, dataManager.GetFileOrReadFromDisk,
                                             CacheTexture)
                    .WithBgColorChange(stainColor);
            }
        }

        return new GenericMaterialBuilder(materialName, material, dataManager.GetFileOrReadFromDisk, CacheTexture)
            .WithGeneric();
    }
    
    private ImageBuilder CacheTexture(SKTexture texture, string texName)
    {
        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
            textureBytes = memoryStream.ToArray();
        }

        var outPath = Path.Combine(CacheDir, $"{texName}.png");
        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        File.WriteAllBytes(outPath, textureBytes);
        
        var outImage = new MemoryImage(() => File.ReadAllBytes(outPath));
        imageCache.TryAdd(texName, (outPath, outImage));
        
        var name = Path.GetFileNameWithoutExtension(texName.Replace('.', '_'));
        var builder = ImageBuilder.From(outImage, name);
        builder.AlternateWriteFileName = $"{name}.*";
        return builder;
    }

    public void Dispose()
    {
        cancellationToken.ThrowIfCancellationRequested();
    }
}
