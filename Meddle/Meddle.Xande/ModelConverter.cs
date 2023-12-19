using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using Meddle.Xande.Utility;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.RegularExpressions;
using Xande;
using Xande.Enums;
using Xande.Files;
using Xande.Havok;
using Xande.Models.Export;
using static Meddle.Xande.Utility.TextureUtility;
using static SharpGLTF.Schema2.Toolkit;

namespace Meddle.Xande;

public class ModelConverter
{
    private class ModelConverterLogger
    {
        public readonly IPluginLog PluginLog;
        public string LastMessage = "";

        public ModelConverterLogger(IPluginLog log)
        {
            PluginLog = log;
        }

        public void Debug(string message)
        {
            PluginLog.Debug(message);
        }

        public void Info(string message)
        {
            PluginLog.Info(message);
            LastMessage = message;
        }

        public void Warning(string message)
        {
            PluginLog.Warning(message);
            LastMessage = message;
        }

        public void Error(Exception e, string message)
        {
            PluginLog.Error(e, message);
            LastMessage = message;
        }
    }

    private ModelConverterLogger Log { get; }

    public string GetLastMessage()
    {
        return Log.LastMessage;
    }

    private HavokConverter Converter { get; }
    private LuminaManager LuminaManager { get; }
    private IFramework Framework { get; }
    private PbdFile Pbd { get; }

    public ModelConverter(HavokConverter converter, LuminaManager luminaManager, IPluginLog log,
        IFramework framework)
    {
        Converter = converter;
        LuminaManager = luminaManager;
        Log = new ModelConverterLogger(log);
        Framework = framework;
        Pbd = LuminaManager.GetPbdFile();
    }

    public Task ExportResourceTree(NewTree character, bool openFolderWhenComplete,
        ExportType exportType,
        string exportPath,
        bool copyNormalAlphaToDiffuse,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(exportPath, $"{character.GetHashCode():X8}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
        Directory.CreateDirectory(path);

        Log.Debug($"Exporting character to {path}");

        return Task.Run(async () =>
        {
            try
            {
                await ExportResourceTreeEx(path, character, exportType, copyNormalAlphaToDiffuse,
                    cancellationToken);
                // open path
                if (openFolderWhenComplete)
                    Process.Start("explorer.exe", path);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while exporting character");
            }
        }, cancellationToken);
    }

    public async Task ExportResourceTreeEx(
        string exportPath,
        NewTree tree,
        ExportType exportType,
        bool copyNormalAlphaToDiffuse,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var glTfScene = new SceneBuilder(string.IsNullOrWhiteSpace(tree.Name) ? "scene" : tree.Name);
            var modelTasks = new List<Task>();

            NodeBuilder? rootBone = null;
            List<NodeBuilder>? boneMap = null;

            if (tree.Skeleton.PartialSkeletons.Count > 0)
            {
                boneMap = ModelUtility.GetBoneMap(tree.Skeleton, -1, out rootBone);
                var raceDeformer = new RaceDeformer(Pbd, boneMap);
                if (rootBone != null)
                    glTfScene.AddNode(rootBone);

                foreach (var model in tree.Models)
                {
                    var t = HandleModel(model, raceDeformer, tree.RaceCode, exportPath, boneMap, glTfScene,
                        copyNormalAlphaToDiffuse, Matrix4x4.Identity,
                        cancellationToken);
                    t.Wait(cancellationToken);
                    modelTasks.Add(t);
                }
            }
            else
            {
                foreach (var node in tree.Models)
                {
                    var t = Task.Run(async () =>
                    {
                        var materials = await GetMaterialsFromModelNode(node, exportPath, copyNormalAlphaToDiffuse, cancellationToken);

                        // log all materials 
                        Log.Debug($"Handling model {node.HandlePath} with {node.Meshes.Count} meshes");
                        var name = Path.GetFileNameWithoutExtension(node.HandlePath);

                        foreach (var mesh in node.Meshes)
                        {
                            var material = materials[mesh.MaterialIdx];
                            if (material == null)
                                continue;

                            try
                            {
                                var meshbuilder = new MeshBuilder(mesh,
                                    false,
                                    new Dictionary<int, int>(),
                                    material,
                                    new RaceDeformer(Pbd, new()));

                                meshbuilder.BuildVertices();

                                BuildSubmeshes(meshbuilder, name, mesh, node, glTfScene, Matrix4x4.Identity, null, false);
                            }
                            catch (Exception e)
                            {
                                Log.Error(e,
                                    $"Failed to handle mesh creation for material {mesh.MaterialIdx}");
                            }
                        }
                    }, cancellationToken);
                    t.Wait(cancellationToken);
                    modelTasks.Add(t);
                }
            }

            if (tree.AttachedChildren != null)
            {
                var i = 0;
                foreach (var child in tree.AttachedChildren)
                {
                    var childBoneMap = ModelUtility.GetBoneMap(child.Skeleton, i++, out var root);
                    var attachName = tree.Skeleton.PartialSkeletons[child.Attach.PartialSkeletonIdx].HkSkeleton!.BoneNames[child.Attach.BoneIdx];

                    if (rootBone == null || boneMap == null)
                        glTfScene.AddNode(root);
                    else
                        boneMap.First(b => b.Name.Equals(attachName, StringComparison.Ordinal)).AddNode(root);

                    var transform = Matrix4x4.Identity;
                    var c = root;
                    while (c != null)
                    {
                        transform *= c.LocalMatrix;
                        c = c.Parent;
                    }

                    foreach (var model in tree.Models)
                    {
                        Log.Debug($"Handling child model {model.HandlePath}");

                        var t = HandleModel(model, null, tree.RaceCode, exportPath, childBoneMap, glTfScene,
                            copyNormalAlphaToDiffuse, transform,
                            cancellationToken);
                        t.Wait(cancellationToken);
                        modelTasks.Add(t);
                    }
                }
            }

            await Task.WhenAll(modelTasks);

            var glTfModel = glTfScene.ToGltf2();

            // check if exportType contains each type using flags
            if (exportType.HasFlag(ExportType.Glb))
                glTfModel.SaveGLB(Path.Combine(exportPath, "glb.glb"));

            if (exportType.HasFlag(ExportType.Gltf))
            {
                var glTfFolder = Path.Combine(exportPath, "gltf");
                Directory.CreateDirectory(glTfFolder);
                glTfModel.SaveGLTF(Path.Combine(glTfFolder, "gltf.gltf"));
            }
            
            if (exportType.HasFlag(ExportType.Wavefront))
            {
                var waveFrontFolder = Path.Combine(exportPath, "wavefront");
                Directory.CreateDirectory(waveFrontFolder);
                glTfModel.SaveAsWavefront(Path.Combine(waveFrontFolder, "wavefront.obj"));
            }

            Log.Debug($"Exported model to {exportPath}");
            Log.Info($"Exported model");
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to export model");
        }
    }

    private async Task HandleModel(NewModel model, RaceDeformer? raceDeformer, ushort? deform, string exportPath,
        List<NodeBuilder> joints,
        SceneBuilder glTfScene, bool copyNormalAlphaToDiffuse, Matrix4x4 worldLocation, CancellationToken cancellationToken)
    {
        Log.Info($"Handling model {model.HandlePath}");

        var materials = await GetMaterialsFromModelNode(model, exportPath, copyNormalAlphaToDiffuse, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
            return;

        Log.Debug($"Handling model {model.HandlePath} with {model.Meshes.Count} meshes");

        var name = Path.GetFileNameWithoutExtension(model.HandlePath);

        foreach (var mesh in model.Meshes)
        {
            var material = materials[mesh.MaterialIdx];
            if (material == null)
                continue;

            try
            {
                HandleMeshCreation(material, raceDeformer, glTfScene, mesh, model, model.RaceCode, deform,
                    name, joints, worldLocation);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Failed to handle mesh creation for material {mesh.MaterialIdx}");
            }
        }
    }

    private async Task<List<MaterialBuilder?>> GetMaterialsFromModelNode(NewModel node, string exportPath, bool copyNormalAlphaToDiffuse, CancellationToken cancellationToken)
    {
        var materials = new MaterialBuilder?[node.Materials.Count];
        var textureTasks = new List<Task>();

        for (var i = 0; i < node.Materials.Count; ++i)
        {
            var idx = i;
            var t = Task.Run(() =>
            {
                var material = node.Materials[idx];
                try
                {
                    var glTfMaterial = ComposeTextures(material, exportPath, copyNormalAlphaToDiffuse, cancellationToken);

                    if (glTfMaterial == null)
                        return;

                    materials[idx] = glTfMaterial;
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Failed to compose textures for material {material.HandlePath}");
                }
            }, cancellationToken);
            t.Wait(cancellationToken);
            textureTasks.Add(t);
        }

        await Task.WhenAll(textureTasks);

        return materials.ToList();
    }

    public static IEnumerable<NodeBuilder> Flatten(NodeBuilder container)
    {
        if (container == null) yield break;

        yield return container;

        foreach (var c in container.VisualChildren)
        {
            var cc = Flatten(c);

            foreach (var ccc in cc) yield return ccc;
        }
    }

    private void BuildSubmeshes(MeshBuilder meshBuilder, string name, NewMesh xivMesh, NewModel xivModel, SceneBuilder glTfScene, Matrix4x4 worldLocation, List<NodeBuilder>? joints, bool useSkinning)
    {
        for (var i = 0; i < xivMesh.Submeshes.Count; i++)
        {
            try
            {
                var xivSubmesh = xivMesh.Submeshes[i];
                var subMesh = meshBuilder.BuildSubmesh(xivSubmesh);
                subMesh.Name = $"{name}_{xivMesh.MeshIdx}.{i}";
                if (xivSubmesh.Attributes != null && xivSubmesh.Attributes.Count > 0)
                    subMesh.Name = $"{subMesh.Name};{string.Join(";", xivSubmesh.Attributes)}";
                meshBuilder.BuildShapes(xivModel.Shapes, subMesh, (int)xivSubmesh.IndexOffset,
                    (int)(xivSubmesh.IndexOffset + xivSubmesh.IndexCount));
                InstanceBuilder? instance;
                if (joints != null)
                {
                    if (!NodeBuilder.IsValidArmature(joints))
                    {
                        Log.Warning(
                            $"Joints are not valid, skipping submesh {i} for {name}, {string.Join(", ", joints.Select(x => x.Name))}");
                        continue;
                    }
                    instance =
                        useSkinning ?
                            glTfScene.AddSkinnedMesh(subMesh, worldLocation, joints.ToArray()) :
                            glTfScene.AddRigidMesh(subMesh, worldLocation);
                }
                else
                    instance = glTfScene.AddRigidMesh(subMesh, worldLocation);
                ApplyMeshModifiers(instance, xivSubmesh, xivModel);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Failed to build submesh {i} for {name}");
            }
        }
    }
        
    private void ApplyMeshModifiers(InstanceBuilder builder, NewSubMesh subMesh, NewModel model)
    {
        if (model.Shapes.Count != 0)
            builder.Content.UseMorphing().SetValue(model.Shapes.Select(s => model.EnabledShapes.Any(n => s.Name.Equals(n, StringComparison.Ordinal)) ? 1f : 0).ToArray());

        if (subMesh.Attributes != null)
        {
            if (!subMesh.Attributes.All(model.EnabledAttributes.Contains))
                builder.Remove();
        }
    }

    private void ApplyMeshModifiers(InstanceBuilder builder, NewMesh mesh, NewModel model)
    {
        if (model.Shapes.Count != 0)
            builder.Content.UseMorphing().SetValue(model.Shapes.Select(s => model.EnabledShapes.Any(n => s.Name.Equals(n, StringComparison.Ordinal)) ? 1f : 0).ToArray());

        if (mesh.Attributes != null)
        {
            if (!mesh.Attributes.All(model.EnabledAttributes.Contains))
                builder.Remove();
        }
    }

    /// <summary>
    /// Handles the creation of a 3D mesh for a given character or object.
    /// </summary>
    /// <param name="glTfMaterial">The material builder for the mesh.</param>
    /// <param name="raceDeformer">The deformer specific to the character's race.</param>
    /// <param name="glTfScene">The scene builder where the mesh will be added.</param>
    /// <param name="xivMesh">The mesh data of the character or object.</param>
    /// <param name="xivModel">The model of the character or object.</param>
    /// <param name="raceCode">The code representing the character's race (nullable).</param>
    /// <param name="deform">The deformation value for the character (nullable).</param>
    /// <param name="boneMap">A dictionary mapping bone names to their corresponding nodes.</param>
    /// <param name="name">The name of the mesh.</param>
    /// <param name="joints">An array of nodes representing joints in the mesh's skeleton.</param>
    /// <returns>A task representing the asynchronous execution of the mesh creation process.</returns>
    private void HandleMeshCreation(MaterialBuilder glTfMaterial,
        RaceDeformer? raceDeformer,
        SceneBuilder glTfScene,
        NewMesh xivMesh,
        NewModel xivModel,
        ushort? raceCode,
        ushort? deform,
        string name,
        List<NodeBuilder> joints,
        Matrix4x4 worldLocation)
    {
        var useSkinning = xivMesh.BoneTable != null;

        // Mapping between ID referenced in the mesh and in Havok
        Dictionary<int, int> jointIdMapping = new();
        if (xivMesh.BoneTable != null)
        {
            var jointLut = joints.Select((joint, i) => (joint.Name, i));
            for (var i = 0; i < xivMesh.BoneTable.Count; ++i)
                jointIdMapping[i] = jointLut.First(x => x.Name.Equals(xivMesh.BoneTable[i], StringComparison.Ordinal)).i;
        }

        // Handle submeshes and the main mesh
        var meshBuilder = new MeshBuilder(
            xivMesh,
            useSkinning,
            jointIdMapping,
            glTfMaterial,
            raceDeformer
        );

        // Deform for full bodies
        if (raceCode != null && deform != null && deform != 0)
        {
            Log.Debug($"Setting up deform steps for {name}, {raceCode.Value}, {deform.Value}");
            meshBuilder.SetupDeformSteps(raceCode.Value, deform.Value);
        }

        meshBuilder.BuildVertices();

        if (xivMesh.Submeshes.Count > 0)
        {
            BuildSubmeshes(meshBuilder, name, xivMesh, xivModel, glTfScene, worldLocation, joints, useSkinning);
        }
        else
        {
            var mesh = meshBuilder.BuildMesh();
            mesh.Name = $"{name}_{xivMesh.MeshIdx}";
            Log.Debug($"Building mesh: \"{mesh.Name}\"");
            meshBuilder.BuildShapes(xivModel.Shapes, mesh, 0, xivMesh.Indices.Count);
            var instance =
                useSkinning ?
                    glTfScene.AddSkinnedMesh(mesh, worldLocation, joints.ToArray()) :
                    glTfScene.AddRigidMesh(mesh, worldLocation);
            ApplyMeshModifiers(instance, xivMesh, xivModel);
        }
    }

    private MaterialBuilder? ComposeTextures(NewMaterial xivMaterial, string outputDir,
        bool copyNormalAlphaToDiffuse,
        CancellationToken cancellationToken)
    {
        var xivTextureMap = new Dictionary<TextureUsage, SKTexture>();

        foreach (var xivTexture in xivMaterial.Textures)
        {
            // Check for cancellation request
            if (cancellationToken.IsCancellationRequested)
                return null;

            if (xivTexture.HandlePath == "dummy.tex")
                continue;

            var dummyRegex = new Regex(@"^.+/dummy_?.+?\.tex$");
            if (dummyRegex.IsMatch(xivTexture.HandlePath))
                continue;

            xivTextureMap.Add(xivTexture.Usage ?? throw new ArgumentException($"Expected usage for {xivTexture.HandlePath}"), new(TextureHelper.ToBitmap(xivTexture.Resource)));
        }

        // reference for this fuckery
        // https://docs.google.com/spreadsheets/u/0/d/1kIKvVsW3fOnVeTi9iZlBDqJo6GWVn6K6BCUIRldEjhw/htmlview#
        var alphaMode = AlphaMode.MASK;
        var backfaceCulling = true;

        var initTextureTypes = xivTextureMap.Keys.ToArray();

        switch (xivMaterial.ShaderPackage.Name)
        {
            case "character.shpk":
                {
                    // not sure if backface culling should be done here, depends on model ugh
                    backfaceCulling = false;
                    TextureUtility.ParseCharacterTextures(xivTextureMap, xivMaterial, Log.PluginLog,
                        copyNormalAlphaToDiffuse);
                    break;
                }
            case "skin.shpk":
                {
                    alphaMode = AlphaMode.MASK;
                    TextureUtility.ParseSkinTextures(xivTextureMap, xivMaterial, Log.PluginLog);
                    break;
                }
            case "hair.shpk":
                {
                    alphaMode = AlphaMode.MASK;
                    backfaceCulling = false;
                    TextureUtility.ParseHairTextures(xivTextureMap, xivMaterial, Log.PluginLog);
                    break;
                }
            case "iris.shpk":
                {
                    TextureUtility.ParseIrisTextures(xivTextureMap!, xivMaterial, Log.PluginLog);
                    break;
                }
            default:
                Log.Warning($"Unhandled shader pack {xivMaterial.ShaderPackage.Name}");
                break;
        }

        var textureTypes = xivTextureMap.Keys.ToArray();
        // log texturetypes
        // if new value present show (new)
        // if value missing show (missing)
        var newTextureTypes = textureTypes.Except(initTextureTypes).ToArray();
        var missingTextureTypes = initTextureTypes.Except(textureTypes).ToArray();
        Log.Debug($"Texture types for {xivMaterial.ShaderPackage.Name} {xivMaterial.HandlePath}\n" +
                   $"New: {string.Join(", ", newTextureTypes)}\n" +
                   $"Missing: {string.Join(", ", missingTextureTypes)}\n" +
                   $"Final: {string.Join(", ", textureTypes)}");

        var glTfMaterial = new MaterialBuilder
        {
            Name = Path.GetFileNameWithoutExtension(xivMaterial.HandlePath),
            AlphaMode = alphaMode,
            DoubleSided = !backfaceCulling
        };

        TextureUtility.ExportTextures(glTfMaterial, xivTextureMap, outputDir);

        return glTfMaterial;
    }
}
