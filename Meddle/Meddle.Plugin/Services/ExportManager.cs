using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Enums;
using Meddle.Plugin.Files;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Utility;
using Meddle.Plugin.Xande;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;
using RaceDeformer = Meddle.Plugin.Xande.RaceDeformer;

namespace Meddle.Plugin.Services;

public partial class ExportManager
{
    public IPluginLog Log { get; }
    public ModelBuilder ModelBuilder { get; }
    public PbdFile Pbd { get; }
    public bool IsExporting { get; private set; }

    public ExportManager(IPluginLog log, IDataManager gameData, ModelBuilder modelBuilder)
    {
        Log = log;
        ModelBuilder = modelBuilder;
        var fileContent =
            gameData.GetFile("chara/xls/boneDeformer/human.pbd") ??
            throw new InvalidOperationException("Failed to load PBD file");
        Pbd = new PbdFile(fileContent.Data);
    }


    public async Task Export(
        ExportLogger logger, ExportConfig config, CharacterTree characterTree, CancellationToken cancellationToken)
    {
        if (IsExporting)
            throw new InvalidOperationException("Already exporting.");

        IsExporting = true;
        await Task.Run(() =>
        {
            try
            {
                ExportInternal(logger, config, characterTree, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.Info("Export cancelled");
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to export model");
            } finally
            {
                IsExporting = false;
            }
        }, cancellationToken);
    }

    public async Task Export(
        ExportLogger logger, ExportConfig config, 
        Model[] models, 
        AttachedChild[] attachedChildren,
        Skeleton skeleton, GenderRace targetRace,
        CustomizeParameters? customizeParameter, CancellationToken cancellationToken)
    {
        if (IsExporting)
            throw new InvalidOperationException("Already exporting.");

        if (models.Length == 0 && attachedChildren.Length == 0)
            throw new ArgumentException("No models to export");
        
        IsExporting = true;
        await Task.Run(() =>
        {
            try
            {
                var path = Path.Combine(Plugin.TempDirectory,
                                        $"{models.GetHashCode():X8}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
                logger.Debug($"Exporting to {path}");

                var scene = new SceneBuilder("scene");
                var boneMap = ModelUtility.GetBoneMap(skeleton, out var rootBone).ToArray();
                scene.AddNode(rootBone);
                
                ForEach(models, model =>
                {
                    HandleModel(logger, config, model, targetRace, scene, boneMap, Matrix4x4.Identity,
                                customizeParameter, cancellationToken);
                }, config.ParallelBuild);
                
                for (var i = 0; i < attachedChildren.Length; i++)
                {
                    var child = attachedChildren[i];
                    var attachName = skeleton.PartialSkeletons[child.Attach.PartialSkeletonIdx]
                                              .HkSkeleton!.BoneNames[child.Attach.BoneIdx];
                    var childInfo = HandleAttachedChild(child, i, attachName, scene, boneMap, rootBone);
                    foreach (var model in child.Models)
                    {
                        logger.Debug($"Handling child model {model.Path}");
                        HandleModel(logger, config, model, targetRace, scene,
                                    childInfo.childBoneMap.ToArray(),
                                    childInfo.worldPosition,
                                    customizeParameter, cancellationToken);
                    }
                }

                Directory.CreateDirectory(path);
                var gltfPath = Path.Combine(path, "model.gltf");
                var output = scene.ToGltf2();
                output.SaveGLTF(gltfPath);
                logger.Debug($"Exported model");
                if (config.OpenFolderWhenComplete)
                    Process.Start("explorer.exe", path);
            }
            catch (OperationCanceledException)
            {
                logger.Info("Export cancelled");
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to export model");
            } finally
            {
                IsExporting = false;
            }
        }, cancellationToken);
    }

    [GeneratedRegex("^chara/human/c\\d+/obj/body/b0003/model/c\\d+b0003_top.mdl$")]
    private static partial Regex LowPolyModelRegex();

    private static void ForEach<T>(IEnumerable<T> source, Action<T> action, bool parallel = false)
    {
        if (parallel)
            Parallel.ForEach(source, action);
        else
            foreach (var item in source)
                action(item);
    }
    
    private void ExportInternal(
        ExportLogger logger, ExportConfig config, CharacterTree character, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Plugin.TempDirectory,
                                $"{character.GetHashCode():X8}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
        logger.Debug($"Exporting to {path}");

        var scene = new SceneBuilder(string.IsNullOrWhiteSpace(character.Name) ? "scene" : character.Name);
        var boneMap = ModelUtility.GetBoneMap(character.Skeleton, out var rootBone).ToArray();
        scene.AddNode(rootBone);
        
        ForEach(character.Models, model =>
        {
            if (LowPolyModelRegex().IsMatch(model.Path)) return;

            HandleModel(logger, config, model, character.RaceCode!.Value, scene, boneMap, Matrix4x4.Identity,
                        character.CustomizeParameter, cancellationToken);
        }, config.ParallelBuild);

        for (var i = 0; i < character.AttachedChildren.Count; i++)
        {
            var child = character.AttachedChildren[i];
            var attachName = character.Skeleton.PartialSkeletons[child.Attach.PartialSkeletonIdx]
                                         .HkSkeleton!.BoneNames[child.Attach.BoneIdx];
            var childInfo = HandleAttachedChild(child, i, attachName, scene, boneMap, rootBone);
            foreach (var model in child.Models)
            {
                logger.Debug($"Handling child model {model.Path}");
                HandleModel(logger, config, model, character.RaceCode ?? 0, scene,
                            childInfo.childBoneMap.ToArray(),
                            childInfo.worldPosition,
                            character.CustomizeParameter, cancellationToken);
            }
        }

        Directory.CreateDirectory(path);
        var gltfPath = Path.Combine(path, "model.gltf");
        var output = scene.ToGltf2();
        output.SaveGLTF(gltfPath);
        logger.Debug($"Exported model to {gltfPath}");
        if (config.OpenFolderWhenComplete)
            Process.Start("explorer.exe", path);
    }

    private void HandleModel(
        ExportLogger logger, ExportConfig config, Model model, GenderRace targetRace, SceneBuilder scene,
        BoneNodeBuilder[] boneMap, Matrix4x4 worldPosition, CustomizeParameters? customizeParameter,
        CancellationToken cancellationToken = default)
    {
        logger.Debug($"Exporting model {model.Path}");
        var boneNodes = boneMap.Cast<NodeBuilder>().ToArray();

        var materials = CreateMaterials(logger, model, customizeParameter, cancellationToken).ToArray();

        IEnumerable<MeshExport> meshes;
        if (model.RaceCode != GenderRace.Unknown)
        {
            logger.Debug($"Setup deform for {model.Path} from {model.RaceCode} to {targetRace}");
            var raceDeformer = new RaceDeformer(Pbd, boneMap.ToArray());
            meshes = ModelBuilder.BuildMeshes(model, materials, boneMap, (targetRace, raceDeformer));
        }
        else
        {
            meshes = ModelBuilder.BuildMeshes(model, materials, boneMap, null);
        }

        foreach (var (mesh, useSkinning, subMesh, shapes) in meshes)
        {
            var instance = useSkinning
                               ? scene.AddSkinnedMesh(mesh, worldPosition, boneNodes)
                               : scene.AddRigidMesh(mesh, worldPosition);

            ApplyMeshShapes(instance, model, shapes);

            // Remove subMeshes that are not enabled
            if (subMesh != null)
            {
                // Reaper eye go away
                if (subMesh.Attributes.Contains("atr_eye_a") && config.IncludeReaperEye == false)
                {
                    instance.Remove();
                }
                else if (!subMesh.Attributes.All(model.EnabledAttributes.Contains))
                {
                    instance.Remove();
                }
            }
        }
    }

    private (BoneNodeBuilder rootBone, Matrix4x4 worldPosition, IReadOnlyList<BoneNodeBuilder> childBoneMap) HandleAttachedChild(AttachedChild child, 
                                     int i,
                                     string attachName,
                                     SceneBuilder scene,
                                     BoneNodeBuilder[]? boneMap, 
                                     NodeBuilder? rootBone)
    {
        var childBoneMap = ModelUtility.GetBoneMap(child.Skeleton, out var childRoot);
        childRoot!.SetSuffixRecursively(i);

        if (rootBone == null || boneMap == null)
        {
            scene.AddNode(childRoot);
        }
        else
        {
            var boneTarget = boneMap.First(b => b.BoneName.Equals(attachName, StringComparison.Ordinal));
            boneTarget.AddNode(childRoot);
        }

        var transform = Matrix4x4.Identity;
        NodeBuilder c = childRoot;
        while (c != null)
        {
            transform *= c.LocalMatrix;
            c = c.Parent;
        }
        
        return (childRoot, transform, childBoneMap);
    }

    private static void ApplyMeshShapes(InstanceBuilder builder, Model model, IReadOnlyList<string>? appliedShapes)
    {
        if (model.Shapes.Count == 0 || appliedShapes == null) return;

        // This will set the morphing value to 1 if the shape is enabled, 0 if not
        var shapes = model.Shapes
                          .Where(x => appliedShapes.Contains(x.Name))
                          .Select(x => (x, model.EnabledShapes.Contains(x.Name)));
        builder.Content.UseMorphing().SetValue(shapes.Select(x => x.Item2 ? 1f : 0).ToArray());
    }

    private IEnumerable<MaterialBuilder> CreateMaterials(
        ExportLogger logger, Model model, CustomizeParameters? customizeParameters, CancellationToken cancellationToken)
    {
        var materials = new MaterialBuilder[model.Materials.Count];

        for (var i = 0; i < model.Materials.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var material = model.Materials[i];
            logger.Debug($"Exporting material {material.HandlePath}");
;
            materials[i] = ParseMaterial(material, Path.GetFileName(material.HandlePath), customizeParameters);
        }

        return materials;
    }
    
    public static void ExportMaterial(Material material, ExportLogger logger, string directory, CustomizeParameters? customizeParameters = null)
    { 
        logger.Log(ExportLogger.LogEventLevel.Information, "Exporting material textures");
        Directory.CreateDirectory(directory);
        
        foreach (var texture in material.Textures)
        {
            logger.Log(ExportLogger.LogEventLevel.Information, $"Exporting texture {texture.Usage}");
            var skTexture = texture.Resource.ToTexture();
            var name = $"{texture.Usage}_{texture.Resource.Format}_xivraw.png";
            var path = Path.Combine(directory, name);
            using var outStream = File.OpenWrite(path);
            skTexture.Bitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(outStream);
        }
        
        logger.Log(ExportLogger.LogEventLevel.Information, "Parsing material");
        var materialBuilder = ParseMaterial(material, Path.GetFileName(material.HandlePath), customizeParameters);

        foreach (var channel in materialBuilder.Channels)
        {
            if (channel.Texture.PrimaryImage == null) continue;
            logger.Log(ExportLogger.LogEventLevel.Information, $"Exporting channel {channel.Key}");
            var image = channel.Texture.PrimaryImage;
            var name = $"{channel.Key}.{image.Content.FileExtension}";
            image.Content.SaveToFile(Path.Combine(directory, name));
        }
        
        logger.Log(ExportLogger.LogEventLevel.Information, "Exported material textures");
        Process.Start("explorer.exe", directory);
    }
    
    public static MaterialBuilder ParseMaterial(Material material, string name, CustomizeParameters? customizeParameter = null)
    {
        name = $"{name}_{material.ShaderPackage.Name.Replace(".shpk", "")}";

        return material.ShaderPackage.Name switch
        {
            "character.shpk" => MaterialUtility.BuildCharacter(material, name).WithAlpha(AlphaMode.MASK, 0.5f),
            "characterglass.shpk" => MaterialUtility.BuildCharacter(material, name).WithAlpha(AlphaMode.BLEND),
            "hair.shpk" => MaterialUtility.BuildHair(material, name, HairShaderParameters.From(customizeParameter)),
            "iris.shpk"           => MaterialUtility.BuildIris(material, name, customizeParameter?.LeftColor),
            "skin.shpk"           => MaterialUtility.BuildSkin(material, name, SkinShaderParameters.From(customizeParameter)),
            _ => MaterialUtility.BuildFallback(material, name),
        };
    }
}
