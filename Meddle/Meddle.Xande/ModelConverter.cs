using Dalamud.Plugin.Services;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Models.Materials;
using Lumina.Models.Models;
using Meddle.Xande.Utility;
using Penumbra.Api;
using Penumbra.Api.Enums;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.RegularExpressions;
using Xande;
using Xande.Enums;
using Xande.Files;
using Xande.Havok;
using Xande.Models.Export;
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

    // tbd if this is needed, ran into issues when accessing multiple skeletons in succession
    private  Dictionary<string, HavokXml> SkeletonCache { get; } = new();

    public ModelConverter(HavokConverter converter, LuminaManager luminaManager, IPluginLog log,
        IFramework framework)
    {
        Converter = converter;
        LuminaManager = luminaManager;
        Log = new ModelConverterLogger(log);
        Framework = framework;
        Pbd = LuminaManager.GetPbdFile();
    }

    public Task ExportResourceTree(Ipc.ResourceTree tree, bool[] enabledNodes, bool openFolderWhenComplete,
        ExportType exportType,
        string exportPath,
        bool copyNormalAlphaToDiffuse,
        Dictionary<string, AffineTransform>? currentPose,
        List<HkSkeleton.WeaponData>? weaponInfos,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(exportPath, $"{tree.GetHashCode():X8}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
        Directory.CreateDirectory(path);

        return Framework.RunOnTick(() =>
        {
            List<Ipc.ResourceNode> nodes = new();
            for (var i = 0; i < enabledNodes.Length; i++)
            {
                if (enabledNodes[i] == false) continue;
                nodes.Add(tree.Nodes[i]);
            }

            Log.Debug($"Exporting character to {path}");

            var skeletonNodes = tree.Nodes.Where(x => x.Type == ResourceType.Sklb).ToList();

            skeletonNodes.Sort((x, y) =>
            {
                if (x.GamePath?.Contains("weapon") ?? false)
                {
                    return 1;
                }

                if (y.GamePath?.Contains("weapon") ?? false)
                {
                    return -1;
                }

                return 0;
            });

            // will error if not done on the framework thread
            var skeletons = new List<HkSkeleton>();
            try
            {
                foreach (var node in skeletonNodes)
                {
                    HkSkeleton.WeaponData? weaponInfo = null;
                    if (node.GamePath?.Contains("weapon") ?? false)
                        weaponInfo = weaponInfos?.FirstOrDefault(n => n.SklbPath == node.GamePath);
                    foreach(var i in weaponInfos!)
                    {
                        Log.Debug($"WeaponInfo {i.SklbPath} {i.ModelPath} {i.BoneName}");
                        Log.Debug($"{weaponInfo}");
                    }

                    // cannot use fullpath because things like ivcs are fucky and crash the game
                    try
                    {
                        skeletons.Add(new(LoadSkeleton(node.FullPath()), weaponInfo));
                        Log.Debug($"Loaded skeleton {node.FullPath()}");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to load {node.FullPath()}, falling back to GamePath");
                    }

                    try
                    {
                        skeletons.Add(new(LoadSkeleton(node.GamePath ?? string.Empty), weaponInfo));
                        Log.Debug($"Loaded skeleton {node.GamePath}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to load {node.GamePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading skeletons");
                return Task.CompletedTask;
            }

            return Task.Run(async () =>
            {
                try
                {
                    await ExportModel(path, skeletons, currentPose, tree, nodes, exportType, copyNormalAlphaToDiffuse,
                        cancellationToken);
                    // open path
                    if (openFolderWhenComplete)
                    {
                        Process.Start("explorer.exe", path);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while exporting character");
                }
            }, cancellationToken);
        }, cancellationToken: cancellationToken);
    }

    private HavokXml LoadSkeleton(string path)
    {
        if (SkeletonCache.TryGetValue(path, out var havokXml))
            return havokXml;

        var file = LuminaManager.GetFile<FileResource>(path)
            ?? throw new Exception("GetFile returned null");

        var sklb = SklbFile.FromStream(file.Reader.BaseStream);

        var xml = Converter.HkxToXml(sklb.HkxData);
        var ret = new HavokXml(xml);
        SkeletonCache.Add(path, ret);
        return ret;
    }

    public async Task ExportModel(
        string exportPath,
        IEnumerable<HkSkeleton> skeletons,
        Dictionary<string, AffineTransform>? currentPose,
        Ipc.ResourceTree tree,
        IEnumerable<Ipc.ResourceNode> nodes,
        ExportType exportType,
        bool copyNormalAlphaToDiffuse,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var modelNodes = nodes.Where(x =>
                x.Type == ResourceType.Mdl).ToArray();
            var glTfScene = new SceneBuilder(modelNodes.Length > 0 ? modelNodes[0].FullPath() : "scene");
            var modelTasks = new List<Task>();
            var skeletonArray = skeletons.ToArray();
            var mainSkeletons = skeletonArray.Where(s => s.WeaponInfo == null).Select(s => s.Xml).ToArray();
            var weaponSkeletons = skeletonArray.Where(s => s.WeaponInfo != null).ToArray();

            NodeBuilder? rootArmatureNode = null;
            Dictionary<string, NodeBuilder>? rootArmatureBoneMap = null;

            if (mainSkeletons.Length > 0)
            {
                rootArmatureBoneMap = currentPose == null ?
                    ModelUtility.GetReferenceBoneMap(mainSkeletons, out rootArmatureNode) :
                    ModelUtility.GetBoneMap(mainSkeletons, currentPose, out rootArmatureNode);
                var joints = rootArmatureBoneMap.Values.ToArray();
                var raceDeformer = new RaceDeformer(Pbd, rootArmatureBoneMap);
                if (rootArmatureNode != null)
                    glTfScene.AddNode(rootArmatureNode);

                // chara/human/c1101/obj/body/b0003/model/c1101b0003_top.mdl
                var stupidLowPolyModelRegex =
                    new Regex(@"^chara/human/c\d+/obj/body/b0003/model/c\d+b0003_top.mdl$");
                foreach (var node in modelNodes)
                {
                    if (stupidLowPolyModelRegex.IsMatch(node.GamePath ?? string.Empty))
                    {
                        Log.Warning($"Skipping model {node.FullPath()}");
                        continue;
                    }

                    if (node.GamePath?.Contains("weapon") ?? false)
                        continue;

                    Log.Debug($"Handling model {node.FullPath()}");
                    modelTasks.Add(HandleModel(node, raceDeformer, tree.RaceCode, exportPath, rootArmatureBoneMap, joints, glTfScene,
                        copyNormalAlphaToDiffuse, Matrix4x4.Identity,
                        cancellationToken));
                }
            }
            else
            {
                foreach (var node in modelNodes)
                {
                    modelTasks.Add(Task.Run(async () =>
                    {
                        if (!LuminaManager.TryGetModel(node, tree.RaceCode, out var modelPath, out var model))
                            return;

                        var fileName = Path.GetFileNameWithoutExtension(modelPath);
                        var materials =
                            new List<(string fullpath, string gamepath, MaterialBuilder material)>();
                        var textureTasks = new List<Task>();

                        foreach (var child in node.Children)
                        {
                            textureTasks.Add(Task.Run(async () =>
                            {
                                if (child.Type != ResourceType.Mtrl)
                                {
                                    return;
                                }

                                Material? material;
                                try
                                {
                                    var mtrlFile = Path.IsPathRooted(child.FullPath())
                                        ? LuminaManager.GameData.GetFileFromDisk<MtrlFile>(child.FullPath(),
                                            child.GamePath)
                                        : LuminaManager.GameData.GetFile<MtrlFile>(child.FullPath());

                                    if (mtrlFile == null)
                                    {
                                        Log.Warning($"Could not load material {child.FullPath()}");
                                        return;
                                    }

                                    material = new Material(mtrlFile);
                                }
                                catch (Exception e)
                                {
                                    Log.Error(e, $"Failed to load material {child.FullPath()}");
                                    return;
                                }

                                try
                                {
                                    var glTfMaterial =
                                        await ComposeTextures(material, exportPath, child.Children,
                                            copyNormalAlphaToDiffuse,
                                            cancellationToken);

                                    if (glTfMaterial == null)
                                    {
                                        return;
                                    }

                                    materials.Add((child.FullPath(), child.GamePath ?? string.Empty, glTfMaterial));
                                }
                                catch (Exception e)
                                {
                                    Log.Error(e, $"Failed to compose textures for material {child.FullPath()}");
                                }
                            }, cancellationToken));
                        }

                        await Task.WhenAll(textureTasks);

                        // log all materials 
                        Log.Debug($"Handling model {fileName} with {model.Meshes.Length} meshes\n" +
                                   $"Using materials\n{string.Join("\n", materials.Select(x => x.fullpath == x.gamepath ? x.fullpath : $"{x.gamepath} -> {x.fullpath}"))}");

                        foreach (var mesh in model.Meshes)
                        {
                            mesh.Material.Update(LuminaManager.GameData);
                            var material = materials.FirstOrDefault(x =>
                                x.fullpath == mesh.Material.ResolvedPath ||
                                x.gamepath == mesh.Material.ResolvedPath ||
                                x.fullpath == mesh.Material.MaterialPath ||
                                x.gamepath == mesh.Material.MaterialPath);

                            if (material == default)
                            {
                                var match = materials
                                    .Select(x => (x.fullpath, x.gamepath,
                                        x.fullpath.ComputeLd(mesh.Material.MaterialPath))).OrderBy(x => x.Item3)
                                    .FirstOrDefault();
                                var match2 = materials
                                    .Select(x => (x.fullpath, x.gamepath,
                                        x.gamepath.ComputeLd(mesh.Material.MaterialPath))).OrderBy(x => x.Item3)
                                    .FirstOrDefault();

                                material = match.Item3 < match2.Item3
                                    ? materials.FirstOrDefault(x =>
                                        x.fullpath == match.fullpath || x.gamepath == match.gamepath)
                                    : materials.FirstOrDefault(x =>
                                        x.fullpath == match2.fullpath || x.gamepath == match2.gamepath);
                            }

                            if (material == default)
                            {
                                Log.Warning($"Could not find material for {mesh.Material.ResolvedPath}");
                                continue;
                            }

                            try
                            {
                                if (mesh.Material.ResolvedPath != material.gamepath)
                                {
                                    Log.Warning(
                                        $"Using material {material.gamepath} for {mesh.Material.ResolvedPath}");
                                }


                                var meshbuilder = new MeshBuilder(mesh,
                                    false,
                                    new Dictionary<int, int>(),
                                    material.material,
                                    new RaceDeformer(Pbd, new Dictionary<string, NodeBuilder>()));


                                meshbuilder.BuildVertices();

                                for (int i = 0; i < mesh.Submeshes.Length; i++)
                                {
                                    var sub = mesh.Submeshes[i];
                                    var submesh = meshbuilder.BuildSubmesh(sub);
                                    submesh.Name = $"{sub.IndexNum}_{i}";
                                    meshbuilder.BuildShapes(model.Shapes.Values.ToArray(), submesh,
                                        (int)mesh.Submeshes[i].IndexOffset,
                                        (int)(mesh.Submeshes[i].IndexOffset + mesh.Submeshes[i].IndexNum));
                                    glTfScene.AddRigidMesh(submesh, Matrix4x4.Identity);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e,
                                    $"Failed to handle mesh creation for {mesh.Material.ResolvedPath}");
                            }
                        }
                    }, cancellationToken));
                }
            }

            foreach (var weaponSkel in weaponSkeletons)
            {
                var info = weaponSkel.WeaponInfo!;
                var boneMap = ModelUtility.GetWeaponBoneMap(weaponSkel, out var root);
                var joints = boneMap.Values.ToArray();

                if (rootArmatureNode == null || rootArmatureBoneMap == null)
                    glTfScene.AddNode(root);
                else
                    rootArmatureBoneMap[info.BoneName].AddNode(root);

                var transform = Matrix4x4.Identity;
                var c = root;
                while (c != null)
                {
                    transform *= c.LocalMatrix;
                    c = c.Parent;
                }

                if (modelNodes.FirstOrDefault(n => n.GamePath == info.ModelPath) is { } node)
                {
                    Log.Debug($"Handling weapon model {node.FullPath()}");
                    
                    modelTasks.Add(HandleModel(node, null, tree.RaceCode, exportPath, boneMap, joints, glTfScene,
                        copyNormalAlphaToDiffuse, transform,
                        cancellationToken));
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

    private async Task HandleModel(Ipc.ResourceNode node, RaceDeformer? raceDeformer, ushort? deform, string exportPath,
        Dictionary<string, NodeBuilder> boneMap, NodeBuilder[] joints,
        SceneBuilder glTfScene, bool copyNormalAlphaToDiffuse, Matrix4x4 worldLocation, CancellationToken cancellationToken)
    {
        Log.Info($"Handling model {node.GamePath}");
        var path = node.FullPath();

        if (!LuminaManager.TryGetModel(node, deform, out var modelPath, out var model))
            return;

        if (model == null) return;

        if (string.Equals(path, modelPath, StringComparison.InvariantCultureIgnoreCase))
            Log.Debug($"Using full path for {path}");
        else
            Log.Debug($"Retrieved model\n" +
                       $"Used path: {modelPath}\n" +
                       $"Init path: {path}");

        var fileName = Path.GetFileNameWithoutExtension(path);

        ushort? raceCode = null;
        try
        {
            raceCode = raceDeformer?.RaceCodeFromPath(path);
        }
        catch (Exception e)
        {
            Log.Error(e, $"Failed to parse race code from path {path}");
        }


        // reaper eye go away
        var stupidEyeMeshRegex = new Regex(@"^/mt_c\d+f.+_etc_b.mtrl$");
        var meshes = model.Meshes.Where(x => x.Types.Contains(Mesh.MeshType.Main) &&
                                             !stupidEyeMeshRegex.IsMatch(x.Material.MaterialPath.ToString()))
            .ToArray();

        var materials = new List<(string fullpath, string gamepath, MaterialBuilder material)>();

        var textureTasks = new List<Task>();

        foreach (var child in node.Children)
        {
            textureTasks.Add(Task.Run(async () =>
            {
                if (child.Type != ResourceType.Mtrl)
                {
                    return;
                }

                Material? material;
                try
                {
                    var mtrlFile = Path.IsPathRooted(child.FullPath())
                        ? LuminaManager.GameData.GetFileFromDisk<MtrlFile>(child.FullPath(), child.GamePath)
                        : LuminaManager.GameData.GetFile<MtrlFile>(child.FullPath());

                    if (mtrlFile == null)
                    {
                        Log.Warning($"Could not load material {child.FullPath()}");
                        return;
                    }

                    material = new Material(mtrlFile);
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Failed to load material {child.FullPath()}");
                    return;
                }

                try
                {
                    var glTfMaterial =
                        await ComposeTextures(material, exportPath, child.Children, copyNormalAlphaToDiffuse,
                            cancellationToken);

                    if (glTfMaterial == null)
                    {
                        return;
                    }

                    materials.Add((child.FullPath(), child.GamePath ?? string.Empty, glTfMaterial));
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Failed to compose textures for material {child.FullPath()}");
                }
            }, cancellationToken));
        }

        await Task.WhenAll(textureTasks);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        foreach (var mesh in meshes)
        {
            mesh.Material.Update(LuminaManager.GameData);
        }

        Log.Debug(
            $"Handling model {fileName} with {meshes.Length} meshes\n" +
            $"{string.Join("\n", meshes.Select(x => x.Material.ResolvedPath))}\n" +
            $"Using materials\n{string.Join("\n", materials.Select(x => x.fullpath == x.gamepath ? x.fullpath : $"{x.gamepath} -> {x.fullpath}"))}");

        foreach (var mesh in meshes)
        {
            // try get material from materials
            var material = materials.FirstOrDefault(x =>
                x.fullpath == mesh.Material.ResolvedPath || x.gamepath == mesh.Material.ResolvedPath);

            if (material == default)
            {
                // match most similar material from list
                if (mesh.Material.ResolvedPath == null)
                {
                    Log.Warning($"Could not find material for {mesh.Material.ResolvedPath}");
                    continue;
                }

                var match = materials
                    .Select(x => (x.fullpath, x.gamepath,
                        x.fullpath.ComputeLd(mesh.Material.ResolvedPath))).OrderBy(x => x.Item3)
                    .FirstOrDefault();
                var match2 = materials
                    .Select(x => (x.fullpath, x.gamepath,
                        x.gamepath.ComputeLd(mesh.Material.ResolvedPath))).OrderBy(x => x.Item3)
                    .FirstOrDefault();

                material = match.Item3 < match2.Item3
                    ? materials.FirstOrDefault(x => x.fullpath == match.fullpath || x.gamepath == match.gamepath)
                    : materials.FirstOrDefault(x => x.fullpath == match2.fullpath || x.gamepath == match2.gamepath);
            }

            if (material == default)
            {
                Log.Warning($"Could not find material for {mesh.Material.ResolvedPath}");
                continue;
            }

            try
            {
                if (mesh.Material.ResolvedPath != material.gamepath)
                {
                    Log.Warning($"Using material {material.gamepath} for {mesh.Material.ResolvedPath}");
                }

                await HandleMeshCreation(material.material, raceDeformer, glTfScene, mesh, model, raceCode, deform,
                    boneMap, fileName, joints, worldLocation);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Failed to handle mesh creation for {mesh.Material.ResolvedPath}");
            }
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
    private Task HandleMeshCreation(MaterialBuilder glTfMaterial,
        RaceDeformer? raceDeformer,
        SceneBuilder glTfScene,
        Mesh xivMesh,
        Model xivModel,
        ushort? raceCode,
        ushort? deform,
        IReadOnlyDictionary<string, NodeBuilder> boneMap,
        string name,
        NodeBuilder[] joints,
        Matrix4x4 worldLocation)
    {
        var boneSet = xivMesh.BoneTable();
        var boneSetJoints = boneSet?.Select(n =>
        {
            if (boneMap.TryGetValue(n, out var node))
                return node;

            Log.Warning($"Could not find bone {n} in boneMap");
            return null;
        }).Where(x => x != null).Select(x => x!).ToArray();
        var useSkinning = boneSet != null;

        // Mapping between ID referenced in the mesh and in Havok
        Dictionary<int, int> jointIdMapping = new();
        for (var i = 0; i < boneSetJoints?.Length; i++)
        {
            var joint = boneSetJoints[i];
            var idx = joints.ToList().IndexOf(joint);
            jointIdMapping[i] = idx;
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

        if (xivMesh.Submeshes.Length > 0)
        {
            for (var i = 0; i < xivMesh.Submeshes.Length; i++)
            {
                try
                {
                    var xivSubmesh = xivMesh.Submeshes[i];
                    var subMesh = meshBuilder.BuildSubmesh(xivSubmesh);
                    subMesh.Name = $"{name}_{xivMesh.MeshIndex}.{i}";
                    meshBuilder.BuildShapes(xivModel.Shapes.Values.ToArray(), subMesh, (int)xivSubmesh.IndexOffset,
                        (int)(xivSubmesh.IndexOffset + xivSubmesh.IndexNum));

                    if (!NodeBuilder.IsValidArmature(joints))
                    {
                        if (joints == null) Log.Warning("0");
                        if (!joints.Any()) Log.Warning("1");
                        if (joints.Any(item => item == null)) Log.Warning("2");

                        var root = joints.First().Root;

                        // check if all joints share the same root
                        if (!joints.All(item => ReferenceEquals(item.Root, root))) Log.Warning("3");

                        var nameGroups = NodeBuilder.Flatten(root)
                            .Where(item => item.Name != null)
                            .GroupBy(item => item.Name);

                        if (nameGroups.Any(group => group.Count() > 1))
                        {
                            Log.Warning("4");
                            nameGroups.Where(group => group.Count() > 1)
                                .Select(group => group.Key)
                                .ToList()
                                .ForEach(name => Log.Warning($"Duplicate bone name: {name}"));
                        }

                        Log.Warning(
                            $"Joints are not valid, skipping submesh {i} for {name}, {string.Join(", ", joints.Select(x => x.Name))}");
                        continue;
                    }

                    if (useSkinning)
                    {
                        glTfScene.AddSkinnedMesh(subMesh, worldLocation, joints);
                    }
                    else
                    {
                        glTfScene.AddRigidMesh(subMesh, worldLocation);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Failed to build submesh {i} for {name}");
                }
            }
        }
        else
        {
            var mesh = meshBuilder.BuildMesh();
            mesh.Name = $"{name}_{xivMesh.MeshIndex}";
            Log.Debug($"Building mesh: \"{mesh.Name}\"");
            meshBuilder.BuildShapes(xivModel.Shapes.Values.ToArray(), mesh, 0, xivMesh.Indices.Length);
            if (useSkinning)
            {
                glTfScene.AddSkinnedMesh(mesh, worldLocation, joints);
            }
            else
            {
                glTfScene.AddRigidMesh(mesh, worldLocation);
            }
        }

        return Task.CompletedTask;
    }

    private async Task<MaterialBuilder?> ComposeTextures(Material xivMaterial, string outputDir, IEnumerable<Ipc.ResourceNode>? nodes,
        bool copyNormalAlphaToDiffuse,
        CancellationToken cancellationToken)
    {
        var xivTextureMap = new Dictionary<TextureUsage, Bitmap>();

        foreach (var xivTexture in xivMaterial.Textures)
        {
            // Check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (xivTexture.TexturePath == "dummy.tex")
            {
                continue;
            }

            var dummyRegex = new Regex(@"^.+/dummy_?.+?\.tex$");
            if (dummyRegex.IsMatch(xivTexture.TexturePath))
            {
                continue;
            }

            var texturePath = xivTexture.TexturePath;
            // try find matching node for tex file
            if (nodes != null)
            {
                var nodeMatch = nodes.FirstOrDefault(x => x.GamePath == texturePath);
                if (nodeMatch != null)
                {
                    texturePath = nodeMatch.FullPath();
                }
                else
                {
                    var fileName = Path.GetFileNameWithoutExtension(texturePath);
                    // try get using contains
                    nodeMatch = nodes.FirstOrDefault(x => x.GamePath.Contains(fileName));

                    if (nodeMatch != null)
                    {
                        texturePath = nodeMatch.FullPath();
                    }
                }
            }

            var textureBuffer =
                TextureUtility.GetTextureBufferCopy(LuminaManager, texturePath, xivTexture.TexturePath);
            xivTextureMap.Add(xivTexture.TextureUsageRaw, textureBuffer);
        }

        // reference for this fuckery
        // https://docs.google.com/spreadsheets/u/0/d/1kIKvVsW3fOnVeTi9iZlBDqJo6GWVn6K6BCUIRldEjhw/htmlview#
        var alphaMode = AlphaMode.MASK;
        var backfaceCulling = true;

        var initTextureTypes = xivTextureMap.Keys.ToArray();

        switch (xivMaterial.ShaderPack)
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
                Log.Warning($"Unhandled shader pack {xivMaterial.ShaderPack}");
                break;
        }

        var textureTypes = xivTextureMap.Keys.ToArray();
        // log texturetypes
        // if new value present show (new)
        // if value missing show (missing)
        var newTextureTypes = textureTypes.Except(initTextureTypes).ToArray();
        var missingTextureTypes = initTextureTypes.Except(textureTypes).ToArray();
        Log.Debug($"Texture types for {xivMaterial.ShaderPack} {xivMaterial.File?.FilePath.Path}\n" +
                   $"New: {string.Join(", ", newTextureTypes)}\n" +
                   $"Missing: {string.Join(", ", missingTextureTypes)}\n" +
                   $"Final: {string.Join(", ", textureTypes)}\n" +
                   $"Nodes:\n{string.Join("\n", nodes?.Select(x => $"{x.FullPath()} -> {x.GamePath}") ?? Array.Empty<string>())}");

        var glTfMaterial = new MaterialBuilder
        {
            Name = xivMaterial.File?.FilePath.Path,
            AlphaMode = alphaMode,
            DoubleSided = !backfaceCulling
        };

        await TextureUtility.ExportTextures(glTfMaterial, xivTextureMap, outputDir);

        return glTfMaterial;
    }
}
