using System.Numerics;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Havok;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;

namespace Meddle.UI.Windows.Views;

public class ExportView(SqPack pack, Configuration configuration, ImageHandler imageHandler, PathManager pathManager)
    : IView
{
    private record TexGroup(TexFile File, IndexHashTableEntry Hash, string Path);

    private record ShpkGroup(ShpkFile File, string Path);

    private record MtrlGroup(
        MtrlFile File,
        string ResolvedPath,
        string OriginalPath,
        ShpkGroup Shpk,
        List<TexGroup> Textures);

    private record MdlGroup(MdlFile File, string Path, List<MtrlGroup> Mtrls);

    private record SklbGroup(SklbFile File, string Path);

    private readonly Dictionary<string, MdlGroup> models = new();
    private readonly Dictionary<string, SklbGroup> skeletons = new();
    private Dictionary<string, IView> views = new();
    private Task LoadTask = Task.CompletedTask;
    private CancellationTokenSource cts = new();
    private Task ExportTask = Task.CompletedTask;

    private MaterialUtility.MaterialParameters materialParameters = new()
    {
        SkinColor = new Vector3(1, 0.8f, 0.6f),
        HairColor = new Vector3(0.6f, 0.4f, 0.2f),
        LipColor = new Vector3(0.8f, 0.4f, 0.4f),
        HighlightColor = new Vector3(0.8f, 0.6f, 0.4f),
        DecalColor = new Vector4(0.8f, 0.6f, 0.4f, 1.0f)
    };


    private string input = "";

    private Dictionary<string, MdlGroup> HandleMdls(string[] paths)
    {
        var mdlGroups = new Dictionary<string, MdlGroup>();
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
            var mtrlGroups = new List<MtrlGroup>();
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
                var texGroups = new List<TexGroup>();
                foreach (var (texOffset, texPath) in textures)
                {
                    var texLookupResult = pack.GetFile(texPath);
                    if (texLookupResult == null)
                    {
                        continue;
                    }

                    var texFile = new TexFile(texLookupResult.Value.file.RawData);
                    texGroups.Add(new TexGroup(texFile, texLookupResult.Value.hash, texPath));
                }

                mtrlGroups.Add(new MtrlGroup(mtrlFile, mtrlPath, originalPath, new ShpkGroup(shpkFile, shpkPath),
                                             texGroups));
            }

            mdlGroups[mdlPath] = new MdlGroup(mdlFile, mdlPath, mtrlGroups);
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
                    foreach (var mtrlGroup in mdlGroup.Mtrls)
                    {
                        if (ImGui.CollapsingHeader(mtrlGroup.ResolvedPath))
                        {
                            //views[mtrlGroup.ResolvedPath].Draw();
                            if (!views.TryGetValue(mtrlGroup.ResolvedPath, out var mtrlView))
                            {
                                mtrlView = new MtrlView(mtrlGroup.File, pack, imageHandler);
                                views[mtrlGroup.ResolvedPath] = mtrlView;
                            }

                            mtrlView.Draw();

                            ImGui.SeparatorText("Textures");
                            ImGui.Indent();
                            try
                            {
                                foreach (var texGroup in mtrlGroup.Textures)
                                {
                                    if (ImGui.CollapsingHeader(texGroup.Path))
                                    {
                                        if (!views.TryGetValue(texGroup.Path, out var texView))
                                        {
                                            texView = new TexView(texGroup.Hash, texGroup.File, imageHandler,
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
                        views[path] = mdlView = new MdlView(mdlGroup.File, path);
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

        var hairColor = materialParameters.HairColor;
        if (ImGui.ColorEdit3("Hair Color", ref hairColor))
        {
            materialParameters.HairColor = hairColor;
        }

        if (materialParameters.HighlightColor != null)
        {
            var highlightColor = materialParameters.HighlightColor.Value;
            if (ImGui.ColorEdit3("Highlight Color", ref highlightColor))
            {
                materialParameters.HighlightColor = highlightColor;
            }

            // clear highlight button
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                materialParameters.HighlightColor = null;
            }
        }
        else
        {
            // set highlight button
            if (ImGui.Button("Set Highlight Color"))
            {
                materialParameters.HighlightColor = new Vector3(0.8f, 0.6f, 0.4f);
            }
        }

        var skinColor = materialParameters.SkinColor;
        if (ImGui.ColorEdit3("Skin Color", ref skinColor))
        {
            materialParameters.SkinColor = skinColor;
        }

        var lipColor = materialParameters.LipColor;
        if (ImGui.ColorEdit3("Lip Color", ref lipColor))
        {
            materialParameters.LipColor = lipColor;
        }

        var decalColor = materialParameters.DecalColor;
        if (decalColor != null)
        {
            var decalColorValue = decalColor.Value;
            if (ImGui.ColorEdit4("Decal Color", ref decalColorValue))
            {
                materialParameters.DecalColor = decalColorValue;
            }

            // clear tattoo button
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                materialParameters.DecalColor = null;
            }
        }
        else
        {
            // set tattoo button
            if (ImGui.Button("Set Decal Color"))
            {
                materialParameters.DecalColor = new Vector4(0.8f, 0.6f, 0.4f, 1.0f);
            }
        }

        if (ImGui.Button("Export as GLTF"))
        {
            var sklbs = this.skeletons
                            .Select(x => (x.Key, views[x.Key] as SklbView))
                            .Select(x => (x.Item1, x.Item2!.Resolve().GetAwaiter().GetResult()))
                            .Select(x => (x.Item1, new HavokXml(x.Item2)))
                            .ToDictionary();
            cts?.Cancel();
            cts = new CancellationTokenSource();
            ExportTask = Task.Run(() => RunExport(models, sklbs, materialParameters, cts.Token), cts.Token);
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
        Dictionary<string, MdlGroup> modelDict, Dictionary<string, HavokXml> sklbDict,
        MaterialUtility.MaterialParameters @params, CancellationToken token = default)
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

            var mtrlDict = mdlGroup.Mtrls
                                   .DistinctBy(x => x.OriginalPath)
                                   .ToDictionary(x => x.OriginalPath, x => x.File);
            var shpkDict = mdlGroup.Mtrls
                                   .Select(x => x.Shpk)
                                   .DistinctBy(x => x.Path)
                                   .ToDictionary(x => x.Path, x => x.File);
            var texDict = mdlGroup.Mtrls.SelectMany(x => x.Textures)
                                  .DistinctBy(x => x.Path)
                                  .ToDictionary(x => x.Path, x => x.File);
            var model = new Utils.Export.Model(mdlGroup.File, path, shpkDict, mtrlDict, texDict);

            var materials = new List<MaterialBuilder>();
            foreach (var mtrlGroup in mdlGroup.Mtrls)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    var textures = mtrlGroup.Textures
                                            .DistinctBy(x => x.Path)
                                            .ToDictionary(x => x.Path, x => x.File);
                    var shpk = mtrlGroup.Shpk;
                    var material = new Material(mtrlGroup.File, mtrlGroup.ResolvedPath, shpk.File, textures);
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
                        "charactertattoo.shpk" => MaterialUtility.BuildCharacterTattoo(material, name, @params),
                        "hair.shpk" => MaterialUtility.BuildHair(material, name, @params),
                        "skin.shpk" => MaterialUtility.BuildSkin(material, name, @params),
                        "iris.shpk" => MaterialUtility.BuildIris(material, name),
                        _ => MaterialUtility.BuildFallback(material, name)
                    };

                    materials.Add(builder);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing material {mtrlGroup.ResolvedPath}: {ex}");
                    throw;
                }
            }

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
            foreach (var mtrlGroup in mdlGroup.Value.Mtrls)
            {
                foreach (var texGroup in mtrlGroup.Textures)
                {
                    var texPath = texGroup.Path;
                    var fileName = Path.GetFileName(texPath);
                    var texOutputPath = Path.Combine("output", rootName, "textures", fileName);
                    var texFolder = Path.GetDirectoryName(texOutputPath) ?? "output";
                    if (!Directory.Exists(texFolder))
                    {
                        Directory.CreateDirectory(texFolder);
                    }

                    var texture = new Texture(texGroup.File, texPath, null, null, null);
                    var skTex = texture.ToBitmap();

                    var str = new SKDynamicMemoryWStream();
                    skTex.Encode(str, SKEncodedImageFormat.Png, 100);

                    var data = str.DetachAsData().AsSpan();

                    File.WriteAllBytes(texOutputPath + ".png", data.ToArray());
                }
            }
        }
    }
}
