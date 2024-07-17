using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Meddle.UI.Util;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Meddle.Utils.Skeletons.Havok;
using Meddle.Utils.Skeletons.Havok.Models;
using Meddle.Utils.Skeletons.HavokAnim;
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
    private Task loadTask = Task.CompletedTask;
    private CancellationTokenSource cts = new();
    private Task exportTask = Task.CompletedTask;

    private CustomizeParameter customizeParameters = new()
    {
        HairFresnelValue0 = new Vector3(0.7443291f, 0.7443291f, 0.7443291f),
        LeftColor = new Vector4(0.65261054f, 0.24804306f, 0.26795852f, 1),
        LipColor = new Vector4(0.2214533f, 0.073218f, 0.16633603f, 0.6f),
        MainColor = new Vector3(0.7994464f, 0.7994464f, 0.7994464f),
        MeshColor = new Vector3(0.75111115f, 0.4549635f, 0.41868514f),
        OptionColor = new Vector3(0.6029066f, 0.6029066f, 0.6029066f),
        RightColor = new Vector4(0.65261054f, 0.24804306f, 0.26795852f, 1),
        SkinColor = new Vector3(1, 0.7647674f, 0.7443291f),
        SkinFresnelValue0 = new Vector4(0.0625f, 0.0625f, 0.0625f, 32)
    };

    private CustomizeData customizeData = new()
    {
        LipStick = true,
        Highlights = true
    };

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

            mdlGroups[mdlPath] = new Model.MdlGroup(mdlPath, mdlFile, mtrlGroups.ToArray(), null);
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

    public void DrawFiles()
    {
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
    }
    
    public void Draw()
    {
        if (ImGui.InputTextMultiline("##Input", ref input, 1000, new Vector2(500, 500)))
        {
            loadTask = Task.Run(() =>
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

        if (!loadTask.IsCompleted)
        {
            ImGui.Text("Loading...");
            return;
        }

        if (loadTask.IsFaulted)
        {
            var ex = loadTask.Exception;
            ImGui.Text($"Error: {ex}");
            return;
        }

        DrawFiles();

        ImGui.SeparatorText("Parameters");
        DrawParameters();

        if (ImGui.Button("Export as GLTF"))
        {
            var sklbs = this.skeletons
                            .Select(x => (x.Key, SkeletonUtil.ProcessHavokInput(x.Value.File.Skeleton.ToArray())))
                            .ToDictionary(x => x.Key, y => y.Item2.Item2);

            cts?.Cancel();
            cts = new CancellationTokenSource();
            exportTask = Task.Run(() => RunExport(models, sklbs, cts.Token), cts.Token);
        }

        if (exportTask.IsFaulted)
        {
            ImGui.Text($"Error: {exportTask.Exception?.Message}");
        }
        else if (!exportTask.IsCompleted)
        {
            ImGui.Text("Exporting...");
            if (ImGui.Button("Cancel"))
            {
                cts.Cancel();
            }
        }
    }

    private void DrawParameters()
    {
        ImGui.Text("Parameters");
        //ImGui.ColorEdit3("Hair Fresnel Value 0", ref customizeParameters.HairFresnelValue0);
        ImGui.ColorEdit3("Main Color", ref customizeParameters.MainColor);
        ImGui.ColorEdit3("Mesh Color", ref customizeParameters.MeshColor);
        ImGui.ColorEdit3("Option Color", ref customizeParameters.OptionColor);
        ImGui.ColorEdit4("Left Color", ref customizeParameters.LeftColor);
        ImGui.ColorEdit4("Right Color", ref customizeParameters.RightColor);
        ImGui.ColorEdit3("Skin Color", ref customizeParameters.SkinColor);
        //ImGui.ColorEdit4("Skin Fresnel Value 0", ref customizeParameters.SkinFresnelValue0);
        ImGui.ColorEdit4("Lip Color", ref customizeParameters.LipColor);
        ImGui.Checkbox("Highlights", ref customizeData.Highlights);
        ImGui.Checkbox("Lip Stick", ref customizeData.LipStick);
    }

    private void RunExport(Dictionary<string, Model.MdlGroup> modelDict, Dictionary<string, HavokSkeleton> sklbDict, CancellationToken token = default)
    {
        var scene = new SceneBuilder();
        var havokXmls = sklbDict.Values.ToArray();
        var bones = XmlUtils.GetBoneMap(havokXmls, out var root).ToArray();
        var boneNodes = bones.Cast<NodeBuilder>().ToArray();
        var catchlightTexture = pack.GetFile("chara/common/texture/sphere_d_array.tex");
        if (catchlightTexture == null)
        {
            throw new InvalidOperationException("Missing catchlight texture");
        }
        // chara/xls/bonedeformer/human.pbd
        var pbd = pack.GetFile("chara/xls/bonedeformer/human.pbd");
        
        var catchlightTex = new TexFile(catchlightTexture.Value.file.RawData);

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

            var materials = new MaterialBuilder[model.Materials.Count];
            //foreach (var mtrlGroup in mdlGroup.Mtrls)
            Parallel.ForEach(model.Materials, (material, state, i) =>
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
                        "hair.shpk" => MaterialUtility.BuildHair(material, name, customizeParameters, customizeData),
                        "skin.shpk" => MaterialUtility.BuildSkin(material, name, customizeParameters, customizeData),
                        "iris.shpk" => MaterialUtility.BuildIris(material, name, catchlightTex, customizeParameters, customizeData),
                        _ => MaterialUtility.BuildFallback(material, name)
                    };

                    materials[i] = builder;
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
        
        Process.Start("explorer.exe", folder);

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
