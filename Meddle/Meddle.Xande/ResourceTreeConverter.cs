using System.Drawing;
using System.Numerics;
using System.Text.RegularExpressions;
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
using Xande;
using Xande.Files;
using Xande.Havok;
using Xande.Models.Export;

namespace Meddle.Xande;

public class ResourceTreeConverter
{
    private readonly HavokConverter _converter;
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly LuminaManager _luminaManager;

    private readonly List<Regex> _skipMeshRegexes = new()
    {
        new Regex(@"^/mt_c\d+f.+_etc_b.mtrl$")
    };

    private readonly List<Regex> _skipModelRegexes = new()
    {
        new Regex(@"^chara/human/c\d+/obj/body/b0003/model/c\d+b0003_top.mdl$")
    };

    public ResourceTreeConverter(HavokConverter converter,
        LuminaManager luminaManager,
        IPluginLog log,
        IFramework framework)
    {
        _converter = converter;
        _luminaManager = luminaManager;
        _log = log;
        _framework = framework;
    }

    private static List<TrimmedResourceNode> GetSortedSkeletons(IEnumerable<TrimmedResourceNode> nodes)
    {
        var skeletonNodes = nodes.Where(x => x.Type == ResourceType.Sklb).ToList();
        skeletonNodes.Sort((x, y) =>
        {
            if (x.GamePath == null) return 1;

            if (y.GamePath == null) return -1;

            if (x.GamePath.Contains("weapon")) return 1;

            if (y.GamePath.Contains("weapon")) return -1;

            return 0;
        });

        return skeletonNodes;
    }

    private bool TryGetSkeleton(string path, out HavokXml? havokXml)
    {
        path = path.Replace("\\", "/");
        try
        {
            var file = _luminaManager.GetFile<FileResource>(path);
            if (file == null)
            {
                havokXml = null;
                return false;
            }

            var sklb = SklbFile.FromStream(file.Reader.BaseStream);
            var xml = _converter.HkxToXml(sklb.HkxData);
            havokXml = new HavokXml(xml);
            _log.Debug($"Loaded skeleton {path}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Failed to load {path}");
            havokXml = null;
            return false;
        }
    }

    private List<HavokXml>? GetSkeletons(IEnumerable<TrimmedResourceNode> nodes)
    {
        var skeletonNodes = GetSortedSkeletons(nodes);
        var skeletons = new List<HavokXml>();

        try
        {
            foreach (var node in skeletonNodes)
            {
                var nodePath = node.ActualPath;
                HavokXml? havokXml;
                if (TryGetSkeleton(nodePath, out havokXml))
                {
                    skeletons.Add(havokXml!);
                    continue;
                }

                _log.Error($"Failed to load {nodePath}, falling back to GamePath");
                nodePath = node.GamePath;
                if (nodePath != null && TryGetSkeleton(nodePath, out havokXml))
                {
                    skeletons.Add(havokXml!);
                    continue;
                }

                _log.Error($"Failed to load {nodePath}, skipping");
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error loading skeletons");
            return null;
        }

        return skeletons;
    }

    private IEnumerable<(TrimmedResourceNode, Model)> GetModels(IEnumerable<TrimmedResourceNode> nodes,
        ushort? raceCode)
    {
        var modelNodes = nodes.Where(x => x.Type == ResourceType.Mdl).ToList();

        var models = new List<(TrimmedResourceNode, Model)>();
        foreach (var modelNode in modelNodes)
        {
            if (!_luminaManager.TryGetModel(modelNode.ActualPath, modelNode.GamePath!, raceCode,
                    out var path, out var model))
            {
                _log.Error($"Failed to load {modelNode.ActualPath}, skipping");
                continue;
            }

            if (model == null)
            {
                _log.Error($"Failed to load {modelNode.ActualPath}, skipping");
                continue;
            }

            _log.Debug($"Loaded model {path}");
            models.Add((modelNode, model));
        }

        return models;
    }

    private static List<Ipc.ResourceNode> FlattenTree(Ipc.ResourceTree tree)
    {
        var nodes = new List<Ipc.ResourceNode>();
        foreach (var node in tree.Nodes) nodes.AddRange(FlattenNode(node));

        return nodes;

        IEnumerable<Ipc.ResourceNode> FlattenNode(Ipc.ResourceNode node)
        {
            var flattenNode = new List<Ipc.ResourceNode> {node};
            foreach (var child in node.Children) flattenNode.AddRange(FlattenNode(child));

            return flattenNode;
        }
    }

    public Task ExportResourceTree(ExportRequest exportRequest)
    {
        return _framework.RunOnTick(() => ExportResourceTreeInternal(exportRequest));
    }

    private async Task ExportResourceTreeWithSkeletonsAsync(ExportRequest exportRequest,
        IEnumerable<HavokXml> skeletons,
        List<TrimmedResourceNode> resources)
    {
        IEnumerable<(TrimmedResourceNode node, Model model)> models = GetModels(resources, exportRequest.Tree.RaceCode);
        IEnumerable<(TrimmedResourceNode node, Material material)> materials = GetMaterials(resources);

        var deform = exportRequest.Tree.RaceCode;
        var glTfScene = new SceneBuilder("scene");
        var boneMap = ModelUtility.GetBoneMap(skeletons.ToArray(), out var root);
        var joints = boneMap.Values.ToArray();
        var raceDeformer = new RaceDeformer(_luminaManager.GetPbdFile(), boneMap);
        if (root != null) glTfScene.AddNode(root);

        List<(TrimmedResourceNode materialNode, Material material, MaterialBuilder builder)> materialBuilders = new();
        await Parallel.ForEachAsync(materials, new ParallelOptions(), async (material, ct) =>
        {
            try
            {
                var result = await ProcessMaterial(material.node, material.material,
                    exportRequest.ColorSetInfos?.GetValueOrDefault(material.node.GamePath ?? material.node.ActualPath));
                materialBuilders.Add(result);
            }
            catch (Exception e)
            {
                _log.Error(e, $"Error processing material {material.node.ActualPath}");
            }
        });

        foreach (var model in models)
        {
            if (_skipModelRegexes.Any(x => x.IsMatch(model.node.ActualPath)))
            {
                _log.Debug($"Skipping model {model.node.ActualPath}");
                continue;
            }

            _log.Debug($"Handling model {model.node.ActualPath}");
            var relevantMaterials = materialBuilders
                .Where(x => model.node.Children.Any(y => y.ActualPath == x.materialNode.ActualPath))
                .ToList();

            var meshMaterialMappings = MapMeshesToMaterials(model.node, model.model, relevantMaterials);

            ushort? raceCode = null;
            try
            {
                raceCode = raceDeformer.RaceCodeFromPath(model.node.GamePath);
            }
            catch (Exception e)
            {
                _log.Error(e, $"Failed to parse race code from path {model.node.GamePath}");
            }

            foreach (var (mesh, group) in meshMaterialMappings)
                try
                {
                    HandleMesh(mesh, boneMap, joints, glTfScene, model.model, group, raceDeformer, raceCode, deform);
                }
                catch (Exception e)
                {
                    _log.Error(e, $"Failed to handle mesh {mesh.Material.MaterialPath}");
                }
        }

        var gltf = glTfScene.ToGltf2();
        var gltfPath = Path.Combine(exportRequest.ExportPath, exportRequest.Tree.Name);
        if (!Directory.Exists(gltfPath)) Directory.CreateDirectory(gltfPath);
        var gltfFile = Path.Combine(gltfPath, "model.gltf");
        gltf.SaveGLTF(gltfFile);
    }

    private void HandleMesh(Mesh mesh,
        IReadOnlyDictionary<string, NodeBuilder> boneMap,
        NodeBuilder[] joints,
        SceneBuilder glTfScene,
        Model model,
        (TrimmedResourceNode node, Material material, MaterialBuilder builder) group,
        RaceDeformer raceDeformer,
        ushort? raceCode,
        ushort deform)
    {
        var name = Path.GetFileNameWithoutExtension(group.node.GamePath ?? group.node.ActualPath);
        var boneSet = mesh.BoneTable();
        var boneSetJoints = boneSet?.Select(n =>
        {
            if (boneMap.TryGetValue(n, out var node)) return node;

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
            mesh,
            useSkinning,
            jointIdMapping,
            group.builder,
            raceDeformer
        );

        // Deform for full bodies
        if (raceCode != null && deform != 0)
        {
            _log.Debug($"Setting up deform steps for {name}");
            meshBuilder.SetupDeformSteps(raceCode.Value, deform);
        }

        meshBuilder.BuildVertices();

        if (mesh.Submeshes.Length > 0)
        {
            for (var i = 0; i < mesh.Submeshes.Length; i++)
                try
                {
                    var xivSubmesh = mesh.Submeshes[i];
                    var subMesh = meshBuilder.BuildSubmesh(xivSubmesh);
                    subMesh.Name = $"{name}_{mesh.MeshIndex}.{i}";
                    meshBuilder.BuildShapes(model.Shapes.Values.ToArray(), subMesh, (int) xivSubmesh.IndexOffset,
                        (int) (xivSubmesh.IndexOffset + xivSubmesh.IndexNum));

                    if (!NodeBuilder.IsValidArmature(joints))
                    {
                        _log.Warning($"Joints are not valid, skipping submesh {i} for {name}, " +
                                     $"{string.Join(", ", joints.Select(x => x.Name))}");
                        continue;
                    }

                    if (useSkinning)
                        glTfScene.AddSkinnedMesh(subMesh, Matrix4x4.Identity, joints);
                    else
                        glTfScene.AddRigidMesh(subMesh, Matrix4x4.Identity);
                }
                catch (Exception e)
                {
                    _log.Error(e, $"Failed to build submesh {i} for {name}");
                }
        }
        else
        {
            var msh = meshBuilder.BuildMesh();
            msh.Name = $"{name}_{mesh.MeshIndex}";
            _log.Debug($"Building mesh: \"{msh.Name}\"");
            meshBuilder.BuildShapes(model.Shapes.Values.ToArray(), msh, 0, mesh.Indices.Length);
            if (useSkinning)
                glTfScene.AddSkinnedMesh(msh, Matrix4x4.Identity, joints);
            else
                glTfScene.AddRigidMesh(msh, Matrix4x4.Identity);
        }
    }

    private Task ExportResourceTreeInternal(ExportRequest exportRequest)
    {
        var trimmedResources = TrimResourceTree(exportRequest.Tree);
        var skeletons = GetSkeletons(trimmedResources);
        if (skeletons == null || skeletons.Count == 0)
        {
            _log.Error("Failed to load skeletons");
            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            try
            {
                await ExportResourceTreeWithSkeletonsAsync(exportRequest, skeletons, trimmedResources);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error exporting resource tree");
            }
        });
    }

    private async Task<(TrimmedResourceNode materialNode, Material material, MaterialBuilder builder)> ProcessMaterial(
        TrimmedResourceNode materialNode, Material material, ColorSetInfo? colorSetInfoOverride)
    {
        var textureMap = new Dictionary<TextureUsage, Bitmap>();

        foreach (var texture in material.Textures)
        {
            if (texture.TexturePath == "dummy.tex") continue;

            var dummyRegex = new Regex(@"^.+/dummy_?.+?\.tex$");
            if (dummyRegex.IsMatch(texture.TexturePath)) continue;

            var texturePath = texture.TexturePath.Replace("\\", "/");
            // get most similar nodeTexturePath
            var nodeTexturePath =
                materialNode.Children.FirstOrDefault(x => x.ActualPath == texturePath || x.GamePath == texturePath);
            if (nodeTexturePath?.ActualPath == null)
            {
                // compute LD on paths and use closest
                // There are some cases for things like bibo where the name is not identical but is close.
                // NOTE: need to ensure there are no issues where the suffix is closer to a different material type
                var distances = materialNode.Children.Select(x => new
                {
                    node = x,
                    actualDist = x.ActualPath.ComputeLd(texturePath),
                    gameDist = x.GamePath?.ComputeLd(texturePath) ?? int.MaxValue
                }).ToList();

                var closest = distances.MinBy(x => x.gameDist)!;
                _log.Debug($"Loaded texture\n" +
                           $"Act:  {closest.node.ActualPath}\n" +
                           $"Game: {closest.node.GamePath}\n" +
                           $"Text: {texturePath}");
                textureMap[texture.TextureUsageRaw] = TextureUtility.GetTextureBufferCopy(_luminaManager,
                    closest.node.ActualPath.Replace("\\", "/"), texturePath);
            }
            else
            {
                _log.Debug($"Loaded texture\n" +
                           $"Act:  {nodeTexturePath.ActualPath}\n" +
                           $"Game: {nodeTexturePath.GamePath}\n" +
                           $"Text: {texturePath}");
                try
                {
                    textureMap[texture.TextureUsageRaw] = TextureUtility.GetTextureBufferCopy(_luminaManager,
                        nodeTexturePath.ActualPath.Replace("\\", "/"), texturePath);
                }
                catch (Exception e)
                {
                    _log.Error(e, $"Failed to load texture {nodeTexturePath.ActualPath}");
                }
            }
        }

        var alphaMode = AlphaMode.MASK;
        var backfaceCulling = true;
        switch (material.ShaderPack)
        {
            case "character.shpk":
            {
                // not sure if backface culling should be done here, depends on model ugh
                backfaceCulling = false;
                TextureUtility.ParseCharacterTextures(textureMap, material, colorSetInfoOverride, _log);
                break;
            }
            case "skin.shpk":
            {
                alphaMode = AlphaMode.MASK;
                TextureUtility.ParseSkinTextures(textureMap, material);
                break;
            }
            case "hair.shpk":
            {
                alphaMode = AlphaMode.MASK;
                backfaceCulling = false;
                TextureUtility.ParseHairTextures(textureMap, material);
                break;
            }
            case "iris.shpk":
            {
                TextureUtility.ParseIrisTextures(textureMap, material);
                break;
            }
            default:
                _log.Warning($"Unhandled shader pack {material.ShaderPack}");
                break;
        }

        var glTfMaterial = new MaterialBuilder
        {
            Name = material.File?.FilePath.Path,
            AlphaMode = alphaMode,
            DoubleSided = !backfaceCulling
        };

        var exportDir = Path.Combine(Path.GetTempPath(), "Meddle.Export");
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);
        var exportFolder = Path.Combine(exportDir, "Temp");
        if (!Directory.Exists(exportFolder)) Directory.CreateDirectory(exportFolder);

        await TextureUtility.ExportTextures(glTfMaterial, textureMap, exportFolder);
        return (materialNode, material, glTfMaterial);
    }

    private List<(Mesh mesh, (TrimmedResourceNode, Material, MaterialBuilder) group)> MapMeshesToMaterials(
        TrimmedResourceNode node, 
        Model model, 
        IReadOnlyCollection<(TrimmedResourceNode node, Material material, MaterialBuilder builder)> materials)
    {
        var meshes = model.Meshes.Where(x => x.Types.Contains(Mesh.MeshType.Main)).ToList();

        if (materials.Count == 0)
        {
            _log.Error($"No materials found for {node.ActualPath}");
            return new List<(Mesh mesh, (TrimmedResourceNode, Material, MaterialBuilder))>();
        }

        var meshMaterialMappings = new List<(Mesh mesh, (TrimmedResourceNode, Material, MaterialBuilder))>();
        foreach (var mesh in meshes)
        {
            if (_skipMeshRegexes.Any(x => x.IsMatch(mesh.Material.MaterialPath)))
            {
                _log.Debug($"Skipping mesh matching {mesh.Material.MaterialPath}");
                continue;
            }

            _log.Debug($"Handling mesh {mesh.Material.MaterialPath}");

            mesh.Material.Update(_luminaManager.GameData);

            var distances = materials.Select(x => new
            {
                group = x,
                actualDist = x.node.ActualPath.ComputeLd(mesh.Material.ResolvedPath!),
                gameDist = x.node.GamePath?.ComputeLd(mesh.Material.ResolvedPath!) ?? int.MaxValue
            }).ToList();

            var closestGame = distances.MinBy(x => x.gameDist);
            _log.Debug($"Using material {closestGame!.group.node.GamePath} for {mesh.Material.ResolvedPath}");
            meshMaterialMappings.Add((mesh, closestGame.group));
        }

        return meshMaterialMappings;
    }

    private IEnumerable<(TrimmedResourceNode, Material)> GetMaterials(IEnumerable<TrimmedResourceNode> resources)
    {
        var mtrlNodes = resources.Where(x => x.Type == ResourceType.Mtrl).ToList();
        var materials = new List<(TrimmedResourceNode, Material)>();

        foreach (var mtrlNode in mtrlNodes)
        {
            MtrlFile? file;
            if (Path.IsPathRooted(mtrlNode.ActualPath))
            {
                _log.Debug($"Loading material {mtrlNode.ActualPath} -> {mtrlNode.GamePath}");
                file = _luminaManager.GetFile<MtrlFile>(mtrlNode.ActualPath, mtrlNode.GamePath);
            }
            else
            {
                _log.Debug($"Loading material {mtrlNode.GamePath}");
                file = _luminaManager.GetFile<MtrlFile>(mtrlNode.GamePath!);
            }

            if (file == null)
            {
                _log.Error($"Failed to load {mtrlNode.ActualPath}");
                continue;
            }

            var material = new Material(file);
            materials.Add((mtrlNode, material));
            _log.Debug($"Loaded material {mtrlNode.ActualPath}");
        }

        return materials;
    }

    // Function to convert Ipc.ResourceTree to a trimmed version that can be serialized
    private List<TrimmedResourceNode> TrimResourceTree(Ipc.ResourceTree tree)
    {
        var nodes = FlattenTree(tree);
        return nodes.Select(node => new TrimmedResourceNode().FromResourceNode(node)).ToList();
    }

    public record TrimmedResourceNode
    {
        public ResourceType Type { get; private init; }

        public ChangedItemIcon Icon { get; private init; }

        public string? Name { get; private init; }

        public string? GamePath { get; private init; }

        public string ActualPath { get; private init; } = null!;

        public List<TrimmedResourceNode> Children { get; private init; } = null!;

        public int HashCode => (Type, Icon, Name, GamePath, ActualPath, Children).GetHashCode();

        public TrimmedResourceNode FromResourceNode(Ipc.ResourceNode node)
        {
            return new TrimmedResourceNode
            {
                Type = node.Type,
                Icon = node.Icon,
                Name = node.Name,
                GamePath = node.GamePath,
                ActualPath = node.ActualPath,
                Children = node.Children.Select(FromResourceNode).ToList()
            };
        }
    }

    public class ExportRequest
    {
        public ExportRequest(Ipc.ResourceTree tree, Dictionary<string, ColorSetInfo> colorSetInfos, string exportPath)
        {
            Tree = tree;
            ColorSetInfos = colorSetInfos;
            ExportPath = exportPath;
        }

        public Ipc.ResourceTree Tree { get; }

        // Mtrl -> ColorSetInfo
        public Dictionary<string, ColorSetInfo> ColorSetInfos { get; }

        public string ExportPath { get; }
    }
}