using System.Numerics;
using System.Text.Json;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Models.Composer;

public class CharacterComposer
{
    private readonly DataProvider dataProvider;
    private readonly Action<ProgressEvent>? progress;
    private static readonly object StaticFileLock = new();
    private static PbdFile? DefaultPbdFile;
    private readonly SkeletonUtils.PoseMode poseMode;
    private readonly bool includePose;
    private readonly TextureMode textureMode;
    private bool arrayTexturesSaved;
    
    private void SaveArrayTextures()
    {
        if (arrayTexturesSaved) return;
        arrayTexturesSaved = true;
        lock (StaticFileLock)
        {
            try
            {
                var outDir = Path.Combine(this.dataProvider.GetCacheDir(), "array_textures");

                var catchlight = this.dataProvider.LookupData("chara/common/texture/sphere_d_array.tex");
                if (catchlight == null) throw new Exception("Failed to load catchlight texture");
                var catchLightTex = new TexFile(catchlight);
                var catchlightOutDir = Path.Combine(outDir, "chara/common/texture/sphere_d_array");
                Directory.CreateDirectory(catchlightOutDir);
                for (int i = 0; i < catchLightTex.Header.CalculatedArraySize; i++)
                {
                    var img = ImageUtils.GetTexData(catchLightTex, i, 0, 0);
                    var texture = img.ImageAsPng();
                    File.WriteAllBytes(Path.Combine(catchlightOutDir, $"sphere_d_array.{i}.png"), texture.ToArray());
                }

                var tileNorm = this.dataProvider.LookupData("chara/common/texture/tile_norm_array.tex");
                if (tileNorm == null) throw new Exception("Failed to load tile norm texture");
                var tileNormTex = new TexFile(tileNorm);
                var tileNormOutDir = Path.Combine(outDir, "chara/common/texture/tile_norm_array");
                Directory.CreateDirectory(tileNormOutDir);
                for (int i = 0; i < tileNormTex.Header.CalculatedArraySize; i++)
                {
                    var img = ImageUtils.GetTexData(tileNormTex, i, 0, 0);
                    var texture = img.ImageAsPng();
                    File.WriteAllBytes(Path.Combine(tileNormOutDir, $"tile_norm_array.{i}.png"), texture.ToArray());
                }

                var tileOrb = this.dataProvider.LookupData("chara/common/texture/tile_orb_array.tex");
                if (tileOrb == null) throw new Exception("Failed to load tile orb texture");
                var tileOrbTex = new TexFile(tileOrb);
                var tileOrbOutDir = Path.Combine(outDir, "chara/common/texture/tile_orb_array");
                Directory.CreateDirectory(tileOrbOutDir);
                for (int i = 0; i < tileOrbTex.Header.CalculatedArraySize; i++)
                {
                    var img = ImageUtils.GetTexData(tileOrbTex, i, 0, 0);
                    var texture = img.ImageAsPng();
                    File.WriteAllBytes(Path.Combine(tileOrbOutDir, $"tile_orb_array.{i}.png"), texture.ToArray());
                }
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError(e, "Failed to save array textures");
            }
        }
    }
    
    public CharacterComposer(DataProvider dataProvider, Configuration config, Action<ProgressEvent>? progress = null) 
    {
        this.dataProvider = dataProvider;
        this.progress = progress;
        includePose = config.IncludePose;
        poseMode = config.PoseMode;
        textureMode = config.TextureMode;
        
        lock (StaticFileLock)
        {
            if (DefaultPbdFile == null)
            {
                var pbdData = dataProvider.LookupData("chara/xls/boneDeformer/human.pbd");
                if (pbdData == null) throw new InvalidOperationException("Failed to load default pbd file");
                DefaultPbdFile = new PbdFile(pbdData);
            }
        }
    }

    private void HandleModel(GenderRace genderRace, CustomizeParameter customizeParameter, CustomizeData customizeData, 
                             ParsedModelInfo modelInfo, SceneBuilder scene, List<BoneNodeBuilder> bones, 
                             BoneNodeBuilder? rootBone,
                             Matrix4x4 transform)
    {
        SaveArrayTextures();
        
        if (modelInfo.Path.GamePath.Contains("b0003_top"))
        {
            Plugin.Logger?.LogDebug("Skipping model {ModelPath}", modelInfo.Path.GamePath);
            return;
        }
        var mdlData = dataProvider.LookupData(modelInfo.Path.FullPath);
        if (mdlData == null)
        {
            Plugin.Logger?.LogWarning("Failed to load model file: {modelPath}", modelInfo.Path);
            return;
        }

        Plugin.Logger?.LogInformation("Loaded model {modelPath}", modelInfo.Path.FullPath);
        var mdlFile = new MdlFile(mdlData);
        //var materialBuilders = new List<MaterialBuilder>();
        var materialBuilders = new MaterialBuilder[modelInfo.Materials.Length];
        for (int i = 0; i < modelInfo.Materials.Length; i++)
        {
            var materialInfo = modelInfo.Materials[i];
            try
            {
                progress?.Invoke(new ProgressEvent(modelInfo.GetHashCode(), $"{materialInfo.Path.GamePath}", i + 1, modelInfo.Materials.Length));
                var mtrlData = dataProvider.LookupData(materialInfo.Path.FullPath);
                if (mtrlData == null)
                {
                    Plugin.Logger?.LogWarning("Failed to load material file: {mtrlPath}", materialInfo.Path.FullPath);
                    throw new Exception($"Failed to load material file {materialInfo.Path.FullPath} returned null");
                }

                Plugin.Logger?.LogInformation("Loaded material {mtrlPath}", materialInfo.Path.FullPath);
                var mtrlFile = new MtrlFile(mtrlData);
                var shpkName = mtrlFile.GetShaderPackageName();
                var shpkPath = $"shader/sm5/shpk/{shpkName}";
                var shpkData = dataProvider.LookupData(shpkPath);
                if (shpkData == null) throw new Exception($"Failed to load shader package file {shpkPath} returned null");
                var shpkFile = new ShpkFile(shpkData);
                var material = new MaterialSet(mtrlFile, materialInfo.Path.GamePath,
                                               shpkFile,
                                               shpkName,
                                               materialInfo.Textures
                                                           .Select(x => x.Path)
                                                           .ToArray(),
                                               handleString =>
                                               {
                                                   var match = materialInfo.Textures.FirstOrDefault(x =>
                                                           x.Path.GamePath == handleString.GamePath &&
                                                           x.Path.FullPath == handleString.FullPath);
                                                   return match?.Resource;
                                               });
                if (materialInfo.ColorTable != null)
                {
                    material.SetColorTable(materialInfo.ColorTable);
                }

                material.SetCustomizeParameters(customizeParameter);
                material.SetCustomizeData(customizeData);
                material.SetTextureMode(textureMode);

                materialBuilders[i] = material.Compose(dataProvider);
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError(e, "Failed to load material\n{Message}\n{MaterialInfo}", e.Message, JsonSerializer.Serialize(modelInfo.Materials[i], jsonOptions));
                materialBuilders[i] = new MaterialBuilder("error");
            }
        }

        var model = new Model(modelInfo.Path.GamePath, mdlFile, modelInfo.ShapeAttributeGroup);
        EnsureBonesExist(model, bones, rootBone);
        (GenderRace from, GenderRace to, RaceDeformer deformer)? deform;
        if (modelInfo.Deformer != null)
        {
            // custom pbd may exist
            var pbdFileData = dataProvider.LookupData(modelInfo.Deformer.Value.PbdPath);
            if (pbdFileData == null)
            {
                throw new InvalidOperationException($"Failed to get deformer pbd {modelInfo.Deformer.Value.PbdPath} returned null");
            }
            
            deform = ((GenderRace)modelInfo.Deformer.Value.DeformerId, (GenderRace)modelInfo.Deformer.Value.RaceSexId, new RaceDeformer(new PbdFile(pbdFileData), bones));
            Plugin.Logger?.LogDebug("Using deformer pbd {Path}", modelInfo.Deformer.Value.PbdPath);
        }
        else
        {
            var parsed = RaceDeformer.ParseRaceCode(modelInfo.Path.GamePath);
            if (Enum.IsDefined(parsed))
            {
                deform = (parsed, genderRace, new RaceDeformer(DefaultPbdFile!, bones));
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
                instance = scene.AddSkinnedMesh(mesh.Mesh, transform, bones.Cast<NodeBuilder>().ToArray());
            }
            else
            {
                instance = scene.AddRigidMesh(mesh.Mesh, transform);
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
    }
    
    private int attachSuffix;
    private readonly object attachLock = new();

    private bool HandleAttach((ParsedCharacterInfo Owner, List<BoneNodeBuilder> OwnerBones, ParsedAttach Attach) attachData, SceneBuilder scene, BoneNodeBuilder rootBone, ref Matrix4x4 transform)
    {
        bool rootParented;
        var attach = attachData.Attach;
        if (rootBone == null) throw new InvalidOperationException("Root bone not found");
        var attachName = attachData.Owner.Skeleton.PartialSkeletons[attach.PartialSkeletonIdx]
                                   .HkSkeleton!.BoneNames[(int)attach.BoneIdx];
        Plugin.Logger?.LogInformation("Attaching {AttachName} to {RootBone}", attachName, rootBone.BoneName);
        lock (attachLock)
        {
            Interlocked.Increment(ref attachSuffix);
            rootBone.SetSuffixRecursively(attachSuffix);
        }

        if (attach.OffsetTransform is { } ct)
        {
            rootBone.WithLocalScale(ct.Scale);
            rootBone.WithLocalRotation(ct.Rotation);
            rootBone.WithLocalTranslation(ct.Translation);
            if (rootBone.AnimationTracksNames.Contains("pose"))
            {
                rootBone.UseScale().UseTrackBuilder("pose").WithPoint(0, ct.Scale);
                rootBone.UseRotation().UseTrackBuilder("pose").WithPoint(0, ct.Rotation);
                rootBone.UseTranslation().UseTrackBuilder("pose").WithPoint(0, ct.Translation);
            }
        }

        var attachPointBone = attachData.OwnerBones.FirstOrDefault(
                x => x.BoneName.Equals(attachName, StringComparison.Ordinal));
        if (attachPointBone == null)
        {
            scene.AddNode(rootBone);
            rootParented = true;
        }
        else
        {
            attachPointBone.AddNode(rootBone);
            rootParented = true;
        }

        NodeBuilder? c = rootBone;
        while (c != null)
        {
            transform *= c.LocalMatrix;
            c = c.Parent;
        }
        
        return rootParented;
    }

    private bool HandleRootAttach(ParsedCharacterInfo characterInfo, SceneBuilder scene, NodeBuilder root, ref BoneNodeBuilder rootBone, ref Matrix4x4 transform)
    {
        bool rootParented = false;
        var rootAttach = characterInfo.Attaches.FirstOrDefault(x => x.Attach.ExecuteType == 0);
        if (rootAttach == null)
        {
            Plugin.Logger?.LogWarning("Root attach not found");
        }
        else
        {
            Plugin.Logger?.LogWarning("Root attach found");
            // handle root first, then attach this to the root
            var rootAttachData = ComposeCharacterInfo(rootAttach, null, scene, root);
            if (rootAttachData != null)
            {
                var attachName = rootAttach.Skeleton.PartialSkeletons[characterInfo.Attach.PartialSkeletonIdx]
                                           .HkSkeleton!
                                           .BoneNames[(int)characterInfo.Attach.BoneIdx];
                if (rootBone == null) throw new InvalidOperationException("Root bone not found");
                var attachRoot = rootAttachData.Value.root;
                lock (attachLock)
                {
                    Interlocked.Increment(ref attachSuffix);
                    attachRoot.SetSuffixRecursively(attachSuffix);
                }

                if (rootAttach.Attach.OffsetTransform is { } ct)
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
                    rootAttachData.Value.bones.FirstOrDefault(
                        x => x.BoneName.Equals(attachName, StringComparison.Ordinal));
                if (attachPointBone == null)
                {
                    scene.AddNode(rootBone);
                    rootParented = true;
                }
                else
                {
                    attachPointBone.AddNode(rootBone);
                    rootParented = true;
                }

                NodeBuilder? c = rootBone;
                while (c != null)
                {
                    transform *= c.LocalMatrix;
                    c = c.Parent;
                }

                rootBone = attachRoot;
            }
        }
        
        return rootParented;
    }
    
    private static JsonSerializerOptions jsonOptions = new()
    {
        IncludeFields = true
    };
    
    public (List<BoneNodeBuilder> bones, BoneNodeBuilder root)? ComposeCharacterInfo(ParsedCharacterInfo characterInfo, (ParsedCharacterInfo Owner, List<BoneNodeBuilder> OwnerBones, ParsedAttach Attach)? attachData, SceneBuilder scene, NodeBuilder root)
    {
        List<BoneNodeBuilder> bones;
        BoneNodeBuilder? rootBone;
        try
        {
            bones = SkeletonUtils.GetBoneMap(characterInfo.Skeleton, includePose ? poseMode : null, out rootBone);
            if (rootBone == null)
            {
                Plugin.Logger?.LogWarning("Root bone not found");
                return null;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger?.LogError(e, "Failed to get bone map");
            return null;
        }

        bool rootParented = false;
        Matrix4x4 transform = Matrix4x4.Identity;
        if (attachData != null)
        {
            try
            {
                if (HandleAttach(attachData.Value, scene, rootBone, ref transform))
                {
                    rootParented = true;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError(e, "Failed to handle attach {AttachData}", JsonSerializer.Serialize(new
                {
                    AttachData = attachData.Value.Attach,
                    Owner = attachData.Value.Owner
                }, jsonOptions));
            }
        }
        else if (characterInfo.Attach.ExecuteType != 0)
        {
            try
            {
                if (HandleRootAttach(characterInfo, scene, root, ref rootBone, ref transform))
                {
                    rootParented = true;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError(e, "Failed to handle root attach {CharacterInfo}", JsonSerializer.Serialize(characterInfo, jsonOptions));
            }
        }

        if (!rootParented)
        {
            root.AddNode(rootBone);
        }

        for (var i = 0; i < characterInfo.Models.Length; i++)
        {
            var t = characterInfo.Models[i];
            progress?.Invoke(new ProgressEvent(characterInfo.GetHashCode(), $"{t.Path.GamePath}", i + 1, characterInfo.Models.Length));
            try
            {
                HandleModel(characterInfo.GenderRace, characterInfo.CustomizeParameter, characterInfo.CustomizeData,
                            t, scene, bones, rootBone, transform);
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError(e, "Failed to handle model\n{Message}\n{ModelInfo}", e.Message, JsonSerializer.Serialize(
                                            t, new JsonSerializerOptions
                                            {
                                                IncludeFields = true
                                            }));
            }
        }

        for (var i = 0; i < characterInfo.Attaches.Length; i++)
        {
            try
            {
                var attach = characterInfo.Attaches[i];
                if (attach.Attach.ExecuteType == 0) continue;
                ComposeCharacterInfo(attach, (characterInfo, bones, attach.Attach), scene, root);
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError(e, "Failed to handle attach {Attach}", JsonSerializer.Serialize(characterInfo.Attaches[i], jsonOptions));
            }
        }

        return (bones, rootBone);
    }

    public void ComposeModels(ParsedModelInfo[] models, GenderRace genderRace, CustomizeParameter customizeParameter, 
                              CustomizeData customizeData, ParsedSkeleton skeleton, SceneBuilder scene, NodeBuilder root)
    { 
        List<BoneNodeBuilder> bones;
        BoneNodeBuilder? rootBone;
        try
        {
            bones = SkeletonUtils.GetBoneMap(skeleton, includePose ? poseMode : null, out rootBone);
            if (rootBone == null)
            {
                Plugin.Logger?.LogWarning("Root bone not found");
                return;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger?.LogError(e, "Failed to get bone map");
            return;
        }

        foreach (var t in models)
        {
            try
            {
                HandleModel(genderRace, customizeParameter, customizeData, t, scene, bones, rootBone,
                            Matrix4x4.Identity);
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogError(e, "Failed to handle model\n{Message}\n{ModelInfo}", e.Message, JsonSerializer.Serialize(t, jsonOptions));
            }
        }
    }
    
    public void ComposeCharacterInstance(ParsedCharacterInstance characterInstance, SceneBuilder scene, NodeBuilder root)
    {
        var characterInfo = characterInstance.CharacterInfo;
        if (characterInfo == null) return;
        ComposeCharacterInfo(characterInfo, null, scene, root);
    }
    
    private void EnsureBonesExist(Model model, List<BoneNodeBuilder> bones, BoneNodeBuilder? root)
    {
        foreach (var mesh in model.Meshes)
        {
            if (mesh.BoneTable == null) continue;
    
            foreach (var boneName in mesh.BoneTable)
            {
                if (bones.All(b => !b.BoneName.Equals(boneName, StringComparison.Ordinal)))
                {
                    if (root == null)
                    {
                        throw new InvalidOperationException($"Root bone not found when generating missing bone {boneName} for {model.Path}");
                    }
                    
                    var bone = new BoneNodeBuilder(boneName)
                    {
                        IsGenerated = true
                    };
                    
                    var parent = FindLogicalParent(model, bone, bones) ?? root;
                    parent.AddNode(bone);
                    
                    Plugin.Logger?.LogWarning("Added bone {BoneName} to {ParentBone}\n" +
                                              "NOTE: This may break posing in some cases, you may find better results " +
                                              "deleting the bone after importing into editing software", boneName, parent.BoneName);
                    bones.Add(bone);
                }
            }
        }
    }

    private BoneNodeBuilder? FindLogicalParent(Model model, BoneNodeBuilder node, List<BoneNodeBuilder> bones)
    {
        // for each vertex in meshes which contain the bone
        // check if the bone is included in the blend indices for the vertex
        // if it is, check other bones weighted to the same vertex to find a parent bone
        foreach (var mesh in model.Meshes)
        {
            if (mesh.BoneTable != null && mesh.BoneTable.Contains(node.BoneName))
            {
                foreach (var vertex in mesh.Vertices)
                {
                    if (vertex.BlendIndices == null) continue;
                    
                    var vertexBones = vertex.BlendIndices.Select(x => mesh.BoneTable[x]).ToArray();
                    if (vertexBones.Contains(node.BoneName))
                    {
                        var parentBone = vertexBones.FirstOrDefault(x => bones.Any(b => b.BoneName.Equals(x, StringComparison.Ordinal)));
                        if (parentBone != null)
                        {
                            return bones.FirstOrDefault(x => x.BoneName.Equals(parentBone, StringComparison.Ordinal));
                        }
                    }
                }
            }
        }
        
        return null;
    }
}
