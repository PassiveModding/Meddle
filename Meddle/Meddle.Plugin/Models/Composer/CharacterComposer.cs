using System.Numerics;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Models.Composer;

public class CharacterComposer
{
    private readonly ILogger log;
    private readonly DataProvider dataProvider;
    private static TexFile? CubeMapTex;
    private static PbdFile? PbdFile;
    private static readonly object StaticFileLock = new();
    
    public CharacterComposer(ILogger log, DataProvider dataProvider)
    {
        this.log = log;
        this.dataProvider = dataProvider;

        lock (StaticFileLock)
        {
            if (CubeMapTex == null)
            {
                var catchlight = this.dataProvider.LookupData("chara/common/texture/sphere_d_array.tex");
                if (catchlight == null) throw new Exception("Failed to load catchlight texture");
                CubeMapTex = new TexFile(catchlight);
            }

            if (PbdFile == null)
            {
                var pbdData = this.dataProvider.LookupData("chara/xls/boneDeformer/human.pbd");
                if (pbdData == null) throw new Exception("Failed to load human.pbd");
                PbdFile = new PbdFile(pbdData);
            }
        }
    }
    
    public void ComposeCharacterInstance(ParsedCharacterInstance characterInstance, SceneBuilder scene, NodeBuilder root)
    {
        var characterInfo = characterInstance.CharacterInfo;
        if (characterInfo == null) return;

        var bones = SkeletonUtils.GetBoneMap(characterInfo.Skeleton, true, out var rootBone);
        if (rootBone != null)
        {
            root.AddNode(rootBone);
        }

        var textureMappings = characterInstance.TextureMap;

        for (var i = 0; i < characterInfo.Models.Count; i++)
        {
            var modelInfo = characterInfo.Models[i];
            if (modelInfo.PathFromCharacter.Contains("b0003_top")) continue;
            var mdlData = dataProvider.LookupData(modelInfo.Path);
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
                var mtrlData = dataProvider.LookupData(materialInfo.Path);
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
                var shpkData = dataProvider.LookupData(shpkPath);
                if (shpkData == null) throw new Exception($"Failed to load shader package file: {shpkPath}");
                var shpkFile = new ShpkFile(shpkData);
                var material = new MaterialSet(mtrlFile, materialInfo.Path, shpkFile, shpkName);
                material.SetCustomizeParameters(characterInstance.CustomizeParameter);
                material.SetCustomizeData(characterInstance.CustomizeData);
                material.SetTexturePathMappings(characterInstance.TextureMap);
                materialBuilders.Add(material.Compose(dataProvider));
            }

            var model = new Model(modelInfo.Path, mdlFile, modelInfo.ShapeAttributeGroup);
            EnsureBonesExist(model, bones, rootBone);
            (GenderRace from, GenderRace to, RaceDeformer deformer)? deform;
            if (modelInfo.Deformer != null)
            {
                // custom pbd may exist
                var pbdFileData = dataProvider.LookupData(modelInfo.Deformer.Value.PbdPath);
                if (pbdFileData == null) throw new InvalidOperationException($"Failed to get deformer pbd {modelInfo.Deformer.Value.PbdPath}");
                deform = ((GenderRace)modelInfo.Deformer.Value.DeformerId, (GenderRace)modelInfo.Deformer.Value.RaceSexId, new RaceDeformer(new PbdFile(pbdFileData), bones));
                log.LogDebug("Using deformer pbd {Path}", modelInfo.Deformer.Value.PbdPath);
            }
            else
            {
                var parsed = RaceDeformer.ParseRaceCode(modelInfo.PathFromCharacter);
                if (Enum.IsDefined(parsed))
                {
                    deform = (parsed, characterInfo.GenderRace, new RaceDeformer(PbdFile!, bones));
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
        }
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
}
