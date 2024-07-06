using System.Diagnostics;
using System.Numerics;
using Meddle.Plugin.Skeleton;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Skeletons;
using Meddle.Utils.Skeletons.Havok;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;
using CustomizeData = Meddle.Utils.Export.CustomizeData;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;
using Material = Meddle.Utils.Export.Material;
using Model = Meddle.Utils.Export.Model;
using Vector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;
using Vector4 = FFXIVClientStructs.FFXIV.Common.Math.Vector4;

namespace Meddle.Plugin.Utils;

public static class ExportUtil
{
    public record CharacterGroup(CustomizeParameter CustomizeParams, CustomizeData CustomizeData, GenderRace GenderRace, Model.MdlGroup[] MdlGroups, Skeleton.Skeleton Skeleton);
    public record CharacterGroupHK(CustomizeParameter CustomizeParams, CustomizeData CustomizeData, GenderRace GenderRace, Model.MdlGroup[] MdlGroups, IEnumerable<HavokXml> Skeletons);
    public static void ExportTexture(SKBitmap bitmap, string path)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "Meddle.Export", "output", $"{Path.GetFileNameWithoutExtension(path)}.png");
        var folder = Path.GetDirectoryName(outputPath);
        if (folder == null) throw new InvalidOperationException("Failed to get directory");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            // delete and recreate
            Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
        }
            
        var str = new SKDynamicMemoryWStream();
        bitmap.Encode(str, SKEncodedImageFormat.Png, 100);

        var data = str.DetachAsData().AsSpan();
        File.WriteAllBytes(outputPath, data.ToArray());
        Process.Start("explorer.exe", folder);
    }
    
    public static void ExportRawTextures(CharacterGroup characterGroup, CancellationToken token = default)
    {
        try
        {
            var folder = Path.Combine(Plugin.TempDirectory, "output", "textures");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                // delete and recreate
                Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }

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
    
    public static void Export(CharacterGroup characterGroup, PbdFile? pbd, CancellationToken token = default)
    {
        try
        {
            var scene = new SceneBuilder();

            foreach (var mdlGroup in characterGroup.MdlGroups)
            {
                if (mdlGroup.Path.Contains("b0003_top")) continue;
                Service.Log.Information("Exporting {Path}", mdlGroup.Path);
                var model = new Model(mdlGroup);
                var materials = new List<MaterialBuilder>();
                //Parallel.ForEach(mdlGroup.MtrlFiles, mtrlGroup =>
                foreach (var mtrlGroup in mdlGroup.MtrlFiles)
                {
                    if (token.IsCancellationRequested) return;
                    
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
                        "iris.shpk" => MaterialUtility.BuildIris(material, name, characterGroup.CustomizeParams,
                                                                 characterGroup.CustomizeData),
                        _ => MaterialUtility.BuildFallback(material, name)
                    };

                    materials.Add(builder);
                }

                var bones = SkeletonUtils.GetBoneMap(characterGroup.Skeleton, out var root);
                //var bones = XmlUtils.GetBoneMap(characterGroup.Skeletons, out var root);
                if (root != null)
                {
                    scene.AddNode(root);
                }
                
                var boneNodes = bones.Cast<NodeBuilder>().ToArray();
                (GenderRace, RaceDeformer)? raceDeformerValue = pbd != null ? (characterGroup.GenderRace, new RaceDeformer(pbd, bones)) : null;
                var meshes = ModelBuilder.BuildMeshes(model, materials, bones, raceDeformerValue);
                foreach (var mesh in meshes)
                {
                    if (token.IsCancellationRequested) return;
                    
                    InstanceBuilder instance;
                    if (mesh.UseSkinning && boneNodes.Length > 0)
                    {
                        instance = scene.AddSkinnedMesh(mesh.Mesh, Matrix4x4.Identity, boneNodes);
                    }
                    else
                    {
                        instance = scene.AddRigidMesh(mesh.Mesh, Matrix4x4.Identity);
                    }

                    //ApplyMeshShapes(instance, model, mesh.Shapes);

                    // Remove subMeshes that are not enabled
                    if (mesh.Submesh != null)
                    {
                        // Reaper eye go away (might not be necessary since DT)
                        if (mesh.Submesh.Attributes.Contains("atr_eye_a"))
                        {
                            instance.Remove();
                        }
                        else if (!mesh.Submesh.Attributes.All(model.EnabledAttributes.Contains))
                        {
                            instance.Remove();
                        }
                    }
                }
            }


            var sceneGraph = scene.ToGltf2();
            var outputPath = Path.Combine(Plugin.TempDirectory, "output", "model.mdl");
            var folder = Path.GetDirectoryName(outputPath) ?? "output";
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                // delete and recreate
                Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }

            // replace extension with gltf
            outputPath = Path.ChangeExtension(outputPath, ".gltf");

            sceneGraph.SaveGLTF(outputPath);
            Process.Start("explorer.exe", folder);
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "Failed to export character");
            throw;
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
    
    public static System.Numerics.Vector4 ToVector4(this Vector4 vec)
    {
        return new System.Numerics.Vector4(vec.X, vec.Y, vec.Z, vec.W);
    }
    
    public static System.Numerics.Vector3 ToVector3(this Vector3 vec)
    {
        return new System.Numerics.Vector3(vec.X, vec.Y, vec.Z);
    }
}
