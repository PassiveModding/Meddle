using System.Diagnostics;
using System.Numerics;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Skeleton;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Meddle.Utils.Skeletons;
using Meddle.Utils.Skeletons.Havok;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;
using CustomizeData = Meddle.Utils.Export.CustomizeData;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;
using Material = Meddle.Utils.Export.Material;
using Model = Meddle.Utils.Export.Model;

namespace Meddle.Plugin.Utils;

public class ExportUtil
{
    private readonly SqPack pack;

    public record CharacterGroup(CustomizeParameter CustomizeParams, CustomizeData CustomizeData, GenderRace GenderRace, Model.MdlGroup[] MdlGroups, Skeleton.Skeleton Skeleton, AttachedModelGroup[] AttachedModelGroups);
    public record AttachedModelGroup(Attach Attach, Model.MdlGroup[] MdlGroups, Skeleton.Skeleton Skeleton);
    //public record CharacterGroupHK(CustomizeParameter CustomizeParams, CustomizeData CustomizeData, GenderRace GenderRace, Model.MdlGroup[] MdlGroups, IEnumerable<HavokXml> Skeletons);
    //public record CharacterGroupCT(CustomizeParameter CustomizeParams, CustomizeData CustomizeData, GenderRace GenderRace, Model.MdlGroup[] MdlGroups, IEnumerable<HavokXml> Skeletons);

    public ExportUtil(SqPack pack)
    {
        this.pack = pack;
    }
    
    private string GetPathForOutput()
    {
        var now = DateTime.Now;
        var folder = Path.Combine(Plugin.TempDirectory, "output", now.ToString("yyyy-MM-dd-HH-mm-ss"));
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        return folder;
    }
    
    public void ExportTexture(SKBitmap bitmap, string path)
    {
        var folder = GetPathForOutput();
        var outputPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)}.png");
            
        var str = new SKDynamicMemoryWStream();
        bitmap.Encode(str, SKEncodedImageFormat.Png, 100);

        var data = str.DetachAsData().AsSpan();
        File.WriteAllBytes(outputPath, data.ToArray());
        Process.Start("explorer.exe", folder);
    }
    
    public void ExportRawTextures(CharacterGroup characterGroup, CancellationToken token = default)
    {
        try
        {
            var folder = GetPathForOutput();

            foreach (var mdlGroup in characterGroup.MdlGroups)
            {
                foreach (var mtrlGroup in mdlGroup.MtrlFiles)
                {
                    foreach (var texGroup in mtrlGroup.TexFiles)
                    {
                        if (token.IsCancellationRequested) return;
                        var outputPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(texGroup.Path)}.png");
                        var texture = new Texture(texGroup.TexFile, texGroup.Path, null, null, null);
                        var str = new SKDynamicMemoryWStream();
                        texture.ToTexture().Bitmap.Encode(str, SKEncodedImageFormat.Png, 100);

                        var data = str.DetachAsData().AsSpan();
                        File.WriteAllBytes(outputPath, data.ToArray());
                    }
                }
            }
            Process.Start("explorer.exe", folder);
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "Failed to export textures");
            throw;
        }
    }
    
    public void Export(CharacterGroup characterGroup, PbdFile? pbd, CancellationToken token = default)
    {
        try
        {
            var catchlight = pack.GetFile("chara/common/texture/sphere_d_array.tex");
            if (catchlight == null) throw new InvalidOperationException("Failed to get catchlight texture");
            var catchlightTex = new TexFile(catchlight.Value.file.RawData);
            
            var scene = new SceneBuilder();

            
            var bones = SkeletonUtils.GetBoneMap(characterGroup.Skeleton, out var root);
            //var bones = XmlUtils.GetBoneMap(characterGroup.Skeletons, out var root);
            if (root != null)
            {
                scene.AddNode(root);
            }
            
            var meshOutput = new List<(Model, ModelBuilder.MeshExport)>();
            foreach (var mdlGroup in characterGroup.MdlGroups)
            {
                if (mdlGroup.Path.Contains("b0003_top")) continue;
                var meshes = HandleModel(characterGroup, mdlGroup, pbd, catchlightTex, bones, token);
                meshOutput.AddRange(meshes);
            }
            
            var meshOutputAttach = new List<(Matrix4x4 Position, Model Model, ModelBuilder.MeshExport Mesh, BoneNodeBuilder[] Bones)>();
            for (int i = 0; i < characterGroup.AttachedModelGroups.Length; i++)
            {
                var attachedModelGroup = characterGroup.AttachedModelGroups[i];
                var attachName = characterGroup.Skeleton.PartialSkeletons[attachedModelGroup.Attach.PartialSkeletonIdx]
                                               .HkSkeleton!.BoneNames[attachedModelGroup.Attach.BoneIdx];
                var attachBones = SkeletonUtils.GetBoneMap(attachedModelGroup.Skeleton, out var attachRoot);
                if (attachRoot == null)
                {
                    throw new InvalidOperationException("Failed to get attach root");
                }
                attachRoot.SetSuffixRecursively(i);

                if (attachedModelGroup.Attach.OffsetTransform is { } ct)
                {
                    attachRoot.WithLocalScale(ct.Scale);
                    attachRoot.WithLocalRotation(ct.Rotation);
                    attachRoot.WithLocalTranslation(ct.Translation);
                    if (attachRoot.AnimationTracksNames.Contains("pose"))
                    {
                        attachRoot.UseScale().UseTrackBuilder("pose").WithPoint(0, ct.Scale);
                        attachRoot.UseRotation().UseTrackBuilder("pose").WithPoint(0, ct.Rotation);
                        attachRoot.UseTranslation().UseTrackBuilder("pose").WithPoint(0, ct.Translation);
                    }
                }
                
                var attachPointBone = bones.FirstOrDefault(b => b.BoneName.Equals(attachName, StringComparison.Ordinal));
                if (attachPointBone == null)
                {
                    scene.AddNode(attachRoot);
                }
                else
                {
                    attachPointBone.AddNode(attachRoot);
                }
                
                
                var transform = Matrix4x4.Identity;
                NodeBuilder c = attachRoot;
                while (c != null)
                {
                    transform *= c.LocalMatrix;
                    c = c.Parent;
                }
                
                foreach (var mdlGroup in attachedModelGroup.MdlGroups)
                {
                    var meshes = HandleModel(characterGroup, mdlGroup, pbd, catchlightTex, attachBones, token);
                    foreach (var mesh in meshes)
                    {
                        meshOutputAttach.Add((transform, mesh.model, mesh.mesh, attachBones.ToArray()));
                    }
                }
            }

            foreach (var (model, mesh) in meshOutput)
            {
                if (token.IsCancellationRequested) return;
                AddMesh(scene, Matrix4x4.Identity, model, mesh, bones.ToArray());
            }
            
            foreach (var (position, model, mesh, attachBones) in meshOutputAttach)
            {
                if (token.IsCancellationRequested) return;
                AddMesh(scene, position, model, mesh, attachBones);
            }

            var sceneGraph = scene.ToGltf2();
            var folder = GetPathForOutput();
            var outputPath = Path.Combine(folder, "character.gltf");
            sceneGraph.SaveGLTF(outputPath);
            Process.Start("explorer.exe", folder);
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "Failed to export character");
            throw;
        }
    }

    public static void AddMesh(SceneBuilder scene, Matrix4x4 position, Model model, ModelBuilder.MeshExport mesh, BoneNodeBuilder[] bones)
    {
        InstanceBuilder instance;
        if (mesh.UseSkinning && bones.Length > 0)
        {
            instance = scene.AddSkinnedMesh(mesh.Mesh, position, bones.Cast<NodeBuilder>().ToArray());
        }
        else
        {
            instance = scene.AddRigidMesh(mesh.Mesh, position);
        }

        ApplyMeshShapes(instance, model, mesh.Shapes);

        // Remove subMeshes that are not enabled
        if (mesh.Submesh != null)
        {
            var enabledAttributes = Model.GetEnabledValues(model.EnabledAttributeMask, 
                                                           model.AttributeMasks);
                    
            if (!mesh.Submesh.Attributes.All(enabledAttributes.Contains))
            {
                instance.Remove();
            }
        }
    }

    private static List<(Model model, ModelBuilder.MeshExport mesh)> HandleModel(CharacterGroup characterGroup, Model.MdlGroup mdlGroup, PbdFile? pbd, TexFile catchlightTex, List<BoneNodeBuilder> bones, CancellationToken token)
    {
        Service.Log.Information("Exporting {Path}", mdlGroup.Path);
        var model = new Model(mdlGroup);
        var materials = new List<MaterialBuilder>();
        var meshOutput = new List<(Model, ModelBuilder.MeshExport)>();
        //Parallel.ForEach(mdlGroup.MtrlFiles, mtrlGroup =>
        foreach (var mtrlGroup in mdlGroup.MtrlFiles)
        {
            if (token.IsCancellationRequested) return meshOutput;
            
            Service.Log.Information("Exporting {Path}", mtrlGroup.Path);
            var material = new Material(mtrlGroup);
            var name =
                $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_{Path.GetFileNameWithoutExtension(material.ShaderPackageName)}";
            var builder = material.ShaderPackageName switch
            {
                "bg.shpk" => MaterialUtility.BuildBg(material, name),
                "bgprop.shpk" => MaterialUtility.BuildBgProp(material, name),
                "character.shpk" => MaterialUtility.BuildCharacter(material, name),
                "characterocclusion.shpk" => MaterialUtility.BuildCharacterOcclusion(material, name),
                "characterlegacy.shpk" => MaterialUtility.BuildCharacterLegacy(material, name),
                "charactertattoo.shpk" => MaterialUtility.BuildCharacterTattoo(
                    material, name, characterGroup.CustomizeParams, characterGroup.CustomizeData),
                "hair.shpk" => MaterialUtility.BuildHair(material, name, characterGroup.CustomizeParams,
                                                         characterGroup.CustomizeData),
                "skin.shpk" => MaterialUtility.BuildSkin(material, name, characterGroup.CustomizeParams,
                                                         characterGroup.CustomizeData),
                "iris.shpk" => MaterialUtility.BuildIris(material, name, catchlightTex, characterGroup.CustomizeParams,
                                                         characterGroup.CustomizeData),
                _ => MaterialUtility.BuildFallback(material, name)
            };

            materials.Add(builder);
        }
        
        (GenderRace, RaceDeformer)? raceDeformerValue = pbd != null ? (characterGroup.GenderRace, new RaceDeformer(pbd, bones)) : null;

        var meshes = ModelBuilder.BuildMeshes(model, materials, bones, raceDeformerValue);
        foreach (var mesh in meshes)
        {
            meshOutput.Add((model, mesh));
        }
        
        return meshOutput;
    }
    
    private static void ApplyMeshShapes(InstanceBuilder builder, Model model, IReadOnlyList<string>? appliedShapes)
    {
        if (model.Shapes.Count == 0 || appliedShapes == null) return;

        // This will set the morphing value to 1 if the shape is enabled, 0 if not
        var enabledShapes = Model.GetEnabledValues(model.EnabledShapeMask, model.ShapeMasks)
                                 .ToArray();
        var shapes = model.Shapes
                          .Where(x => appliedShapes.Contains(x.Name))
                          .Select(x => (x, enabledShapes.Contains(x.Name)));
        builder.Content.UseMorphing().SetValue(shapes.Select(x => x.Item2 ? 1f : 0).ToArray());
    }

    public record Resource(string mdlPath, Vector3 position, Quaternion rotation, Vector3 scale);
    public void ExportResource(Resource[] resources, Vector3 rootPosition)
    {
        try
        {
            var scene = new SceneBuilder();
            var materialCache = new Dictionary<string, MaterialBuilder>();
            foreach (var resource in resources)
            {
                var mdlFileData = pack.GetFile(resource.mdlPath);
                if (mdlFileData == null) throw new InvalidOperationException("Failed to get resource");
                var data = mdlFileData.Value.file.RawData;
                var mdlFile = new MdlFile(data);
                var mtrlGroups = new List<Material.MtrlGroup>();
                foreach (var (mtrlOff, mtrlPath) in mdlFile.GetMaterialNames())
                {
                    if (mtrlPath.StartsWith('/')) throw new InvalidOperationException($"Relative path found on material {mtrlPath}");
                    var mtrlResource = pack.GetFile(mtrlPath);
                    if (mtrlResource == null) throw new InvalidOperationException("Failed to get mtrl resource");
                    var mtrlData = mtrlResource.Value.file.RawData;
                    
                    var mtrlFile = new MtrlFile(mtrlData);
                    
                    var shpkPath = mtrlFile.GetShaderPackageName();
                    var shpkResource = pack.GetFile($"shader/sm5/shpk/{shpkPath}");
                    if (shpkResource == null) throw new InvalidOperationException("Failed to get shpk resource");
                    var shpkFile = new ShpkFile(shpkResource.Value.file.RawData);
                    var texGroups = new List<Texture.TexGroup>();
                    foreach (var (texOff, texPath) in mtrlFile.GetTexturePaths())
                    {
                        var texResource = pack.GetFile(texPath);
                        if (texResource == null) throw new InvalidOperationException("Failed to get tex resource");
                        var texData = texResource.Value.file.RawData;
                        var texFile = new TexFile(texData);
                        texGroups.Add(new Texture.TexGroup(texPath, texFile));
                    }

                    mtrlGroups.Add(new Material.MtrlGroup(mtrlPath, mtrlFile, shpkPath, shpkFile, texGroups.ToArray()));
                }
                
                var model = new Model(new Model.MdlGroup(resource.mdlPath, mdlFile, mtrlGroups.ToArray(), null));
                var materials = new List<MaterialBuilder>();
                foreach (var mtrlGroup in mtrlGroups)
                {
                    if (materialCache.TryGetValue(mtrlGroup.Path, out var builder))
                    {
                        materials.Add(builder);
                        continue;
                    }
                    var material = new Material(mtrlGroup);
                    var name =
                        $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_{Path.GetFileNameWithoutExtension(material.ShaderPackageName)}";
                    builder = material.ShaderPackageName switch
                    {
                        "bg.shpk" => MaterialUtility.BuildBg(material, name),
                        "bgprop.shpk" => MaterialUtility.BuildBgProp(material, name),
                        _ => MaterialUtility.BuildFallback(material, name)
                    };

                    materials.Add(builder);
                    materialCache[mtrlGroup.Path] = builder;
                }
                
                var meshes = ModelBuilder.BuildMeshes(model, materials, [], null);
                var position = Matrix4x4.CreateTranslation(resource.position - rootPosition);
                var rotation = Matrix4x4.CreateFromQuaternion(resource.rotation);
                var scale = Matrix4x4.CreateScale(resource.scale);
                var transform = position * rotation * scale;
                
                foreach (var mesh in meshes)
                {
                    scene.AddRigidMesh(mesh.Mesh, transform);
                }
            }
            
            var sceneGraph = scene.ToGltf2();
            var folder = GetPathForOutput();
            var outputPath = Path.Combine(folder, "resource.gltf");
            sceneGraph.SaveGLTF(outputPath);
            Process.Start("explorer.exe", folder);
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "Failed to export resource");
            throw;
        }
    }
}
