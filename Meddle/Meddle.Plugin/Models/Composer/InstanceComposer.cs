using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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

public class InstanceComposer
{
    public InstanceComposer(ILogger log, SqPack manager, Configuration config, ParsedInstance[] instances, string? cacheDir = null, 
                       Action<ProgressEvent>? progress = null, CancellationToken cancellationToken = default)
    {
        CacheDir = cacheDir ?? Path.GetTempPath();
        Directory.CreateDirectory(CacheDir);
        this.instances = instances;
        this.log = log;
        this.dataManager = manager;
        this.config = config;
        this.progress = progress;
        this.cancellationToken = cancellationToken;
        this.count = instances.Length;
    }

    private readonly ILogger log;
    private readonly SqPack dataManager;
    private readonly Configuration config;
    private readonly Action<ProgressEvent>? progress;
    private readonly CancellationToken cancellationToken;
    private readonly int count;
    private int countProgress;
    public string CacheDir { get; }
    private readonly ParsedInstance[] instances;
    private readonly ConcurrentDictionary<string, (string PathOnDisk, MemoryImage MemoryImage)> imageCache = new();
    private readonly ConcurrentDictionary<string, (ShpkFile File, ShaderPackage Package)> shpkCache = new();
    private readonly ConcurrentDictionary<string, MaterialBuilder> mtrlCache = new();

    public void Compose(SceneBuilder scene)
    {
        progress?.Invoke(new ProgressEvent("Export", 0, count));
        Parallel.ForEach(instances, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount / 2, 1)
        }, instance =>
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
        });
        /*foreach (var instance in instances)
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

            countProgress++;
            progress?.Invoke(new ProgressEvent("Export", countProgress, count));
        }*/
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
                    _ => ComposeMaterial(materialInfo.Path)
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
                var materialBuilder = ComposeMaterial(mtrlPath);
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
            var output = ComposeMaterial(mtrlPath);
            materialBuilders.Add(output);
        }

        var model = new Model(bgPartsInstance.Path, mdlFile, null);
        var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, [], null);
        return meshes;
    }

    private MaterialBuilder ComposeMaterial(string path)
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
        var material = new MaterialSet(mtrlFile, shader.File, shpkName);
        
        if (shpkName == "lightshaft.shpk")
        {
            return ComposeLightshaft(path, material);
        }
        
        return ComposeGenericMaterial(path, material);
    }

    private MaterialBuilder ComposeLightshaft(string path, MaterialSet materialSet)
    {
        var output = new XivMaterialBuilder(path, "lightshaft.shpk");
        
        var sampler0 = materialSet.TextureUsageDict[TextureUsage.g_Sampler0];
        var sampler1 = materialSet.TextureUsageDict[TextureUsage.g_Sampler1];
        var texture0 = dataManager.GetFileOrReadFromDisk(sampler0);
        var texture1 = dataManager.GetFileOrReadFromDisk(sampler1);
        if (texture0 == null || texture1 == null)
        {
            log.LogWarning("Failed to load lightshaft textures {sampler0} {sampler1}", sampler0, sampler1);
            return output;
        }

        var tex0 = new TexFile(texture0);
        var tex1 = new TexFile(texture1);
        
        Vector2 size = Vector2.Max(new Vector2(tex0.Header.Width, tex0.Header.Height), new Vector2(tex1.Header.Width, tex1.Header.Height));

        var res0 = Texture.GetResource(tex0).ToTexture(size);
        var res1 = Texture.GetResource(tex1).ToTexture(size);
        
        
        var outTexture = new SKTexture((int)size.X, (int)size.Y);
        materialSet.TryGetConstant(MaterialConstant.g_Color, out Vector3 colorv3);
        for (var x = 0; x < outTexture.Width; x++)
        for (var y = 0; y < outTexture.Height; y++)
        {
            var tex0Color = res0[x, y].ToVector4();
            var tex1Color = res1[x, y].ToVector4();
            var outColor = new Vector4(colorv3, 1);
            
            outTexture[x, y] = (outColor * tex0Color * tex1Color).ToSkColor();
        }
        
        // cache texture
        var fileName = $"{Path.GetFileNameWithoutExtension(path)}_computed_lightshaft";
        var tempPath = Path.Combine(CacheDir, $"{fileName}.png");
        var diffuseImage = CacheTexture(outTexture, tempPath);
        output.WithBaseColor(diffuseImage);
        
        
        if (materialSet.TryGetConstant(MaterialConstant.g_AlphaThreshold, out float alphaThreshold))
        {
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);
        }

        materialSet.TryGetConstant(MaterialConstant.g_Ray, out float[] ray);
        materialSet.TryGetConstant(MaterialConstant.g_TexU, out float[] texU);
        materialSet.TryGetConstant(MaterialConstant.g_TexV, out float[] texV);
        materialSet.TryGetConstant(MaterialConstant.g_TexAnim, out float[] texAnim);
        materialSet.TryGetConstant(MaterialConstant.g_ShadowAlphaThreshold, out float[] shadowAlphaThreshold);
        materialSet.TryGetConstant(MaterialConstant.g_NearClip, out float[] nearClip);
        materialSet.TryGetConstant(MaterialConstant.g_AngleClip, out float[] angleClip);
        
        
        var extrasDict = new Dictionary<string, object>
        {
            {"Sampler0", sampler0},
            {"Sampler1", sampler1},
            {"AlphaThreshold", alphaThreshold},
            {"Ray", ray},
            {"TexU", texU},
            {"TexV", texV},
            {"TexAnim", texAnim},
            {"ShadowAlphaThreshold", shadowAlphaThreshold},
            {"NearClip", nearClip},
            {"AngleClip", angleClip},
            {"Color", colorv3}
        };
        
        output.Extras = JsonNode.Parse(JsonSerializer.Serialize(extrasDict, new JsonSerializerOptions
        {
            IncludeFields = true
        }));
        return output;
    }
    
    private MaterialBuilder ComposeGenericMaterial(string path, MaterialSet materialSet)
    {
        var materialName = $"{Path.GetFileNameWithoutExtension(path)}_{materialSet.ShpkName}";
        var output = new XivMaterialBuilder(materialName, materialSet.ShpkName)
                     .WithMetallicRoughnessShader()
                     .WithBaseColor(Vector4.One);

        var alphaThreshold = materialSet.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);
        
        // Initialize texture in cache
        var texturePaths = materialSet.File.GetTexturePaths();
        foreach (var (offset, texPath) in texturePaths)
        {
            if (imageCache.ContainsKey(texPath)) continue;
            CacheTexture(texPath);
        }

        var setTypes = new HashSet<TextureUsage>();
        foreach (var sampler in materialSet.File.Samplers)
        {
            if (sampler.TextureIndex == byte.MaxValue) continue;
            var textureInfo = materialSet.File.TextureOffsets[sampler.TextureIndex];
            var texturePath = texturePaths[textureInfo.Offset];
            if (!imageCache.TryGetValue(texturePath, out var tex)) continue;
            // bg textures can have additional textures, which may be dummy textures, ignore them
            if (texturePath.Contains("dummy_")) continue;
            if (!materialSet.Package.TextureLookup.TryGetValue(sampler.SamplerId, out var usage))
            {
                log.LogWarning("Unknown texture usage for texture {texturePath} ({textureUsage})", texturePath, (TextureUsage)sampler.SamplerId);
                continue;
            }
            
            var channel = MaterialUtility.MapTextureUsageToChannel(usage);
            if (channel != null && setTypes.Add(usage))
            {
                var fileName = $"{Path.GetFileNameWithoutExtension(texturePath)}_{usage}_{materialSet.ShpkName}";
                var imageBuilder = ImageBuilder.From(tex.MemoryImage, fileName);
                imageBuilder.AlternateWriteFileName = $"{fileName}.*";
                output.WithChannelImage(channel.Value, imageBuilder);
            }
            else if (channel != null)
            {
                log.LogDebug("Duplicate texture {texturePath} with usage {usage}", texturePath, usage);
            }
            else
            {
                log.LogDebug("Unknown texture usage {usage} for texture {texturePath}", usage, texturePath);
            }
        }
        
        mtrlCache.TryAdd(path, output);
        return output;
    }

    public class MaterialSet
    {
        public readonly MtrlFile File;
        public readonly ShpkFile Shpk;
        public readonly string ShpkName;
        public readonly ShaderPackage Package;
        public readonly ShaderKey[] ShaderKeys;
        public readonly Dictionary<MaterialConstant, float[]> MaterialConstantDict;
        public readonly Dictionary<TextureUsage, string> TextureUsageDict;

        public bool TryGetConstant(MaterialConstant id, out float[] value)
        {
            if (MaterialConstantDict.TryGetValue(id, out var values))
            {
                value = values;
                return true;
            }

            if (Package.MaterialConstants.TryGetValue(id, out var constant))
            {
                value = constant;
                return true;
            }

            value = [];
            return false;
        }
        
        public bool TryGetConstant(MaterialConstant id, out float value)
        {
            if (MaterialConstantDict.TryGetValue(id, out var values))
            {
                value = values[0];
                return true;
            }

            if (Package.MaterialConstants.TryGetValue(id, out var constant))
            {
                value = constant[0];
                return true;
            }

            value = 0;
            return false;
        }
        
        public bool TryGetConstant(MaterialConstant id, out Vector2 value)
        {
            if (MaterialConstantDict.TryGetValue(id, out var values))
            {
                value = new Vector2(values[0], values[1]);
                return true;
            }

            if (Package.MaterialConstants.TryGetValue(id, out var constant))
            {
                value = new Vector2(constant[0], constant[1]);
                return true;
            }

            value = Vector2.Zero;
            return false;
        }
        
        public bool TryGetConstant(MaterialConstant id, out Vector3 value)
        {
            if (MaterialConstantDict.TryGetValue(id, out var values))
            {
                value = new Vector3(values[0], values[1], values[2]);
                return true;
            }

            if (Package.MaterialConstants.TryGetValue(id, out var constant))
            {
                value = new Vector3(constant[0], constant[1], constant[2]);
                return true;
            }

            value = Vector3.Zero;
            return false;
        }
        
        public bool TryGetConstant(MaterialConstant id, out Vector4 value)
        {
            if (MaterialConstantDict.TryGetValue(id, out var values))
            {
                value = new Vector4(values[0], values[1], values[2], values[3]);
                return true;
            }

            if (Package.MaterialConstants.TryGetValue(id, out var constant))
            {
                value = new Vector4(constant[0], constant[1], constant[2], constant[3]);
                return true;
            }

            value = Vector4.Zero;
            return false;
        }
        
        public float GetConstantOrDefault(MaterialConstant id, float @default)
        {
            return MaterialConstantDict.TryGetValue(id, out var values) ? values[0] : @default;
        }
    
        public Vector2 GetConstantOrDefault(MaterialConstant id, Vector2 @default)
        {
            return MaterialConstantDict.TryGetValue(id, out var values) ? new Vector2(values[0], values[1]) : @default;
        }
    
        public Vector3 GetConstantOrDefault(MaterialConstant id, Vector3 @default)
        {
            return MaterialConstantDict.TryGetValue(id, out var values) ? new Vector3(values[0], values[1], values[2]) : @default;
        }

        public Vector4 GetConstantOrDefault(MaterialConstant id, Vector4 @default)
        {
            return MaterialConstantDict.TryGetValue(id, out var values)
                       ? new Vector4(values[0], values[1], values[2], values[3])
                       : @default;
        }
        
        public MaterialSet(MtrlFile file, ShpkFile shpk, string shpkName)
        {
            this.File = file;
            this.Shpk = shpk;
            this.ShpkName = shpkName;
            this.Package = new ShaderPackage(shpk, shpkName);
            
            ShaderKeys = new ShaderKey[file.ShaderKeys.Length];
            for (var i = 0; i < file.ShaderKeys.Length; i++)
            {
                ShaderKeys[i] = new ShaderKey
                {
                    Category = file.ShaderKeys[i].Category,
                    Value = file.ShaderKeys[i].Value
                };
            }

            MaterialConstantDict = new Dictionary<MaterialConstant, float[]>();
            foreach (var constant in file.Constants)
            {
                var index = constant.ValueOffset / 4;
                var count = constant.ValueSize / 4;
                var buf = new List<byte>(128);
                for (var j = 0; j < count; j++)
                {
                    var value = file.ShaderValues[index + j];
                    var bytes = BitConverter.GetBytes(value);
                    buf.AddRange(bytes);
                }

                var floats = MemoryMarshal.Cast<byte, float>(buf.ToArray());
                var values = new float[count];
                for (var j = 0; j < count; j++)
                {
                    values[j] = floats[j];
                }

                // even if duplicate, last probably takes precedence
                var id = (MaterialConstant)constant.ConstantId;
                MaterialConstantDict[id] = values;
            }
            
            TextureUsageDict = new Dictionary<TextureUsage, string>();
            var texturePaths = file.GetTexturePaths();
            foreach (var sampler in file.Samplers)
            {
                if (sampler.TextureIndex == byte.MaxValue) continue;
                var textureInfo = file.TextureOffsets[sampler.TextureIndex];
                var texturePath = texturePaths[textureInfo.Offset];
                if (!Package.TextureLookup.TryGetValue(sampler.SamplerId, out var usage))
                {
                    continue;
                }
                TextureUsageDict[usage] = texturePath;
            }
        }
    }
    
    private MemoryImage CacheTexture(SKTexture texture, string texPath)
    {
        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
            textureBytes = memoryStream.ToArray();
        }

        var dirPath = Path.GetDirectoryName(texPath);
        if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

        File.WriteAllBytes(texPath, textureBytes);
        
        var outImage = new MemoryImage(() => File.ReadAllBytes(texPath));
        imageCache.TryAdd(texPath, (texPath, outImage));
        return outImage;
    }
    
    private void CacheTexture(string texPath)
    {
        var texData = dataManager.GetFileOrReadFromDisk(texPath);
        if (texData == null) throw new Exception($"Failed to load texture file: {texPath}");
        log.LogInformation("Loaded texture {texPath}", texPath);
        var texFile = new TexFile(texData);
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
