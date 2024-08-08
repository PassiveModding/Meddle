using System.Diagnostics;
using System.Numerics;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Meddle.Utils.Skeletons;
using Microsoft.Extensions.Logging;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;
using Material = Meddle.Utils.Export.Material;
using Model = Meddle.Utils.Export.Model;

namespace Meddle.Plugin.Services;

public class ExportService : IDisposable
{
    private static readonly ActivitySource ActivitySource = new("Meddle.Plugin.Utils.ExportUtil");
    private readonly TexFile catchlightTex;
    private readonly TexFile tileNormTex;
    private readonly TexFile tileOrbTex;
    private readonly EventLogger<ExportService> logger;
    public event Action<LogLevel, string>? OnLogEvent; 
    private readonly SqPack pack;
    private readonly PbdFile pbdFile;
    
    public ExportService(SqPack pack, ILogger<ExportService> logger)
    {
        this.pack = pack;
        this.logger = new EventLogger<ExportService>(logger);
        this.logger.OnLogEvent += OnLog;

        // chara/xls/boneDeformer/human.pbd
        var pbdData = pack.GetFile("chara/xls/boneDeformer/human.pbd");
        if (pbdData == null) throw new Exception("Failed to load human.pbd");
        pbdFile = new PbdFile(pbdData.Value.file.RawData);

        var catchlight = pack.GetFile("chara/common/texture/sphere_d_array.tex");
        if (catchlight == null) throw new InvalidOperationException("Failed to get catchlight texture");
        
        var tileNorm = pack.GetFile("chara/common/texture/tile_norm_array.tex");
        if (tileNorm == null) throw new InvalidOperationException("Failed to get tile norm texture");
        
        var tileOrb = pack.GetFile("chara/common/texture/tile_orb_array.tex");
        if (tileOrb == null) throw new InvalidOperationException("Failed to get tile orb texture");
        tileNormTex = new TexFile(tileNorm.Value.file.RawData);
        tileOrbTex = new TexFile(tileOrb.Value.file.RawData);
        catchlightTex = new TexFile(catchlight.Value.file.RawData);
    }
    
    private void OnLog(LogLevel logLevel, string message)
    {
        OnLogEvent?.Invoke(logLevel, message);
    }

    private static string GetPathForOutput()
    {
        var now = DateTime.Now;
        var folder = Path.Combine(Plugin.TempDirectory, "output", now.ToString("yyyy-MM-dd-HH-mm-ss"));
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        return folder;
    }

    public static void ExportTexture(SKBitmap bitmap, string path)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("path", path);
        var folder = GetPathForOutput();
        var outputPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)}.png");

        using var str = new SKDynamicMemoryWStream();
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
            using var activity = ActivitySource.StartActivity();
            activity?.SetTag("folder", folder);

            foreach (var mdlGroup in characterGroup.MdlGroups)
            {
                foreach (var mtrlGroup in mdlGroup.MtrlFiles)
                {
                    foreach (var texGroup in mtrlGroup.TexFiles)
                    {
                        if (token.IsCancellationRequested) return;
                        var outputPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(texGroup.MtrlPath)}.png");
                        var texture = new Texture(texGroup.Resource, texGroup.MtrlPath, null, null, null);
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
            logger.LogError(e, "Failed to export textures");
            throw;
        }
    }

    public void ExportAnimation(List<(DateTime, AttachSet[])> frames, bool includePositionalData, CancellationToken token = default)
    {
        try
        {
            using var activity = ActivitySource.StartActivity();
            var boneSets = SkeletonUtils.GetAnimatedBoneMap(frames.ToArray());
            var startTime = frames.Min(x => x.Item1);
            var folder = GetPathForOutput();
            foreach (var (id, boneSet) in boneSets)
            {
                var scene = new SceneBuilder();
                if (boneSet.Root == null) throw new InvalidOperationException("Root bone not found");
                logger.LogInformation("Adding bone set {Id}", id);

                if (includePositionalData)
                {
                    var startPos = boneSet.Timeline.First().Attach.Transform.Translation;
                    foreach (var frameTime in boneSet.Timeline)
                    {
                        if (token.IsCancellationRequested) return;
                        var pos = frameTime.Attach.Transform.Translation;
                        var rot = frameTime.Attach.Transform.Rotation;
                        var scale = frameTime.Attach.Transform.Scale;
                        var time = SkeletonUtils.TotalSeconds(frameTime.Time, startTime);
                        var root = boneSet.Root;
                        root.UseTranslation().UseTrackBuilder("pose").WithPoint(time, pos - startPos);
                        root.UseRotation().UseTrackBuilder("pose").WithPoint(time, rot);
                        root.UseScale().UseTrackBuilder("pose").WithPoint(time, scale);
                    }
                }
                
                scene.AddNode(boneSet.Root);
                scene.AddSkinnedMesh(GetDummyMesh(id), Matrix4x4.Identity, boneSet.Bones.Cast<NodeBuilder>().ToArray());
                var sceneGraph = scene.ToGltf2();
                var outputPath = Path.Combine(folder, $"motion_{id}.gltf");
                sceneGraph.SaveGLTF(outputPath);
            }
            
            Process.Start("explorer.exe", folder);
            logger.LogInformation("Export complete");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to export animation");
            throw;
        }
    }
    
    // https://github.com/0ceal0t/Dalamud-VFXEditor/blob/be00131b93b3c6dd4014a4f27c2661093daf3a85/VFXEditor/Utils/Gltf/GltfSkeleton.cs#L132
    public static MeshBuilder<VertexPosition, VertexEmpty, VertexJoints4> GetDummyMesh(string name = "DUMMY_MESH") {
        var dummyMesh = new MeshBuilder<VertexPosition, VertexEmpty, VertexJoints4>( name );
        var material = new MaterialBuilder( "material" );

        var p1 = new VertexPosition
        {
            Position = new Vector3( 0.000001f, 0, 0 )
        };
        var p2 = new VertexPosition
        {
            Position = new Vector3( 0, 0.000001f, 0 )
        };
        var p3 = new VertexPosition
        {
            Position = new Vector3( 0, 0, 0.000001f )
        };

        dummyMesh.UsePrimitive( material ).AddTriangle(
            (p1, new VertexEmpty(), new VertexJoints4( 0 )),
            (p2, new VertexEmpty(), new VertexJoints4( 0 )),
            (p3, new VertexEmpty(), new VertexJoints4( 0 ))
        );

        return dummyMesh;
    }

    public void Export(CharacterGroup characterGroup, string? outputFolder = null, CancellationToken token = default)
    {
        try
        {
            using var activity = ActivitySource.StartActivity();
            var scene = new SceneBuilder();
            var bones = SkeletonUtils.GetBoneMap(characterGroup.Skeleton, true, out var root);
            //var bones = XmlUtils.GetBoneMap(characterGroup.Skeletons, out var root);
            if (root != null)
            {
                scene.AddNode(root);
            }

            var meshOutput = new List<(Model model, ModelBuilder.MeshExport mesh)>();
            foreach (var mdlGroup in characterGroup.MdlGroups)
            {
                if (token.IsCancellationRequested) return;
                if (mdlGroup.Path.Contains("b0003_top")) continue;
                try
                {
                    var meshes = HandleModel(characterGroup, mdlGroup, ref bones, root, token);
                    foreach (var mesh in meshes)
                    {
                        meshOutput.Add((mesh.model, mesh.mesh));
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to export model {Path}", mdlGroup.Path);
                    throw;
                }
            }

            if (token.IsCancellationRequested) return;

            var meshOutputAttach =
                new List<(Matrix4x4 Position, Model Model, ModelBuilder.MeshExport Mesh, BoneNodeBuilder[] Bones)>();
            for (var i = 0; i < characterGroup.AttachedModelGroups.Length; i++)
            {
                var attachedModelGroup = characterGroup.AttachedModelGroups[i];
                var attachName = characterGroup.Skeleton.PartialSkeletons[attachedModelGroup.Attach.PartialSkeletonIdx]
                                               .HkSkeleton!.BoneNames[(int)attachedModelGroup.Attach.BoneIdx];
                var attachBones = SkeletonUtils.GetBoneMap(attachedModelGroup.Skeleton, true, out var attachRoot);
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

                var attachPointBone =
                    bones.FirstOrDefault(b => b.BoneName.Equals(attachName, StringComparison.Ordinal));
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
                    var meshes = HandleModel(characterGroup, mdlGroup, ref attachBones, attachPointBone, token);
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
            if (outputFolder != null)
            {
                Directory.CreateDirectory(outputFolder);
            }
            
            var folder = outputFolder ?? GetPathForOutput();
            var outputPath = Path.Combine(folder, "character.gltf");
            sceneGraph.SaveGLTF(outputPath);
            Process.Start("explorer.exe", folder);
            logger.LogInformation("Export complete");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to export character");
            throw;
        }
    }

    public static void AddMesh(
        SceneBuilder scene, Matrix4x4 position, Model model, ModelBuilder.MeshExport mesh, BoneNodeBuilder[] bones)
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

    private MaterialBuilder HandleMaterial(CharacterGroup characterGroup, Material material, MtrlFileGroup mtrlGroup)
    {
        using var activityMtrl = ActivitySource.StartActivity();
        activityMtrl?.SetTag("mtrlPath", material.HandlePath);

        logger.LogInformation("Exporting {HandlePath} => {Path}", material.HandlePath, mtrlGroup.Path);
        activityMtrl?.SetTag("shaderPackageName", material.ShaderPackageName);
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
                                                     characterGroup.CustomizeData, (tileNormTex, tileOrbTex)),
            "iris.shpk" => MaterialUtility.BuildIris(material, name, catchlightTex, characterGroup.CustomizeParams,
                                                     characterGroup.CustomizeData),
            _ => MaterialUtility.BuildFallback(material, name)
        };

        return builder;
    }

    private List<(Model model, ModelBuilder.MeshExport mesh)> HandleModel(
        CharacterGroup characterGroup, MdlFileGroup mdlGroup, ref List<BoneNodeBuilder> bones, BoneNodeBuilder? root, CancellationToken token)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("characterPath", mdlGroup.CharacterPath);
        activity?.SetTag("path", mdlGroup.Path);
        logger.LogInformation("Exporting {CharacterPath} => {Path}", mdlGroup.CharacterPath, mdlGroup.Path);
        var model = new Model(mdlGroup.CharacterPath, mdlGroup.MdlFile, 
                              mdlGroup.MtrlFiles.Select(x => (
                                x.MdlPath, 
                                x.MtrlFile, 
                                x.TexFiles.ToDictionary(tf => tf.MtrlPath, tf => tf.Resource), 
                                x.ShpkFile)).ToArray(), 
                              mdlGroup.ShapeAttributeGroup);
        
        foreach (var mesh in model.Meshes)
        {
            if (mesh.BoneTable == null) continue;

            foreach (var boneName in mesh.BoneTable)
            {
                if (bones.All(b => !b.BoneName.Equals(boneName, StringComparison.Ordinal)))
                {
                    logger.LogInformation("Adding bone {BoneName} from mesh {MeshPath}", boneName,
                                          mdlGroup.Path);
                    var bone = new BoneNodeBuilder(boneName);
                    if (root == null) throw new InvalidOperationException("Root bone not found");
                    root.AddNode(bone);
                    logger.LogInformation("Added bone {BoneName} to {ParentBone}", boneName, root.BoneName);

                    bones.Add(bone);
                }
            }
        }
        
        
        var materials = new List<MaterialBuilder>();
        var meshOutput = new List<(Model, ModelBuilder.MeshExport)>();
        for (var i = 0; i < model.Materials.Count; i++)
        {
            if (token.IsCancellationRequested) return meshOutput;            
            var material = model.Materials[i];
            var materialGroup = mdlGroup.MtrlFiles[i];
            if (material == null) throw new InvalidOperationException("Material is null");
            var builder = HandleMaterial(characterGroup, material, materialGroup);
            materials.Add(builder);
        }

        if (token.IsCancellationRequested) return meshOutput;

        (GenderRace, RaceDeformer) raceDeformerValue;
        if (mdlGroup.DeformerGroup != null)
        {
            var pbdFileData = pack.GetFileOrReadFromDisk(mdlGroup.DeformerGroup.Path);
            if (pbdFileData == null) throw new InvalidOperationException($"Failed to get deformer pbd {mdlGroup.DeformerGroup.Path}");
            raceDeformerValue = ((GenderRace)mdlGroup.DeformerGroup.RaceSexId, new RaceDeformer(new PbdFile(pbdFileData), bones));
            model.RaceCode = (GenderRace)mdlGroup.DeformerGroup.DeformerId;
            logger.LogDebug("Using deformer pbd {Path}", mdlGroup.DeformerGroup.Path);
        }
        else
        {
            raceDeformerValue = (characterGroup.GenderRace, new RaceDeformer(pbdFile, bones));
        }

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

    public void ExportResource(Resource[] resources, Vector3 rootPosition)
    {
        try
        {
            var scene = new SceneBuilder();
            var materialCache = new Dictionary<string, MaterialBuilder>();
            foreach (var resource in resources)
            {
                var mdlFileData = pack.GetFile(resource.MdlPath);
                if (mdlFileData == null) throw new InvalidOperationException($"Failed to get resource {resource.MdlPath}");
                var data = mdlFileData.Value.file.RawData;
                var mdlFile = new MdlFile(data);
                var mtrlGroups = new List<MtrlFileGroup>();
                foreach (var (_, mtrlPath) in mdlFile.GetMaterialNames())
                {
                    if (mtrlPath.StartsWith('/'))
                        throw new InvalidOperationException($"Relative path found on material {mtrlPath}");
                    var mtrlResource = pack.GetFile(mtrlPath);
                    if (mtrlResource == null) throw new InvalidOperationException($"Failed to get mtrl resource {mtrlPath}");
                    var mtrlData = mtrlResource.Value.file.RawData;

                    var mtrlFile = new MtrlFile(mtrlData);

                    var shpkPath = mtrlFile.GetShaderPackageName();
                    var shpkResource = pack.GetFile($"shader/sm5/shpk/{shpkPath}");
                    if (shpkResource == null) throw new InvalidOperationException($"Failed to get shpk resource {shpkPath}");
                    var shpkFile = new ShpkFile(shpkResource.Value.file.RawData);
                    var texGroups = new List<TexResourceGroup>();
                    foreach (var (_, texPath) in mtrlFile.GetTexturePaths())
                    {
                        var texResource = pack.GetFile(texPath);
                        if (texResource == null) throw new InvalidOperationException($"Failed to get tex resource {texPath}");
                        var texData = texResource.Value.file.RawData;
                        var texFile = new TexFile(texData);
                        texGroups.Add(new TexResourceGroup(texPath, texPath, Texture.GetResource(texFile)));
                    }

                    mtrlGroups.Add(new MtrlFileGroup(mtrlPath, mtrlPath, mtrlFile, shpkPath, shpkFile, texGroups.ToArray()));
                }

                var model = new Model(resource.MdlPath, mdlFile,
                                      mtrlGroups.Select(x => (
                                                                 x.Path, 
                                                                 x.MtrlFile,
                                                                 x.TexFiles.ToDictionary(tf => tf.MtrlPath, tf => tf.Resource), 
                                                                 x.ShpkFile)
                                                    )
                                                .ToArray(),
                                      null);
                var materials = new List<MaterialBuilder>();
                foreach (var material in model.Materials)
                {
                    if (material == null) throw new InvalidOperationException("Material is null");
                    
                    if (materialCache.TryGetValue(material.HandlePath, out var builder))
                    {
                        materials.Add(builder);
                        continue;
                    }
                    var name =
                        $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_{Path.GetFileNameWithoutExtension(material.ShaderPackageName)}";
                    builder = material.ShaderPackageName switch
                    {
                        "bg.shpk" => MaterialUtility.BuildBg(material, name),
                        "bgprop.shpk" => MaterialUtility.BuildBgProp(material, name),
                        _ => BuildAndLogFallbackMaterial(material, name)
                    };

                    materials.Add(builder);
                    materialCache[material.HandlePath] = builder;
                }

                var meshes = ModelBuilder.BuildMeshes(model, materials, [], null);
                var position = Matrix4x4.CreateTranslation(resource.Position - rootPosition);
                var rotation = Matrix4x4.CreateFromQuaternion(resource.Rotation);
                var scale = Matrix4x4.CreateScale(resource.Scale);
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
            logger.LogInformation("Export complete");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to export resource");
            throw;
        }
    }
    
    private MaterialBuilder BuildAndLogFallbackMaterial(Material material, string name)
    {
        logger.LogWarning("Using fallback material for {Path}", material.HandlePath);
        return MaterialUtility.BuildFallback(material, name);
    }

    private void ExportTextureFromPath(string path)
    {
        var data = pack.GetFileOrReadFromDisk(path);
        if (data == null) throw new InvalidOperationException($"Failed to get texture {path}");
        var texFile = new TexFile(data);
        var texture = new Texture(Texture.GetResource(texFile), path, null, null, null);
        ExportTexture(texture.ToTexture().Bitmap, path);
    }
    
    public void Dispose()
    {
        logger.LogDebug("Disposing ExportUtil");
        OnLogEvent -= OnLog;
    }
}
