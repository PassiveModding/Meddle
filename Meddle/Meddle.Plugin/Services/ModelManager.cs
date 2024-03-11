using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Utility;
using Meddle.Plugin.Xande.Models;
using Meddle.Plugin.Xande.Utility;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using Xande.Enums;
using Xande.Files;
using Xande.Models.Export;

namespace Meddle.Plugin.Services;

public partial class ModelManager
{
    public IPluginLog Log { get; }
    public ModelBuilder ModelBuilder { get; }
    public PbdFile Pbd { get; set; }
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


    public async Task Export(ExportConfig config, CharacterTree characterTree, CancellationToken cancellationToken)
    {
        if (IsExporting)
            throw new InvalidOperationException("Already exporting.");
        
        IsExporting = true;
        await Task.Run(async () =>
        {
            try
            {
                await ExportInternal(config, characterTree, cancellationToken);
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to export model");
            } finally
            {
                IsExporting = false;
            }
        }, cancellationToken);
    }
    
    
    [GeneratedRegex("^chara/human/c\\d+/obj/body/b0003/model/c\\d+b0003_top.mdl$")]
    private static partial Regex LowPolyModelRegex();
    
    private async Task ExportInternal(ExportConfig config, CharacterTree character, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Plugin.TempDirectory, $"{character.GetHashCode():X8}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
        Log.Debug("Exporting to {Path}", path);
        
        var scene = new SceneBuilder(string.IsNullOrWhiteSpace(character.Name) ? "scene" : character.Name);
        var boneMap = ModelUtility.GetBoneMap(character.Skeleton, out var rootBone).ToArray();
        scene.AddNode(rootBone);

        foreach (var model in character.Models)
        {
            if (LowPolyModelRegex().IsMatch(model.HandlePath)) continue;
            
            HandleModel(config, model, character, scene, boneMap);
        }

        if (character.AttachedChildren != null)
        {
            var i = 0;
            foreach (var child in character.AttachedChildren)
            {
                var childBoneMap = ModelUtility.GetBoneMap(child.Skeleton, out var childRoot);
                childRoot!.SetSuffixRecursively(i++);
                var attachName = character.Skeleton.PartialSkeletons[child.Attach.PartialSkeletonIdx].HkSkeleton!.BoneNames[child.Attach.BoneIdx];

                if (rootBone == null || boneMap == null)
                    scene.AddNode(childRoot);
                else
                    boneMap.First(b => b.BoneName.Equals(attachName, StringComparison.Ordinal)).AddNode(childRoot);

                var transform = Matrix4x4.Identity;
                NodeBuilder c = childRoot!;
                while (c != null)
                {
                    transform *= c.LocalMatrix;
                    c = c.Parent;
                }

                foreach (var model in child.Models)
                {
                    Log.Debug($"Handling child model {model.HandlePath}");
                    HandleModel(config, model, child, scene, childBoneMap.ToArray());
                }
            }
        }
        
        Directory.CreateDirectory(path);
        var gltfPath = Path.Combine(path, "model.gltf");
        var output = scene.ToGltf2();
        output.SaveGLTF(gltfPath);
        Log.Debug($"Exported model to {gltfPath}");
        if (config.OpenFolderWhenComplete)
            Process.Start("explorer.exe", path);
    }
    
    private void HandleModel(ExportConfig config, Model model, CharacterTree character, SceneBuilder scene, BoneNodeBuilder[] boneMap)
    {
        Log.Debug("Exporting model {Model}", model.HandlePath);

        var materials = CreateMaterials(model).ToArray();
            
        IEnumerable<(IMeshBuilder<MaterialBuilder> mesh, bool useSkinning, SubMesh? submesh)> meshes;
        if (model.RaceCode.HasValue && model.RaceCode.Value != (ushort)GenderRace.Unknown)
        {
            Log.Debug($"Setup deform for {model.HandlePath} from {model.RaceCode} to {character.RaceCode}");
            var raceDeformer = new RaceDeformer(Pbd, boneMap.ToArray());
            meshes = ModelBuilder.BuildMeshes(model,
                                              materials,
                                              boneMap,
                                              (character.RaceCode!.Value, raceDeformer!));
        }
        else
        {
            meshes = ModelBuilder.BuildMeshes(model, materials, boneMap, null);
        }

        foreach (var (mesh, useSkinning, submesh) in meshes)
        {
            var instance = useSkinning ? 
                               scene.AddSkinnedMesh(mesh, Matrix4x4.Identity, boneMap) : 
                               scene.AddRigidMesh(mesh, Matrix4x4.Identity);

            if (submesh != null)
            {
                ApplyMeshModifiers(config, instance, submesh, model);
            }
            else
            {
                ApplyMeshShapes(instance, model);
            }
        }
    }
    
    private static void ApplyMeshModifiers(ExportConfig config, InstanceBuilder builder, SubMesh subMesh, Model model)
    {
        ApplyMeshShapes(builder, model);
        if (subMesh.Attributes.Contains("atr_eye_a") && config.IncludeReaperEye == false)
            builder.Remove();
        else if (!subMesh.Attributes.All(model.EnabledAttributes.Contains))
            builder.Remove();
    }
    
    private static void ApplyMeshShapes(InstanceBuilder builder, Model model)
    {
        if (model.Shapes.Count != 0)
            builder.Content.UseMorphing().SetValue(
                model.Shapes.Select(s => 
                        model.EnabledShapes.Any(n => s.Name.Equals(n, StringComparison.Ordinal)) ? 1f : 0).ToArray());
    }

    private IEnumerable<MaterialBuilder> CreateMaterials(Model model)
    {
        var materials = new MaterialBuilder[model.Materials.Count];

        for (var i = 0; i < model.Materials.Count; i++)
        {
            var material = model.Materials[i];
            Log.Debug("Exporting material {Material}", material.HandlePath);
            
            var name = Path.GetFileName(material.HandlePath);
            materials[i] = MaterialUtility.ParseMaterial(material, name);
        }

        return materials;
    }
}
