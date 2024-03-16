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
using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using RaceDeformer = Meddle.Plugin.Xande.RaceDeformer;

namespace Meddle.Plugin.Services;

public partial class ModelManager
{
    public IPluginLog Log { get; }
    public ModelBuilder ModelBuilder { get; }
    public PbdFile Pbd { get; }
    public bool IsExporting { get; private set; }

    public ModelManager(IPluginLog log, IDataManager gameData, ModelBuilder modelBuilder)
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
        await Task.Run(async () =>
        {
            try
            {
                await ExportInternal(logger, config, characterTree, cancellationToken);
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
        ExportLogger logger, ExportConfig config, Model model, Skeleton skeleton, ushort targetRace,
        CustomizeParameters? customizeParameter, CancellationToken cancellationToken)
    {
        if (IsExporting)
            throw new InvalidOperationException("Already exporting.");

        IsExporting = true;
        await Task.Run(async () =>
        {
            try
            {
                var path = Path.Combine(Plugin.TempDirectory,
                                        $"{model.GetHashCode():X8}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
                logger.Debug($"Exporting to {path}");

                var scene = new SceneBuilder("scene");
                var boneMap = ModelUtility.GetBoneMap(skeleton, out var rootBone).ToArray();
                scene.AddNode(rootBone);
                HandleModel(logger, config, model, targetRace, scene, boneMap, Matrix4x4.Identity, customizeParameter);

                Directory.CreateDirectory(path);
                var gltfPath = Path.Combine(path, "model.gltf");
                var output = scene.ToGltf2();
                output.SaveGLTF(gltfPath);
                logger.Debug($"Exported model");
                if (config.OpenFolderWhenComplete)
                    Process.Start("explorer.exe", path);
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

    private async Task ExportInternal(
        ExportLogger logger, ExportConfig config, CharacterTree character, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Plugin.TempDirectory,
                                $"{character.GetHashCode():X8}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
        logger.Debug($"Exporting to {path}");

        var scene = new SceneBuilder(string.IsNullOrWhiteSpace(character.Name) ? "scene" : character.Name);
        var boneMap = ModelUtility.GetBoneMap(character.Skeleton, out var rootBone).ToArray();
        scene.AddNode(rootBone);

        foreach (var model in character.Models)
        {
            if (LowPolyModelRegex().IsMatch(model.HandlePath)) continue;

            HandleModel(logger, config, model, character.RaceCode!.Value, scene, boneMap, Matrix4x4.Identity, character.CustomizeParameter);
        }

        if (character.AttachedChildren != null)
        {
            for (var i = 0; i < character.AttachedChildren.Count; i++)
            {
                var child = character.AttachedChildren[i];
                var childBoneMap = ModelUtility.GetBoneMap(child.Skeleton, out var childRoot);
                childRoot!.SetSuffixRecursively(i);

                // Name of the bone this model is attached to
                var attachName = character.Skeleton.PartialSkeletons[child.Attach.PartialSkeletonIdx]
                                          .HkSkeleton!.BoneNames[child.Attach.BoneIdx];

                if (rootBone == null || boneMap == null)
                    scene.AddNode(childRoot);
                else
                {
                    // Make root bone of this child a child of the attach bone
                    var boneTarget = boneMap.First(b => b.BoneName.Equals(attachName, StringComparison.Ordinal));
                    boneTarget.AddNode(childRoot);
                }

                var transform = Matrix4x4.Identity;
                NodeBuilder c = childRoot!;
                while (c != null)
                {
                    transform *= c.LocalMatrix;
                    c = c.Parent;
                }

                foreach (var model in child.Models)
                {
                    logger.Debug($"Handling child model {model.HandlePath}");
                    HandleModel(logger, config, model, character.RaceCode ?? 0, scene, 
                                childBoneMap.ToArray(),
                                transform,
                                character.CustomizeParameter);
                }
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
        ExportLogger logger, ExportConfig config, Model model, ushort targetRace, SceneBuilder scene,
        BoneNodeBuilder[] boneMap, Matrix4x4 worldPosition, CustomizeParameters? customizeParameter)
    {
        logger.Debug($"Exporting model {model.HandlePath}");

        var materials = CreateMaterials(logger, model, customizeParameter).ToArray();

        IEnumerable<(IMeshBuilder<MaterialBuilder> mesh, bool useSkinning, SubMesh? submesh, IReadOnlyList<string>?
            shapes)> meshes;
        if (model.RaceCode.HasValue && model.RaceCode.Value != (ushort)GenderRace.Unknown)
        {
            logger.Debug($"Setup deform for {model.HandlePath} from {model.RaceCode} to {targetRace}");
            var raceDeformer = new RaceDeformer(Pbd, boneMap.ToArray());
            meshes = ModelBuilder.BuildMeshes(model, materials, boneMap, (targetRace, raceDeformer!));
        }
        else
        {
            meshes = ModelBuilder.BuildMeshes(model, materials, boneMap, null);
        }

        foreach (var (mesh, useSkinning, submesh, shapes) in meshes)
        {
            var instance = useSkinning
                               ? scene.AddSkinnedMesh(mesh, worldPosition, boneMap)
                               : scene.AddRigidMesh(mesh, worldPosition);

            ApplyMeshShapes(instance, model, shapes);
            
            // Remove subMeshes that are not enabled
            if (submesh != null)
            {
                // Reaper eye for whatever reason is always enabled
                if (submesh.Attributes.Contains("atr_eye_a") && config.IncludeReaperEye == false)
                {
                    instance.Remove();
                }
                else if (!submesh.Attributes.All(model.EnabledAttributes.Contains))
                {
                    instance.Remove();
                }
            }
        }
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

    private IEnumerable<MaterialBuilder> CreateMaterials(ExportLogger logger, Model model, CustomizeParameters? cp)
    {
        var materials = new MaterialBuilder[model.Materials.Count];

        for (var i = 0; i < model.Materials.Count; i++)
        {
            var material = model.Materials[i];
            logger.Debug($"Exporting material {material.HandlePath}");

            var name = Path.GetFileName(material.HandlePath);
            materials[i] = MaterialUtility.ParseMaterial(material, name, cp);
        }

        return materials;
    }
}
