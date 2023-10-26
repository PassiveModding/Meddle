using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Meddle.Lumina.Materials;
using Meddle.Lumina.Models;
using Meddle.Xande.Enums;
using Meddle.Xande.Models;
using Meddle.Xande.Utility;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using Xande;
using Xande.Files;
using Xande.Havok;
using Xande.Models.Export;

namespace Meddle.Xande
{
    public class ModelConverter
    {
        private readonly HavokConverter _converter;
        private readonly LuminaManager _luminaManager;
        private readonly IPluginLog _log;
        private readonly IFramework _framework;
        private readonly PbdFile _pbd;

        // tbd if this is needed, ran into issues when accessing multiple skeletons in succession
        private readonly Dictionary<string, HavokXml> _skeletonCache = new();

        // prevent multiple exports at once
        private static readonly SemaphoreSlim ExportSemaphore = new(1, 1);

        public ModelConverter(HavokConverter converter, LuminaManager luminaManager, IPluginLog log, IFramework framework)
        {
            _converter = converter;
            _luminaManager = luminaManager;
            _log = log;
            _framework = framework;
            _pbd = _luminaManager.GetPbdFile();
        }

        public Task ExportResourceTree(ResourceTree tree, bool[] enabledNodes, bool openFolderWhenComplete,
            ExportType exportType,
            string exportPath,
            CancellationToken cancellationToken)
        {
            if (ExportSemaphore.CurrentCount == 0)
            {
                _log.Warning("Export already in progress");
                throw new Exception("Export already in progress");
            }

            Directory.CreateDirectory(exportPath);
            var path = Path.Combine(exportPath, $"{tree.Name}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
            Directory.CreateDirectory(path);

            return _framework.RunOnTick(() =>
            {
                List<Node> nodes = new();
                for (int i = 0; i < enabledNodes.Length; i++)
                {
                    if (enabledNodes[i] == false) continue;
                    var node = tree.Nodes[i];
                    nodes.Add(node);
                }

                _log.Debug($"Exporting character to {path}");

                // skeletons should only be at the root level so no need to go further
                // do not exclude skeletons regardless of option (because its annoying)
                var skeletonNodes = tree.Nodes.Where(x => x.Type == Penumbra.Api.Enums.ResourceType.Sklb).ToList();
                // if skeleton is for weapon, move it to the end
                skeletonNodes.Sort((x, y) =>
                {
                    if (x.GamePath.Contains("weapon"))
                    {
                        return 1;
                    }

                    if (y.GamePath.Contains("weapon"))
                    {
                        return -1;
                    }

                    return 0;
                });

                // will error if not done on the framework thread
                var skeletons = new List<HavokXml>();
                try
                {
                    foreach (var node in skeletonNodes)
                    {
                        // cannot use fullpath because things like ivcs are fucky and crash the game
                        var nodePath = node.FullPath;
                        if (_skeletonCache.TryGetValue(nodePath, out var havokXml))
                        {
                            skeletons.Add(havokXml);
                            continue;
                        }

                        try
                        {
                            var file = _luminaManager.GetFile<FileResource>(nodePath);

                            if (file == null)
                            {
                                throw new Exception("GetFile returned null");
                            }

                            var sklb = SklbFile.FromStream(file.Reader.BaseStream);

                            var xml = _converter.HkxToXml(sklb.HkxData);
                            havokXml = new HavokXml(xml);
                            skeletons.Add(havokXml);
                            _skeletonCache.Add(nodePath, havokXml);
                            _log.Debug($"Loaded skeleton {nodePath}");
                            continue;
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex, $"Failed to load {nodePath}, falling back to GamePath");
                        }

                        nodePath = node.GamePath;
                        if (_skeletonCache.TryGetValue(nodePath, out havokXml))
                        {
                            skeletons.Add(havokXml);
                            continue;
                        }

                        try
                        {
                            var file = _luminaManager.GetFile<FileResource>(nodePath);

                            if (file == null)
                            {
                                throw new Exception("GetFile returned null");
                            }

                            var sklb = SklbFile.FromStream(file.Reader.BaseStream);

                            var xml = _converter.HkxToXml(sklb.HkxData);
                            havokXml = new HavokXml(xml);
                            skeletons.Add(havokXml);
                            _skeletonCache.Add(nodePath, havokXml);
                            _log.Debug($"Loaded skeleton {nodePath}");
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex, $"Failed to load {nodePath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error loading skeletons");
                    return Task.CompletedTask;
                }

                return Task.Run(async () =>
                {
                    if (ExportSemaphore.CurrentCount == 0)
                    {
                        _log.Warning("Export already in progress");
                        return;
                    }

                    await ExportSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        await ExportModel(path, skeletons, tree, nodes, exportType, cancellationToken);
                        // open path
                        if (openFolderWhenComplete)
                        {
                            Process.Start("explorer.exe", path);
                        }
                    }
                    catch (Exception e)
                    {
                        _log.Error(e, "Error while exporting character");
                    }
                    finally
                    {
                        ExportSemaphore.Release();
                    }
                }, cancellationToken);
            }, cancellationToken: cancellationToken);
        }

        public async Task ExportModel(string exportPath, IEnumerable<HavokXml> skeletons, ResourceTree tree,
            IEnumerable<Node> nodes,
            ExportType exportType,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var deform = (ushort) tree.RaceCode;
                var boneMap = ModelUtility.GetBoneMap(skeletons.ToArray(), out var root);
                var joints = boneMap.Values.ToArray();
                var raceDeformer = new RaceDeformer(_pbd, boneMap);
                var modelNodes = nodes.Where(x =>
                    x.Type == Penumbra.Api.Enums.ResourceType.Mdl).ToArray();
                var glTfScene = new SceneBuilder(modelNodes.Length > 0 ? modelNodes[0].GamePath : "scene");
                if (root != null)
                {
                    glTfScene.AddNode(root);
                }

                var modelTasks = new List<Task>();
                // chara/human/c1101/obj/body/b0003/model/c1101b0003_top.mdl
                var stupidLowPolyModelRegex = new Regex(@"^chara/human/c\d+/obj/body/b0003/model/c\d+b0003_top.mdl$");
                foreach (var node in modelNodes)
                {
                    if (stupidLowPolyModelRegex.IsMatch(node.GamePath))
                    {
                        _log.Warning($"Skipping model {node.FullPath}");
                        continue;
                    }

                    _log.Debug($"Handling model {node.FullPath}");
                    modelTasks.Add(HandleModel(node, raceDeformer, deform, exportPath, boneMap, joints, glTfScene,
                        cancellationToken));
                }

                await Task.WhenAll(modelTasks);

                var glTfModel = glTfScene.ToGltf2();
                
                // check if exportType contains each type using flags
                if (exportType.HasFlag(ExportType.Glb))
                {
                    var glbFolder = Path.Combine(exportPath, "glb");
                    Directory.CreateDirectory(glbFolder);
                    glTfModel.SaveGLB(Path.Combine(glbFolder, "glb.glb"));
                }
                
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
                    //glTfModel.SaveAsWavefront(Path.Combine(waveFrontFolder, "wavefront.obj"));
                    SharpGLTF.Schema2.Toolkit.SaveAsWavefront(glTfModel, Path.Combine(waveFrontFolder, "wavefront.obj"));
                }

                _log.Debug($"Exported model to {exportPath}");
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to export model");
            }
        }

        private async Task HandleModel(Node node, RaceDeformer raceDeformer, ushort? deform, string exportPath,
            Dictionary<string, NodeBuilder> boneMap, NodeBuilder[] joints,
            SceneBuilder glTfScene, CancellationToken cancellationToken)
        {
            var path = node.FullPath;
            //var file = _luminaManager.GetFile<FileResource>(path);
            if (!ModelUtility.TryGetModel(_luminaManager, node, deform, out var modelPath, out var model))
            {
                return;
            }

            if (model == null) return;

            if (string.Equals(path, modelPath, StringComparison.InvariantCultureIgnoreCase))
            {
                _log.Debug($"Using full path for {path}");
            }
            else
            {
                _log.Debug($"Retrieved model\n" +
                           $"Used path: {modelPath}\n" +
                           $"Init path: {path}");
            }

            var fileName = Path.GetFileNameWithoutExtension(path);
            var raceCode = raceDeformer.RaceCodeFromPath(path);

            // reaper eye go away
            var stupidEyeMeshRegex = new Regex(@"^/mt_c\d+f.+_etc_b.mtrl$");
            var meshes = model.Meshes.Where(x => x.Types.Contains(Mesh.MeshType.Main) &&
                                                 !stupidEyeMeshRegex.IsMatch(x.Material.MaterialPath.ToString()))
                .ToArray();
            var nodeChildren = node.Children.ToList();

            var materials = new List<(string fullpath, string gamepath, MaterialBuilder material)>();

            var textureTasks = new List<Task>();

            foreach (var child in nodeChildren)
            {
                textureTasks.Add(Task.Run(async () =>
                {
                    if (child.Type != Penumbra.Api.Enums.ResourceType.Mtrl)
                    {
                        return;
                    }

                    Material? material;
                    try
                    {
                        var mtrlFile = Path.IsPathRooted(child.FullPath)
                            ? _luminaManager.GameData.GetFileFromDisk<MtrlFile>(child.FullPath, child.GamePath)
                            : _luminaManager.GameData.GetFile<MtrlFile>(child.FullPath);

                        if (mtrlFile == null)
                        {
                            _log.Warning($"Could not load material {child.FullPath}");
                            return;
                        }

                        material = new Material(mtrlFile);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e, $"Failed to load material {child.FullPath}");
                        return;
                    }

                    try
                    {
                        var glTfMaterial =
                            await ComposeTextures(material, exportPath, child.Children, cancellationToken);

                        if (glTfMaterial == null)
                        {
                            return;
                        }

                        materials.Add((child.FullPath, child.GamePath, glTfMaterial));
                    }
                    catch (Exception e)
                    {
                        _log.Error(e, $"Failed to compose textures for material {child.FullPath}");
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
                mesh.Material.Update(_luminaManager.GameData);
            }

            _log.Debug(
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
                        _log.Warning($"Could not find material for {mesh.Material.ResolvedPath}");
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
                    _log.Warning($"Could not find material for {mesh.Material.ResolvedPath}");
                    continue;
                }

                try
                {
                    if (mesh.Material.ResolvedPath != material.gamepath)
                    {
                        _log.Warning($"Using material {material.gamepath} for {mesh.Material.ResolvedPath}");
                    }

                    await HandleMeshCreation(material.material, raceDeformer, glTfScene, mesh, model, raceCode, deform,
                        boneMap, fileName, joints);
                }
                catch (Exception e)
                {
                    _log.Error(e, $"Failed to handle mesh creation for {mesh.Material.ResolvedPath}");
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
            RaceDeformer raceDeformer,
            SceneBuilder glTfScene,
            Mesh xivMesh,
            Model xivModel,
            ushort? raceCode,
            ushort? deform,
            IReadOnlyDictionary<string, NodeBuilder> boneMap,
            string name,
            NodeBuilder[] joints)
        {
            var boneSet = xivMesh.BoneTable();
            //var boneSetJoints = boneSet?.Select( n => boneMap[n] ).ToArray();
            var boneSetJoints = boneSet?.Select(n =>
            {
                if (boneMap.TryGetValue(n, out var node))
                {
                    return node;
                }

                _log.Warning($"Could not find bone {n} in boneMap");
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
            if (raceCode != null && deform != null)
            {
                _log.Debug($"Setting up deform steps for {name}, {raceCode.Value}, {deform.Value}");
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
                        meshBuilder.BuildShapes(xivModel.Shapes.Values.ToArray(), subMesh, (int) xivSubmesh.IndexOffset,
                            (int) (xivSubmesh.IndexOffset + xivSubmesh.IndexNum));

                        if (!NodeBuilder.IsValidArmature(joints))
                        {
                            _log.Warning(
                                $"Joints are not valid, skipping submesh {i} for {name}, {string.Join(", ", joints.Select(x => x.Name))}");
                            continue;
                        }

                        if (useSkinning)
                        {
                            glTfScene.AddSkinnedMesh(subMesh, Matrix4x4.Identity, joints);
                        }
                        else
                        {
                            glTfScene.AddRigidMesh(subMesh, Matrix4x4.Identity);
                        }
                    }
                    catch (Exception e)
                    {
                        _log.Error(e, $"Failed to build submesh {i} for {name}");
                    }
                }
            }
            else
            {
                var mesh = meshBuilder.BuildMesh();
                mesh.Name = $"{name}_{xivMesh.MeshIndex}";
                _log.Debug($"Building mesh: \"{mesh.Name}\"");
                meshBuilder.BuildShapes(xivModel.Shapes.Values.ToArray(), mesh, 0, xivMesh.Indices.Length);
                if (useSkinning)
                {
                    glTfScene.AddSkinnedMesh(mesh, Matrix4x4.Identity, joints);
                }
                else
                {
                    glTfScene.AddRigidMesh(mesh, Matrix4x4.Identity);
                }
            }

            return Task.CompletedTask;
        }

        private async Task<MaterialBuilder?> ComposeTextures(Material xivMaterial, string outputDir, Node[]? nodes,
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

                var texturePath = xivTexture.TexturePath;
                // try find matching node for tex file
                if (nodes != null)
                {
                    var nodeMatch = nodes.FirstOrDefault(x => x.GamePath == texturePath);
                    if (nodeMatch != null)
                    {
                        texturePath = nodeMatch.FullPath;
                    }
                    else
                    {
                        var fileName = Path.GetFileNameWithoutExtension(texturePath);
                        // try get using contains
                        nodeMatch = nodes.FirstOrDefault(x => x.GamePath.Contains(fileName));

                        if (nodeMatch != null)
                        {
                            texturePath = nodeMatch.FullPath;
                        }
                    }
                }

                var textureBuffer = TextureUtility.GetTextureBufferCopy(_luminaManager, texturePath, xivTexture.TexturePath);
                xivTextureMap.Add(xivTexture.TextureUsageRaw, textureBuffer);
            }

            // reference for this fuckery
            // https://docs.google.com/spreadsheets/u/0/d/1kIKvVsW3fOnVeTi9iZlBDqJo6GWVn6K6BCUIRldEjhw/htmlview#
            var alphaMode = AlphaMode.MASK;
            var backfaceCulling = true;
            switch (xivMaterial.ShaderPack)
            {
                case "character.shpk":
                {
                    //alphaMode = SharpGLTF.Materials.AlphaMode.MASK;
                    // for character gear, split the normal map into diffuse, specular and emission
                    if (xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal))
                    {
                        xivTextureMap.TryGetValue(TextureUsage.SamplerDiffuse, out var initDiffuse);
                        if (!xivTextureMap.ContainsKey(TextureUsage.SamplerDiffuse) ||
                            !xivTextureMap.ContainsKey(TextureUsage.SamplerSpecular) ||
                            !xivTextureMap.ContainsKey(TextureUsage.SamplerReflection))
                        {
                            var normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
                                ImageLockMode.ReadWrite, normal.PixelFormat);
                            var initDiffuseData = initDiffuse?.LockBits(
                                new Rectangle(0, 0, initDiffuse.Width, initDiffuse.Height), ImageLockMode.ReadWrite,
                                initDiffuse.PixelFormat);

                            try
                            {
                                var characterTextures =
                                    TextureUtility.ComputeCharacterModelTextures(xivMaterial, normalData,
                                        initDiffuseData);

                                // If the textures already exist, tryAdd will make sure they are not overwritten
                                foreach (var (usage, texture) in characterTextures)
                                {
                                    xivTextureMap.TryAdd(usage, texture);
                                }
                            }
                            finally
                            {
                                normal.UnlockBits(normalData);
                                if (initDiffuse != null && initDiffuseData != null)
                                {
                                    initDiffuse.UnlockBits(initDiffuseData);
                                }
                            }
                        }
                    }

                    if (xivTextureMap.TryGetValue(TextureUsage.SamplerMask, out var mask) &&
                        xivTextureMap.TryGetValue(TextureUsage.SamplerSpecular, out var specularMap))
                    {
                        var maskData = mask.LockBits(new Rectangle(0, 0, mask.Width, mask.Height),
                            ImageLockMode.ReadWrite, mask.PixelFormat);
                        var specularMapData = specularMap.LockBits(
                            new Rectangle(0, 0, specularMap.Width, specularMap.Height), ImageLockMode.ReadWrite,
                            specularMap.PixelFormat);
                        var occlusion = TextureUtility.ComputeOcclusion(maskData, specularMapData);
                        mask.UnlockBits(maskData);
                        specularMap.UnlockBits(specularMapData);

                        // Add the specular occlusion texture to xivTextureMap
                        xivTextureMap.Add(TextureUsage.SamplerWaveMap, occlusion);
                    }

                    break;
                }
                case "skin.shpk":
                {
                    alphaMode = AlphaMode.MASK;
                    if (xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal))
                    {
                        xivTextureMap.TryGetValue(TextureUsage.SamplerDiffuse, out var diffuse);

                        if (diffuse == null) throw new Exception("Diffuse texture is null");

                        var normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
                            ImageLockMode.ReadWrite, normal.PixelFormat);
                        var diffuseData = diffuse.LockBits(new Rectangle(0, 0, diffuse.Width, diffuse.Height),
                            ImageLockMode.ReadWrite, diffuse.PixelFormat);
                        try
                        {
                            // use blue for opacity
                            TextureUtility.CopyNormalBlueChannelToDiffuseAlphaChannel(normalData, diffuseData);

                            for (var x = 0; x < normal.Width; x++)
                            {
                                for (var y = 0; y < normal.Height; y++)
                                {
                                    var normalPixel = TextureUtility.GetPixel(normalData, x, y, _log);
                                    TextureUtility.SetPixel(normalData, x, y,
                                        Color.FromArgb(normalPixel.B, normalPixel.R, normalPixel.G, 255));
                                }
                            }
                        }
                        finally
                        {
                            normal.UnlockBits(normalData);
                            diffuse.UnlockBits(diffuseData);
                        }
                    }

                    break;
                }
                case "hair.shpk":
                {
                    alphaMode = AlphaMode.MASK;
                    backfaceCulling = false;
                    if (xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal))
                    {
                        var specular = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
                        var colorSetInfo = xivMaterial.File!.ColorSetInfo;

                        var normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
                            ImageLockMode.ReadWrite, normal.PixelFormat);
                        try
                        {
                            for (int x = 0; x < normalData.Width; x++)
                            {
                                for (int y = 0; y < normalData.Height; y++)
                                {
                                    var normalPixel = TextureUtility.GetPixel(normalData, x, y, _log);
                                    var colorSetIndex1 = normalPixel.A / 17 * 16;
                                    var colorSetBlend = normalPixel.A % 17 / 17.0;
                                    var colorSetIndexT2 = normalPixel.A / 17;
                                    var colorSetIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

                                    var specularBlendColour = ColorUtility.BlendColorSet(in colorSetInfo,
                                        colorSetIndex1, colorSetIndex2, 255, colorSetBlend,
                                        ColorUtility.TextureType.Specular);

                                    // Use normal blue channel for opacity
                                    specular.SetPixel(x, y, Color.FromArgb(
                                        normalPixel.A,
                                        specularBlendColour.R,
                                        specularBlendColour.G,
                                        specularBlendColour.B
                                    ));
                                }
                            }
                        }
                        finally
                        {
                            normal.UnlockBits(normalData);
                        }

                        xivTextureMap.Add(TextureUsage.SamplerSpecular, specular);

                        if (xivTextureMap.TryGetValue(TextureUsage.SamplerMask, out var mask) &&
                            xivTextureMap.TryGetValue(TextureUsage.SamplerSpecular, out var specularMap))
                        {
                            // TODO: Diffuse is to be generated using character options for colors
                            // Currently based on the mask it seems I am blending it in a weird way
                            var diffuse = (Bitmap) mask.Clone();
                            var specularScaleX = specularMap.Width / (float) diffuse.Width;
                            var specularScaleY = specularMap.Height / (float) diffuse.Height;

                            var normalScaleX = normal.Width / (float) diffuse.Width;
                            var normalScaleY = normal.Height / (float) diffuse.Height;

                            var maskData = mask.LockBits(new Rectangle(0, 0, mask.Width, mask.Height),
                                ImageLockMode.ReadWrite, mask.PixelFormat);
                            var specularMapData = specularMap.LockBits(
                                new Rectangle(0, 0, specularMap.Width, specularMap.Height), ImageLockMode.ReadWrite,
                                specularMap.PixelFormat);
                            var diffuseData = diffuse.LockBits(new Rectangle(0, 0, diffuse.Width, diffuse.Height),
                                ImageLockMode.ReadWrite, diffuse.PixelFormat);
                            normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
                                ImageLockMode.ReadWrite, normal.PixelFormat);
                            try
                            {
                                for (var x = 0; x < maskData.Width; x++)
                                {
                                    for (var y = 0; y < maskData.Height; y++)
                                    {
                                        var maskPixel = TextureUtility.GetPixel(maskData, x, y, _log);
                                        var specularPixel = TextureUtility.GetPixel(specularMapData,
                                            (int) (x * specularScaleX), (int) (y * specularScaleY), _log);
                                        var normalPixel = TextureUtility.GetPixel(normalData, (int) (x * normalScaleX),
                                            (int) (y * normalScaleY), _log);

                                        TextureUtility.SetPixel(maskData, x, y, Color.FromArgb(
                                            normalPixel.A,
                                            Convert.ToInt32(specularPixel.R * Math.Pow(maskPixel.G / 255.0, 2)),
                                            Convert.ToInt32(specularPixel.G * Math.Pow(maskPixel.G / 255.0, 2)),
                                            Convert.ToInt32(specularPixel.B * Math.Pow(maskPixel.G / 255.0, 2))
                                        ));

                                        // Copy alpha channel from normal to diffuse
                                        // var diffusePixel = TextureUtility.GetPixel( diffuseData, x, y, _log );

                                        // TODO: Blending using mask
                                        TextureUtility.SetPixel(diffuseData, x, y, Color.FromArgb(
                                            normalPixel.A,
                                            255,
                                            255,
                                            255
                                        ));
                                    }
                                }

                                diffuse.UnlockBits(diffuseData);
                                // Add the specular occlusion texture to xivTextureMap
                                xivTextureMap.Add(TextureUsage.SamplerDiffuse, diffuse);
                            }
                            finally
                            {
                                mask.UnlockBits(maskData);
                                specularMap.UnlockBits(specularMapData);
                                normal.UnlockBits(normalData);
                            }
                        }
                    }

                    break;
                }
                case "iris.shpk":
                {
                    if (xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal))
                    {
                        var specular = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
                        var colorSetInfo = xivMaterial.File!.ColorSetInfo;

                        var normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
                            ImageLockMode.ReadWrite, normal.PixelFormat);
                        try
                        {
                            for (int x = 0; x < normal.Width; x++)
                            {
                                for (int y = 0; y < normal.Height; y++)
                                {
                                    //var normalPixel = normal.GetPixel( x, y );
                                    var normalPixel = TextureUtility.GetPixel(normalData, x, y, _log);
                                    var colorSetIndex1 = normalPixel.A / 17 * 16;
                                    var colorSetBlend = normalPixel.A % 17 / 17.0;
                                    var colorSetIndexT2 = normalPixel.A / 17;
                                    var colorSetIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

                                    var specularBlendColour = ColorUtility.BlendColorSet(in colorSetInfo,
                                        colorSetIndex1, colorSetIndex2, 255, colorSetBlend,
                                        ColorUtility.TextureType.Specular);

                                    specular.SetPixel(x, y,
                                        Color.FromArgb(255, specularBlendColour.R, specularBlendColour.G,
                                            specularBlendColour.B));
                                }
                            }

                            xivTextureMap.Add(TextureUsage.SamplerSpecular, specular);
                        }
                        finally
                        {
                            normal.UnlockBits(normalData);
                        }
                    }

                    break;
                }
                default:
                    _log.Debug($"Unhandled shader pack {xivMaterial.ShaderPack}");
                    break;
            }

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
}