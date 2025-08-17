using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Models.Composer;
public class CharacterComposer
{
    public record SkinningContext(List<BoneNodeBuilder> Bones, BoneNodeBuilder? RootBone, Matrix4x4 Transform);
    
    private readonly ComposerCache composerCache;
    private readonly Configuration.ExportConfiguration exportConfig;
    private readonly CancellationToken cancellationToken;

    public CharacterComposer(ComposerCache composerCache, Configuration.ExportConfiguration exportConfig, CancellationToken cancellationToken)
    {
        this.composerCache = composerCache;
        this.exportConfig = exportConfig;
        this.cancellationToken = cancellationToken;
    }
    
    public CharacterComposer(SqPack pack, Configuration.ExportConfiguration exportConfig, string outDir, CancellationToken cancellationToken)
    {
        this.exportConfig = exportConfig;
        this.cancellationToken = cancellationToken;
        Directory.CreateDirectory(outDir);
        var cacheDir = Path.Combine(outDir, "cache");
        Directory.CreateDirectory(cacheDir);
        composerCache = new ComposerCache(pack, cacheDir, exportConfig);
    }
    
    private void HandleModel(ParsedCharacterInfo characterInfo, ParsedModelInfo m, SceneBuilder scene, SkinningContext skinningContext)
    {
        if (m.Path.GamePath.Contains("b0003_top"))
        {
            Plugin.Logger.LogDebug("Skipping model {ModelPath}", m.Path.GamePath);
            return;
        }
        
        var mdlFile = composerCache.GetMdlFile(m.Path.FullPath);
        Plugin.Logger.LogInformation("Loaded model {modelPath}", m.Path.FullPath);
        var materialBuilders = new MaterialBuilder[m.Materials.Length];
        for (int i = 0; i < m.Materials.Length; i++)
        {
            var materialInfo = m.Materials[i];
            if (materialInfo == null)
            {
                materialBuilders[i] = new MaterialBuilder("null");
                continue;
            }
            
            try
            {
                materialBuilders[i] = composerCache.ComposeMaterial(materialInfo.Path.FullPath, 
                                                                    materialInfo: materialInfo, 
                                                                    characterInfo: characterInfo);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e, "Failed to load material\n{Message}\n{MaterialInfo}", e.Message, 
                                        JsonSerializer.Serialize(m.Materials[i], 
                                            MaterialComposer.JsonOptions));
                materialBuilders[i] = new MaterialBuilder("error");
            }
        }

        var model = new Model(m.Path.GamePath, mdlFile, m.ShapeAttributeGroup);
        EnsureBonesExist(model, skinningContext.Bones, skinningContext.RootBone);
        (GenderRace from, GenderRace to, RaceDeformer deformer)? deform = null;
        if (exportConfig.UseDeformer)
        {
            if (m.Deformer != null)
            {
                // custom pbd may exist
                var pbdFile = composerCache.GetPbdFile(m.Deformer.Value.PbdPath);
                if (pbdFile == null)
                {
                    throw new InvalidOperationException($"Failed to get deformer pbd {m.Deformer.Value.PbdPath} returned null");
                }

                deform = ((GenderRace)m.Deformer.Value.DeformerId,
                             (GenderRace)m.Deformer.Value.RaceSexId,
                             new RaceDeformer(pbdFile, skinningContext.Bones));
                Plugin.Logger.LogDebug("Using deformer pbd {Path}", m.Deformer.Value.PbdPath);
            }
            else
            {
                var parsed = RaceDeformer.ParseRaceCode(m.Path.GamePath);
                if (Enum.IsDefined(parsed))
                {
                    deform = (parsed, characterInfo.GenderRace, new RaceDeformer(composerCache.GetDefaultPbdFile(), skinningContext.Bones));
                }
                else
                {
                    deform = null;
                }
            }
        }

        var enabledAttributes = Model.GetEnabledValues(model.EnabledAttributeMask, model.AttributeMasks).ToArray();
        var meshes = ModelBuilder.BuildMeshes(model, materialBuilders, skinningContext.Bones, deform);
        foreach (var mesh in meshes)
        {
            var extrasDict = new Dictionary<string, string>
            {
                {"modelGamePath", m.Path.GamePath},
                {"modelFullPath", m.Path.FullPath},
                {"meshShapes", mesh.Shapes != null ? string.Join(",", mesh.Shapes) : ""},
                {"modelEnabledAttributes", string.Join(",", enabledAttributes)},
                {"modelAttributes", string.Join(",", model.AttributeMasks.Select(x => x.name))},
                {"nodeType", "CharacterMesh"}
            };
            
            if (deform != null)
            {
                extrasDict.Add("modelDeformFromName", deform.Value.from.ToString());
                extrasDict.Add("modelDeformToName", deform.Value.to.ToString());
                extrasDict.Add("modelDeformFromId", ((int)deform.Value.from).ToString());
                extrasDict.Add("modelDeformToId", ((int)deform.Value.to).ToString());
            }
            else
            {
                extrasDict.Add("modelDeformFromName", "");
                extrasDict.Add("modelDeformToName", "");
                extrasDict.Add("modelDeformFromId", "");
                extrasDict.Add("modelDeformToId", "");
            }

            // for shape keys to work, mesh.Mesh.Extras already has an object with a field 'targetNames' which names all the shape keys. need to preserve that.
            var extras = mesh.Mesh.Extras?.AsObject();
            if (extras != null)
            {
                foreach (var kvp in extrasDict)
                {
                    if (!extras.ContainsKey(kvp.Key))
                    {
                        extras.Add(kvp.Key, kvp.Value);
                    }
                }
                
                mesh.Mesh.Extras = extras;
            }
            else
            {
                mesh.Mesh.Extras = JsonNode.Parse(JsonSerializer.Serialize(extrasDict, MaterialComposer.JsonOptions));
            }
            
            InstanceBuilder instance;
            if (skinningContext.Bones.Count > 0)
            {
                instance = scene.AddSkinnedMesh(mesh.Mesh, skinningContext.Transform, skinningContext.Bones.Cast<NodeBuilder>().ToArray());
            }
            else
            {
                instance = scene.AddRigidMesh(mesh.Mesh, skinningContext.Transform);
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
                if (!mesh.Submesh.Attributes.All(enabledAttributes.Contains) && exportConfig.RemoveAttributeDisabledSubmeshes)
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
        Plugin.Logger.LogInformation("Attaching {AttachName} to {RootBone}", attachName, rootBone.BoneName);
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

    private bool HandleRootAttach(
        ParsedCharacterInfo characterInfo,
        SceneBuilder scene,
        NodeBuilder root,
        ref BoneNodeBuilder rootBone,
        ref Matrix4x4 transform,
        ExportProgress rootProgress)
    {
        bool rootParented = false;
        var rootAttach = characterInfo.Attaches.FirstOrDefault(x => x.Attach.ExecuteType == 0);
        if (rootAttach == null)
        {
            Plugin.Logger.LogWarning("Root attach not found");
        }
        else
        {
            Plugin.Logger.LogWarning("Root attach found");
            // handle root first, then attach this to the root
            var rootAttachProgress = new ExportProgress(rootAttach.Models.Length, "Root attach");
            rootProgress.Children.Add(rootAttachProgress);
            try
            {
                var rootAttachData = ComposeCharacterInfo(rootAttach, null, scene, root, rootAttachProgress);
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
            finally
            {
                rootAttachProgress.IsComplete = true;
            }
        }
        
        return rootParented;
    }
    
    public (List<BoneNodeBuilder> bones, BoneNodeBuilder root)? Compose(ParsedCharacterInfo characterInfo, SceneBuilder scene, NodeBuilder root, ExportProgress progress)
    {
        composerCache.SaveArrayTextures();
        root.Extras = JsonNode.Parse(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            {"raceCode", ((int)characterInfo.GenderRace).ToString()},
            {"raceCodeName", characterInfo.GenderRace.ToString() },
            { "nodeType", "CharacterRoot" }
        }));
        return ComposeCharacterInfo(characterInfo, null, scene, root, progress);
    }
    
    private (List<BoneNodeBuilder> bones, BoneNodeBuilder root)? ComposeCharacterInfo(ParsedCharacterInfo characterInfo, (ParsedCharacterInfo Owner, List<BoneNodeBuilder> OwnerBones, ParsedAttach Attach)? attachData, SceneBuilder scene, NodeBuilder root, ExportProgress rootProgress)
    {
        List<BoneNodeBuilder> bones;
        BoneNodeBuilder? rootBone;
        try
        {
            bones = SkeletonUtils.GetBoneMap(characterInfo.Skeleton, exportConfig.PoseMode, out rootBone);
            if (rootBone == null)
            {
                Plugin.Logger.LogWarning("Root bone not found");
                return null;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e, "Failed to get bone map");
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
                Plugin.Logger.LogError(e, "Failed to handle attach {AttachData}", JsonSerializer.Serialize(new
                {
                    AttachData = attachData.Value.Attach,
                    Owner = attachData.Value.Owner
                }, MaterialComposer.JsonOptions));
            }
        }
        else if (characterInfo.Attach.ExecuteType != 0)
        {
            try
            {
                if (HandleRootAttach(characterInfo, scene, root, ref rootBone, ref transform, rootProgress))
                {
                    rootParented = true;
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e, "Failed to handle root attach {CharacterInfo}", JsonSerializer.Serialize(characterInfo, MaterialComposer.JsonOptions));
            }
        }

        if (!rootParented)
        {
            root.AddNode(rootBone);
        }
        
        foreach (var t in characterInfo.Models)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Plugin.Logger.LogInformation("Export cancelled, stopping model processing");
                break;
            }
            
            try
            {
                HandleModel(characterInfo, t, 
                            scene,
                            new SkinningContext(bones, rootBone, transform));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e, "Failed to handle model\n{Message}\n{ModelInfo}", e.Message, JsonSerializer.Serialize(t, MaterialComposer.JsonOptions));
            }
            
            rootProgress.IncrementProgress();
        }
        
        foreach (var t in characterInfo.Attaches)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Plugin.Logger.LogInformation("Export cancelled, stopping attach processing");
                break;
            }
            
            ExportProgress? attachProgress = null;
            try
            {
                if (t.Attach.ExecuteType == 0) continue;
                attachProgress = new ExportProgress(t.Models.Length, "Attach Meshes");
                rootProgress.Children.Add(attachProgress);
                ComposeCharacterInfo(t, (characterInfo, bones, t.Attach), scene, root, attachProgress);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e, "Failed to handle attach {Attach}", JsonSerializer.Serialize(t, MaterialComposer.JsonOptions));
            }
            finally
            {
                if (attachProgress != null)
                {
                    attachProgress.IsComplete = true;
                }
            }
        }

        return (bones, rootBone);
    }
    
    public static void EnsureBonesExist(Model model, List<BoneNodeBuilder> bones, BoneNodeBuilder? root)
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
                        throw new InvalidOperationException($"Root bone not found when generating missing bone {boneName} for {model.HandlePath}");
                    }
                    
                    var bone = new BoneNodeBuilder(boneName)
                    {
                        IsGenerated = true
                    };
                    
                    var parent = FindLogicalParent(model, bone, bones) ?? root;
                    parent.AddNode(bone);
                    
                    Plugin.Logger.LogWarning("Added bone {BoneName} to {ParentBone}\n" +
                                              "NOTE: This may break posing in some cases, you may find better results " +
                                              "deleting the bone after importing into editing software", boneName, parent.BoneName);
                    bones.Add(bone);
                }
            }
        }
    }

    /// <summary>
    /// Find a logical parent bone for a bone in a model
    /// </summary>
    /// <returns></returns>
    public static BoneNodeBuilder? FindLogicalParent(Model model, BoneNodeBuilder node, List<BoneNodeBuilder> bones)
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
