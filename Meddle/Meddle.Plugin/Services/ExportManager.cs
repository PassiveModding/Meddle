using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using Meddle.Plugin.Enums;
using Meddle.Plugin.Files;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Utility;
using Meddle.Plugin.Utility.Materials;
using Meddle.Plugin.Xande;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;
using RaceDeformer = Meddle.Plugin.Xande.RaceDeformer;

namespace Meddle.Plugin.Services;

public partial class ExportManager : IDisposable
{
    public IPluginLog Log { get; }
    public ModelBuilder ModelBuilder { get; }
    public PbdFile Pbd { get; }
    public bool IsExporting { get; private set; }
    public CancellationTokenSource CancellationTokenSource { get; private set; }

    public ExportManager(IPluginLog log, IDataManager gameData, ModelBuilder modelBuilder)
    {
        Log = log;
        ModelBuilder = modelBuilder;
        var fileContent =
            gameData.GetFile("chara/xls/boneDeformer/human.pbd") ??
            throw new InvalidOperationException("Failed to load PBD file");
        Pbd = new PbdFile(fileContent.Data);
        CancellationTokenSource = new CancellationTokenSource();
    }


    public async Task Export(
        ExportLogger logger, ExportConfig config, CharacterTree characterTree)
    {
        if (IsExporting)
            throw new InvalidOperationException("Already exporting.");
        
        CancellationTokenSource = new CancellationTokenSource();

        IsExporting = true;
        await Task.Run(() =>
        {
            try
            {
                ExportInternal(logger, config, characterTree);
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
        }, CancellationTokenSource.Token);
    }

    public async Task Export(
        ExportLogger logger, ExportConfig config, 
        Model[] models, 
        AttachedChild[] attachedChildren,
        Skeleton skeleton, 
        GenderRace? targetRace,
        CustomizeParameters? customizeParameter)
    {
        if (IsExporting)
            throw new InvalidOperationException("Already exporting.");
        
        CancellationTokenSource = new CancellationTokenSource();

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
                    HandleModel(logger, config, model, targetRace, scene, boneMap, Matrix4x4.Identity, customizeParameter);
                }, config.ParallelBuild);
                
                for (var i = 0; i < attachedChildren.Length; i++)
                {
                    var child = attachedChildren[i];
                    var attachName = skeleton.PartialSkeletons[child.Attach.PartialSkeletonIdx]
                                              .HkSkeleton!.BoneNames[child.Attach.BoneIdx];
                    var childInfo = HandleAttachedChild(child, i, attachName, scene, boneMap);

                    foreach (var model in child.Models)
                    {
                        logger.Debug($"Handling child model {model.Path}");
                        HandleModel(logger, config, model, targetRace, scene,
                                    childInfo.ChildBoneMap,
                                    childInfo.WorldPosition,
                                    customizeParameter);
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
        }, CancellationTokenSource.Token);
    }

    [GeneratedRegex(@"^chara/human/c\d+/obj/body/b0003/model/c\d+b0003_top.mdl$")]
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
        ExportLogger logger, ExportConfig config, CharacterTree character)
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

            HandleModel(logger, config, model, character.RaceCode, scene, boneMap, Matrix4x4.Identity,
                        character.CustomizeParameter);
        }, config.ParallelBuild);
        
        for (var i = 0; i < character.AttachedChildren.Count; i++)
        {
            var child = character.AttachedChildren[i];
            if (child.Attach.ExecuteType != 4) continue;
            var attachName = character.Skeleton.PartialSkeletons[child.Attach.PartialSkeletonIdx]
                                         .HkSkeleton!.BoneNames[child.Attach.BoneIdx];
            var childInfo = HandleAttachedChild(child, i, attachName, scene, boneMap);

            foreach (var model in child.Models)
            {
                logger.Debug($"Handling child model {model.Path}");
                HandleModel(logger, config, model, character.RaceCode, scene,
                            childInfo.ChildBoneMap,
                            childInfo.WorldPosition,
                            character.CustomizeParameter);
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
        ExportLogger logger, ExportConfig config, Model model, GenderRace? targetRace, SceneBuilder scene,
        IReadOnlyList<BoneNodeBuilder> boneMap, Matrix4x4 worldPosition, CustomizeParameters? customizeParameter)
    {
        logger.Debug($"Exporting model {model.Path}");
        var boneNodes = boneMap.Cast<NodeBuilder>().ToArray();

        var materials = CreateMaterials(logger, model, customizeParameter).ToArray();

        IEnumerable<MeshExport> meshes;
        if (model.RaceCode != GenderRace.Unknown && targetRace != null && targetRace != model.RaceCode && targetRace != GenderRace.Unknown)
        {
            logger.Debug($"Setup deform for {model.Path} from {model.RaceCode} to {targetRace}");
            var raceDeformer = new RaceDeformer(Pbd, boneMap.ToArray());
            meshes = ModelBuilder.BuildMeshes(model, materials, boneMap, (targetRace.Value, raceDeformer));
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

    public record AttachedChildOutput(Matrix4x4 WorldPosition, IReadOnlyList<BoneNodeBuilder> ChildBoneMap);
    
    private AttachedChildOutput HandleAttachedChild(AttachedChild child, 
                                     int i,
                                     string? attachName,
                                     SceneBuilder scene,
                                     BoneNodeBuilder[]? boneMap)
    {
        var childBoneMap = ModelUtility.GetBoneMap(child.Skeleton, out var childRoot);
        childRoot!.SetSuffixRecursively(i);

        if (child.Attach.OffsetTransform is { } ct)
        {
            // This appears to fix weapon attaches
            childRoot.WithLocalScale(ct.Scale);
            childRoot.WithLocalRotation(ct.Rotation);
            childRoot.WithLocalTranslation(ct.Translation);
            if (childRoot.AnimationTracksNames.Contains("pose"))
            {
                childRoot.UseScale().UseTrackBuilder("pose").WithPoint(0, ct.Scale);
                childRoot.UseRotation().UseTrackBuilder("pose").WithPoint(0, ct.Rotation);
                childRoot.UseTranslation().UseTrackBuilder("pose").WithPoint(0, ct.Translation);
            }
        }

        var childBone = boneMap?.FirstOrDefault(b => b.BoneName.Equals(attachName, StringComparison.Ordinal));
        if (childBone == null)
        {
            scene.AddNode(childRoot);
        }
        else
        {
            childBone.AddNode(childRoot);
        }

        var transform = Matrix4x4.Identity;
        NodeBuilder c = childRoot;
        while (c != null)
        {
            transform *= c.LocalMatrix;
            c = c.Parent;
        }
        
        return new AttachedChildOutput(transform, childBoneMap.ToArray());
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
        ExportLogger logger, Model model, CustomizeParameters? customizeParameters)
    {
        var materials = new MaterialBuilder[model.Materials.Count];

        for (var i = 0; i < model.Materials.Count; i++)
        {
            CancellationTokenSource.Token.ThrowIfCancellationRequested();

            var material = model.Materials[i];
            if (material == null)
            {
                materials[i] = new MaterialBuilder("fallback");
                continue;
            }

            logger.Debug($"Exporting material {material.HandlePath}");

            materials[i] = MaterialUtility.ParseMaterial(material, customizeParameters);
        }

        return materials;
    }
    
    public async Task ExportMaterial(Material material, 
                    ExportLogger logger, 
                    CustomizeParameters? customizeParameters = null)
    {        
        if (IsExporting)
            throw new InvalidOperationException("Already exporting.");
        
        CancellationTokenSource = new CancellationTokenSource();

        IsExporting = true;
        await Task.Run(async () =>
        {
            try
            {
                var directory = Path.Combine(Plugin.TempDirectory, "Materials", 
                    $"{material.ShaderPackage.Name}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
                logger.Log(ExportLogger.LogEventLevel.Information, "Exporting material textures");
                Directory.CreateDirectory(directory);

                foreach (var texture in material.Textures)
                {
                    logger.Log(ExportLogger.LogEventLevel.Information, $"Exporting texture {texture.Usage}");
                    var skTexture = texture.Resource.ToTexture();
                    var name = $"{texture.Usage}_{texture.Resource.Format}_xivraw.png";
                    var path = Path.Combine(directory, name);
                    await using var outStream = File.OpenWrite(path);
                    skTexture.Bitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(outStream);
                }

                logger.Log(ExportLogger.LogEventLevel.Information, "Parsing material");
                var materialBuilder = MaterialUtility.ParseMaterial(material, customizeParameters);

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
            catch (Exception e)
            {
                logger.Error(e, "Failed to export material textures");
            } finally
            {
                IsExporting = false;
            }
        }, CancellationTokenSource.Token);
    }

    public void Dispose()
    {
        CancellationTokenSource.Dispose();
    }
}
