using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using Meddle.Plugin.Files;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Customize;
using Meddle.Plugin.Models.ResourceTree;
using Meddle.Plugin.Utility;
using Penumbra.Api.Enums;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Files;
using SharpGLTF.IO;
using SharpGLTF.Scenes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Material = Meddle.Plugin.Models.Material;

namespace Meddle.Plugin.Services;

public partial class ExportService : IService
{
    private readonly IPluginLog _logger;
    private readonly FileService _fileService;
    private readonly SklbService _sklbService;
    private readonly ImageService _imageService;
    private readonly ModelService _modelService;
    private readonly IFramework _framework;
    public bool IsRunning { get; private set; }

    public ExportService(IPluginLog logger, FileService fileService, SklbService sklbService, ImageService imageService, ModelService modelService, IFramework framework)
    {
        _logger = logger;
        _fileService = fileService;
        _sklbService = sklbService;
        _imageService = imageService;
        _modelService = modelService;
        _framework = framework;
    }

    [GeneratedRegex("^chara/human/c\\d+/obj/body/b0003/model/c\\d+b0003_top.mdl$")]
    private static partial Regex LowPolyModelRegex();

    public async Task Execute(ExportConfig config,
        ResourceTree.ResourceNode[] nodes,
        IReadOnlyDictionary<string, MtrlFile.ColorTable> colorTables, GenderRace raceCode,
        string outputPath,
        CancellationToken cancel)
    {
        if (IsRunning)
            throw new Exception("Export already running.");
        try
        {
            IsRunning = true;
            
            // Resolve all skeletons for the models
            var sklbPaths = nodes.Where(x => x.Type == ResourceType.Sklb)
                .Select(x => x.ActualPath)
                .ToArray();
            var skeletons = await _sklbService.BuildSkeletonsAsync(sklbPaths, cancel);
            var skeleton = new GltfSkeleton(skeletons);

            await _framework.RunOnTick(
                () =>
                {
                    return Task.Run(
                        async () =>
                        {
                            await ExecuteExport(config, nodes, colorTables, raceCode, outputPath, skeleton, cancel);
                        }, cancel);
                }, cancellationToken: cancel);
        }
        catch (Exception e)
        {
            _logger.Error($"Error exporting models:\n{e}");
        }
        finally
        {
            IsRunning = false;
        }
    }
    
    private async Task ExecuteExport(ExportConfig config, 
        ResourceTree.ResourceNode[] nodes,
        IReadOnlyDictionary<string, MtrlFile.ColorTable> colorTables, GenderRace raceCode, 
        string outputPath,
        GltfSkeleton skeleton,
        CancellationToken cancel)
    {
        var lowPolyModelRegex = LowPolyModelRegex();
        var models = nodes
            .Where(x => x.Type is ResourceType.Mdl)
            .Where(x => x.GamePath != null)
            .Where(x => !lowPolyModelRegex.IsMatch(x.GamePath!))
            .ToArray();

        var pbdFileData = _fileService.ReadFile("chara/xls/boneDeformer/human.pbd");
        if (pbdFileData == null)
        {
            _logger.Error("Could not find bone deformer file.");
            return;
        }

        var pbdFile = new PbdFile(pbdFileData);
        var scene = new SceneBuilder();
        scene.AddNode(skeleton.Root);

        foreach (var node in models)
        {
            try
            {
                _logger.Information($"Exporting {node.ActualPath}.");
                var actualPath = node.ActualPath;
                var gamePath = node.GamePath ?? throw new Exception("Game path is null.");
                var mdlBytes = _fileService.ReadFile(actualPath);
                if (mdlBytes == null)
                {
                    _logger.Error($"Could not find file {actualPath}.");
                    continue;
                }

                var mdlFile = new MdlFile(mdlBytes);

                var materials = CreateMaterials(config, node.Children, mdlFile, colorTables)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);

                // Build deform from the shared model to the requested race code
                var fromDeform = RaceDeformer.RaceCodeFromPath(gamePath);
                RaceDeformer? raceDeformer = null;
                if (fromDeform != null)
                {
                    _logger.Information($"Setup deform for {actualPath} From {fromDeform} To {(ushort) raceCode}.");
                    raceDeformer = new RaceDeformer(pbdFile, skeleton, fromDeform.Value, (ushort) raceCode);
                }

                var model = _modelService.Export(config, mdlFile, skeleton, materials, raceDeformer);
                
                AddMeshesToScene(scene, model, skeleton);
            }
            catch (Exception e)
            {
                _logger.Error($"Error exporting {node.ActualPath}:\n{e}");
            }
        }
        
        var gltfModel = scene.ToGltf2();
        gltfModel.SaveGLTF(outputPath);
        _logger.Information($"Exported to {outputPath}.");

        var folder = Path.GetDirectoryName(outputPath)!;
        Process.Start("explorer.exe", folder);
    }

    private static void AddMeshesToScene(SceneBuilder scene, Model model, GltfSkeleton skeleton)
    {
        foreach (var mesh in model.Meshes)
        {
            foreach (var data in mesh.Meshes)
            {
                var extras = new Dictionary<string, object>(data.Attributes.Length);
                foreach (var attribute in data.Attributes)
                    extras.Add(attribute, true);

                if (extras.ContainsKey("atr_eye_a"))
                {
                    // reaper eye on player models. we don't want them
                    continue;
                }

                // Use common skeleton here since we're exporting the full model
                var instance = mesh.Skeleton != null
                    ? scene.AddSkinnedMesh(data.Mesh, Matrix4x4.Identity, [.. skeleton.Joints])
                    : scene.AddRigidMesh(data.Mesh, Matrix4x4.Identity);

                instance.WithExtras(JsonNode.Parse(JsonSerializer.Serialize(extras)));
            }
        }
    }

    /*private HashSet<XivSkeleton> ProcessSkeletons(IEnumerable<Ipc.ResourceNode> models,
        CancellationToken cancel)
    {
        var skeletons = new HashSet<XivSkeleton>();
        foreach (var node in models)
        {
            var nodeSkeletons = manager.ResolveSklbsForMdl(node.GamePath.ToString(),
                GetEstManipulationsForPath(node.GamePath, activeCollections));
            var xivSkeletons = Util.BuildSkeletons(nodeSkeletons, manager._framework, read, cancel).ToArray();

            // filter out duplicate skeletons
            foreach (var skeleton in xivSkeletons)
            {
                if (skeletons.Any(x => x.Equals(skeleton))) continue;
                skeletons.Add(skeleton);
            }
        }

        return skeletons;
    }*/

    private Dictionary<string, Material> CreateMaterials(
        ExportConfig modelExportConfig,
        IEnumerable<ResourceTree.ResourceNode> mtrlNodes, 
        MdlFile mdlFile,
        IReadOnlyDictionary<string, MtrlFile.ColorTable> colorTables)
    {
        var materials = new Dictionary<string, Material>();
        foreach (var mtrlNode in mtrlNodes)
        {
            if (mtrlNode.Type != ResourceType.Mtrl)
                continue;

            var mtrlFullPath = mtrlNode.ActualPath;
            var nodeGamePath = mtrlNode.GamePath;
            if (nodeGamePath == null)
                continue;

            var bytes = _fileService.ReadFile(mtrlFullPath);
            if (bytes == null)
                continue;

            var mtrl = new MtrlFile(bytes);

            var colorTable = colorTables.TryGetValue(nodeGamePath, out var table) ? table : mtrl.Table;
            mtrl.Table = colorTable;


            /*
             
             var textures = mtrl.ShaderPackage.Samplers.ToDictionary(
                sampler => (TextureUsage) sampler.SamplerId,
                sampler => _imageService.ConvertImage(mtrl.Textures[sampler.TextureIndex])
             );
             */
            // workaround for nonstandard paths which don't resolve well from active collection
            var nodeTextures = mtrlNode.Children.Where(x => x.Type == ResourceType.Tex).ToArray();
            var textures = new Dictionary<TextureUsage, Image<Rgba32>>();
            foreach (var sampler in mtrl.ShaderPackage.Samplers)
            {
                var id = (TextureUsage) sampler.SamplerId;
                var textureIndex = sampler.TextureIndex;

                var simpleTextureMatch = nodeTextures.FirstOrDefault(x => string.Equals(x.Name, $"g_{id}", StringComparison.OrdinalIgnoreCase));
                if (simpleTextureMatch == null)
                {
                    textures[id] = _imageService.ConvertImage(mtrl.Textures[textureIndex]);
                }
                else
                {
                    _logger.Information($"Matched {id} to {simpleTextureMatch.ActualPath}.");
                    textures[id] =
                        _imageService.ConvertImage(mtrl.Textures[textureIndex], simpleTextureMatch.ActualPath);
                }
            }
            

            var baseMaterial = new Material(mtrl, textures);

            var parameters = modelExportConfig.Customize?.Parameters?.WithDefault() ?? Parameters.Default();
            
            var material = mtrl.ShaderPackage.Name switch
            {
                // we do a little ternary and null coalescing sorry future reader
                "hair.shpk" => new HairMaterial(baseMaterial, 
                    parameters.HairDiffuse!.ToVector4(), 
                    parameters.HairHighlight!.ToVector4()),
                "iris.shpk" => new IrisMaterial(baseMaterial, 
                    parameters.LeftEye!.ToVector4(), 
                    parameters.RightEye!.ToVector4()
                    ),
                "skin.shpk" => new SkinMaterial(baseMaterial, 
                    parameters.SkinDiffuse!.ToVector4(), 
                    parameters.LipDiffuse!.ToVector4()
                    ),
                "character.shpk" => new CharacterMaterial(baseMaterial),
                "characterglass.shpk" => new CharacterGlassMaterial(baseMaterial),
                _ => baseMaterial
            };

            // since the race may be different, find most similar by comparing the gamepath of the node
            // ie. au ra skin textures on a shirt with a hyur base
            var mostSimilar = mdlFile.Materials
                .Select(modelDefaultGamePath =>
                    (modelDefaultGamePath: modelDefaultGamePath, similarity: StringUtility.ComputeLd(modelDefaultGamePath, nodeGamePath)))
                .MinBy(pair => pair.similarity)
                .modelDefaultGamePath;

            _logger.Information($"Matched\nFull:{mtrlFullPath}\nGamePath:{nodeGamePath}\nSimilar:{mostSimilar}.\n{string.Join("\n", mtrl.Textures.Select(x => x.Path))}");
            
            materials[mostSimilar] = material;
        }

        return materials;
    }
}