using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Meddle.Utils.Skeletons.Havok;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.UI.Windows.Views;

public class ExportView(SqPack pack, Configuration configuration, ImageHandler imageHandler, PathManager pathManager)
    : IView
{
    private record SklbGroup(SklbFile File, string Path);

    private readonly Dictionary<string, Model.MdlGroup> models = new();
    private readonly Dictionary<string, SklbGroup> skeletons = new();
    private Dictionary<string, IView> views = new();
    private Task LoadTask = Task.CompletedTask;
    private CancellationTokenSource cts = new();
    private Task ExportTask = Task.CompletedTask;

    private string input = "";

    private Dictionary<string, Model.MdlGroup> HandleMdls(string[] paths)
    {
        var mdlGroups = new Dictionary<string, Model.MdlGroup>();
        var mdlLines = paths
                       .Select(x => x.Trim())
                       .Where(x => x.EndsWith(".mdl")).ToList();
        foreach (var mdlPath in mdlLines)
        {
            var lookupResult = pack.GetFile(mdlPath);
            if (lookupResult == null)
            {
                continue;
            }

            var mdlFile = new MdlFile(lookupResult.Value.file.RawData);
            var mtrlGroups = new List<Material.MtrlGroup>();
            var mtrlNames = mdlFile.GetMaterialNames();
            foreach (var (offset, originalPath) in mtrlNames)
            {
                var mtrlPath = originalPath.StartsWith('/') ? PathUtil.Resolve(mdlPath, originalPath) : originalPath;
                var mtrlLookupResult = pack.GetFile(mtrlPath);
                if (mtrlLookupResult == null)
                {
                    // versioning
                    var prefix = mtrlPath[..mtrlPath.LastIndexOf('/')];
                    for (var j = 1; j < 9999; j++)
                    {
                        // 1 -> v0001
                        var versionedPath = $"{prefix}/v{j:D4}{originalPath}";
                        var versionedLookupResult = pack.GetFile(versionedPath);
                        if (versionedLookupResult != null)
                        {
                            mtrlLookupResult = versionedLookupResult;
                            break;
                        }
                    }

                    if (mtrlLookupResult == null)
                    {
                        continue;
                    }
                }

                var mtrlFile = new MtrlFile(mtrlLookupResult.Value.file.RawData);
                var shpkPath = $"shader/sm5/shpk/{mtrlFile.GetShaderPackageName()}";
                var shpkLookupResult = pack.GetFile(shpkPath);
                if (shpkLookupResult == null)
                {
                    continue;
                }

                var shpkFile = new ShpkFile(shpkLookupResult.Value.file.RawData);
                var textures = mtrlFile.GetTexturePaths();
                var texGroups = new List<Texture.TexGroup>();
                foreach (var (texOffset, texPath) in textures)
                {
                    var texLookupResult = pack.GetFile(texPath);
                    if (texLookupResult == null)
                    {
                        continue;
                    }

                    var texFile = new TexFile(texLookupResult.Value.file.RawData);
                    texGroups.Add(new Texture.TexGroup(texPath, texFile));
                }

                mtrlGroups.Add(new Material.MtrlGroup(mtrlPath, mtrlFile, shpkPath, shpkFile, texGroups.ToArray()));
            }

            mdlGroups[mdlPath] = new Model.MdlGroup(mdlPath, mdlFile, mtrlGroups.ToArray());
        }

        return mdlGroups;
    }

    private Dictionary<string, SklbGroup> HandleSklbs(string[] lines)
    {
        var sklbGroups = new Dictionary<string, SklbGroup>();
        var mdlLines = lines.Select(x => x.Trim()).Where(x => x.EndsWith(".sklb")).ToList();
        foreach (var sklbPath in mdlLines)
        {
            var lookupResult = pack.GetFile(sklbPath);
            if (lookupResult == null)
            {
                continue;
            }

            var sklbFile = new SklbFile(lookupResult.Value.file.RawData);
            sklbGroups[sklbPath] = new SklbGroup(sklbFile, sklbPath);
        }

        return sklbGroups;
    }

    public void Draw()
    {
        if (ImGui.InputTextMultiline("##Input", ref input, 1000, new Vector2(500, 500)))
        {
            LoadTask = Task.Run(() =>
            {
                models.Clear();
                skeletons.Clear();
                var newModels = HandleMdls(input.Split("\n"));
                foreach (var (key, value) in newModels)
                {
                    models[key] = value;
                }

                var newSkeletons = HandleSklbs(input.Split("\n"));
                foreach (var (key, value) in newSkeletons)
                {
                    skeletons[key] = value;
                }
            });
        }

        if (!LoadTask.IsCompleted)
        {
            ImGui.Text("Loading...");
            return;
        }

        if (LoadTask.IsFaulted)
        {
            var ex = LoadTask.Exception;
            ImGui.Text($"Error: {ex}");
            return;
        }

        ImGui.SeparatorText("Models");
        foreach (var (path, mdlGroup) in models)
        {
            if (ImGui.CollapsingHeader(path))
            {
                ImGui.Indent();
                try
                {
                    ImGui.SeparatorText("Materials");
                    foreach (var mtrlGroup in mdlGroup.MtrlFiles)
                    {
                        if (ImGui.CollapsingHeader(mtrlGroup.Path))
                        {
                            if (!views.TryGetValue(mtrlGroup.Path, out var mtrlView))
                            {
                                mtrlView = new MtrlView(mtrlGroup.MtrlFile, pack, imageHandler);
                                views[mtrlGroup.Path] = mtrlView;
                            }

                            mtrlView.Draw();

                            ImGui.SeparatorText("Textures");
                            ImGui.Indent();
                            try
                            {
                                foreach (var texGroup in mtrlGroup.TexFiles)
                                {
                                    if (ImGui.CollapsingHeader(texGroup.Path))
                                    {
                                        if (!views.TryGetValue(texGroup.Path, out var texView))
                                        {
                                            texView = new TexView(texGroup.TexFile, imageHandler,
                                                                  texGroup.Path);
                                            views[texGroup.Path] = texView;
                                        }

                                        texView.Draw();
                                    }
                                }
                            } finally
                            {
                                ImGui.Unindent();
                            }
                        }
                    }

                    ImGui.SeparatorText("Model Info");
                    if (!views.TryGetValue(path, out var mdlView))
                    {
                        views[path] = mdlView = new MdlView(mdlGroup.MdlFile, path);
                    }

                    mdlView.Draw();
                } finally
                {
                    ImGui.Unindent();
                }
            }
        }

        ImGui.SeparatorText("Skeletons");
        foreach (var (path, value) in skeletons)
        {
            if (ImGui.CollapsingHeader(path))
            {
                if (!views.TryGetValue(path, out var sklbView))
                {
                    sklbView = new SklbView(value.File, configuration);
                    views[path] = sklbView;
                }

                sklbView.Draw();
            }
        }

        ImGui.SeparatorText("Parameters");
        // TODO TODO TODO
        var customizeParameters = new CustomizeParameter();
        var customizeData = new CustomizeData();

        if (ImGui.Button("Export as GLTF"))
        {
            var sklbs = this.skeletons
                            .Select(x => (x.Key, views[x.Key] as SklbView))
                            .Select(x => (x.Item1, x.Item2!.Resolve().GetAwaiter().GetResult()))
                            .Select(x => (x.Item1, new HavokXml(x.Item2)))
                            .ToDictionary();
            cts?.Cancel();
            cts = new CancellationTokenSource();
            ExportTask = Task.Run(() => RunExport(models, sklbs, customizeParameters, customizeData, cts.Token), cts.Token);
        }

        if (ExportTask.IsFaulted)
        {
            ImGui.Text($"Error: {ExportTask.Exception?.Message}");
        }
        else if (!ExportTask.IsCompleted)
        {
            ImGui.Text("Exporting...");
            if (ImGui.Button("Cancel"))
            {
                cts.Cancel();
            }
        }
    }

    private void RunExport(
        Dictionary<string, Model.MdlGroup> modelDict, Dictionary<string, HavokXml> sklbDict,
        CustomizeParameter parameters, CustomizeData customizeData, CancellationToken token = default)
    {
        var scene = new SceneBuilder();
        var havokXmls = sklbDict.Values.ToArray();
        var bones = XmlUtils.GetBoneMap(havokXmls, out var root).ToArray();
        var boneNodes = bones.Cast<NodeBuilder>().ToArray();

        if (root != null)
        {
            scene.AddNode(root);
        }

        foreach (var (path, mdlGroup) in modelDict)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }
            var model = new Model(mdlGroup);

            var materials = new List<MaterialBuilder>();
            //foreach (var mtrlGroup in mdlGroup.Mtrls)
            Parallel.ForEach(model.Materials, material =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    if (material == null)
                    {
                        return;
                    }
                    //var builder = MaterialUtility.ParseMaterial(material);

                    var name =
                        $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_{Path.GetFileNameWithoutExtension(material.ShaderPackageName)}";

                    var builder = material.ShaderPackageName switch
                    {
                        "bg.shpk" => MaterialUtility.BuildBg(material, name),
                        "bgprop.shpk" => MaterialUtility.BuildBgProp(material, name),
                        "character.shpk" => MaterialUtility.BuildCharacter(material, name),
                        "characterocclusion.shpk" => MaterialUtility.BuildCharacterOcclusion(material, name),
                        "characterlegacy.shpk" => MaterialUtility.BuildCharacterLegacy(material, name),
                        //"charactertattoo.shpk" => MaterialUtility.BuildCharacterTattoo(material, name, @params),
                        "hair.shpk" => MaterialUtility.BuildHair(material, name, parameters, customizeData),
                        "skin.shpk" => MaterialUtility.BuildSkin(material, name, parameters, customizeData),
                        "iris.shpk" => MaterialUtility.BuildIris(material, name, parameters, customizeData),
                        _ => MaterialUtility.BuildFallback(material, name)
                    };

                    materials.Add(builder);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing material {material?.HandlePath}: {ex}");
                    throw;
                }
            });

            var meshes = ModelBuilder.BuildMeshes(model, materials, bones, null);
            foreach (var mesh in meshes)
            {
                if (mesh.UseSkinning && boneNodes.Length > 0)
                {
                    scene.AddSkinnedMesh(mesh.Mesh, Matrix4x4.Identity, boneNodes);
                }
                else
                {
                    scene.AddRigidMesh(mesh.Mesh, Matrix4x4.Identity);
                }
            }
        }

        var sceneGraph = scene.ToGltf2();

        var rootName = Path.GetFileNameWithoutExtension(modelDict.Keys.First());
        var outputPath = Path.Combine("output", rootName, "model.mdl");
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

        // save all raw textures
        foreach (var mdlGroup in modelDict)
        {
            foreach (var mtrlGroup in mdlGroup.Value.MtrlFiles)
            {
                foreach (var texGroup in mtrlGroup.TexFiles)
                {
                    var texPath = texGroup.Path;
                    var fileName = Path.GetFileName(texPath);
                    var texOutputPath = Path.Combine("output", rootName, "textures", fileName);
                    var texFolder = Path.GetDirectoryName(texOutputPath) ?? "output";
                    if (!Directory.Exists(texFolder))
                    {
                        Directory.CreateDirectory(texFolder);
                    }

                    var texture = new Texture(texGroup.TexFile, texPath, null, null, null);
                    var skTex = texture.ToTexture().Bitmap;

                    var str = new SKDynamicMemoryWStream();
                    skTex.Encode(str, SKEncodedImageFormat.Png, 100);

                    var data = str.DetachAsData().AsSpan();

                    File.WriteAllBytes(texOutputPath + ".png", data.ToArray());
                }
            }
        }
    }
}
