﻿using System.Diagnostics;
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

public class ExportView(SqPack pack, Configuration configuration, ImageHandler imageHandler) : IView
{
    private record TexGroup(TexFile File, string Path);
    private record ShpkGroup(ShpkFile File, string Path);
    private record MtrlGroup(MtrlFile File, string ResolvedPath, string OriginalPath, ShpkGroup Shpk, List<TexGroup> Textures);
    private record MdlGroup(MdlFile File, string Path, List<MtrlGroup> Mtrls);
    private record SklbGroup(SklbFile File, string Path);
    private readonly Dictionary<string, MdlGroup> models = new();
    private readonly Dictionary<string, SklbGroup> skeletons = new();
    private Dictionary<string, IView> views = new();
    private Task LoadTask = Task.CompletedTask;
    
    private MaterialUtility.MaterialParameters materialParameters = new()
    {
        SkinColor = new Vector3(1, 0.8f, 0.6f),
        HairColor = new Vector3(0.6f, 0.4f, 0.2f),
        LipColor = new Vector3(0.8f, 0.4f, 0.4f),
        HighlightColor = new Vector3(0.8f, 0.6f, 0.4f),
        DecalColor = new Vector4(0.8f, 0.6f, 0.4f, 1.0f)
    };
    
    
    private string input = "";

    private void HandleMdls()
    {
        var mdlLines = input.Split('\n')
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
                    texGroups.Add(new TexGroup(texFile, texPath));
                    views[texPath] = new TexView(texLookupResult.Value.hash, texFile, imageHandler, texPath);
                }

                mtrlGroups.Add(new MtrlGroup(mtrlFile, mtrlPath, originalPath, new ShpkGroup(shpkFile, shpkPath), texGroups));
                views[mtrlPath] = new MtrlView(mtrlFile, pack, imageHandler);
            }
            
            models[mdlPath] = new MdlGroup(mdlFile, mdlPath, mtrlGroups);
            views[mdlPath] = new MdlView(mdlFile, mdlPath);
        }
    }
    
    private void HandleSklbs()
    {
        var lines = input.Split('\n');
        var mdlLines = lines.Select(x => x.Trim()).Where(x => x.EndsWith(".sklb")).ToList();
        foreach (var sklbPath in mdlLines)
        {
            var lookupResult = pack.GetFile(sklbPath);
            if (lookupResult == null)
            {
                continue;
            }
            
            var sklbFile = new SklbFile(lookupResult.Value.file.RawData);
            skeletons[sklbPath] = new SklbGroup(sklbFile, sklbPath);
            views[sklbPath] = new SklbView(sklbFile, configuration);

        }
    }
    
    public void Draw()
    {
        if (ImGui.InputTextMultiline("##Input", ref input, 1000, new Vector2(500, 500)))
        {
            LoadTask = Task.Run(() =>
            {
                models.Clear();
                skeletons.Clear();
                HandleMdls();
                HandleSklbs();
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
                Indent(() =>
                {
                    ImGui.SeparatorText("Materials");
                    foreach (var mtrlGroup in mdlGroup.Mtrls)
                    {
                        if (ImGui.CollapsingHeader(mtrlGroup.ResolvedPath))
                        {
                            views[mtrlGroup.ResolvedPath].Draw();
                            
                            ImGui.SeparatorText("Textures");
                            Indent(() =>
                            {
                                foreach (var texGroup in mtrlGroup.Textures)
                                {
                                    if (ImGui.CollapsingHeader(texGroup.Path))
                                    {
                                        views[texGroup.Path].Draw();
                                    }
                                }
                            });
                        }
                    }

                    ImGui.SeparatorText("Model Info");
                    Indent(() =>
                    {
                        views[path].Draw();
                    });
                });
            }
        }
        
        ImGui.SeparatorText("Skeletons");
        foreach (var (path, value) in skeletons)
        {
            if (ImGui.CollapsingHeader(path))
            {
                views[path].Draw();
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
            var scene = new SceneBuilder();
            var sklbTasks = this.skeletons.Select(x => views[x.Key] as SklbView).Select(x => x.Resolve()).ToArray();
            var havokXmls = sklbTasks.Select(x => x.GetAwaiter().GetResult()).Select(x => new HavokXml(x))
                                     .ToArray();
            var bones = XmlUtils.GetBoneMap(havokXmls, out var root).ToArray();
            var boneNodes = bones.Cast<NodeBuilder>().ToArray();

            if (root != null)
            {
                scene.AddNode(root);
            }

            foreach (var (path, mdlGroup) in models)
            {
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
                    var textures = mtrlGroup.Textures
                                            .DistinctBy(x => x.Path)
                                            .ToDictionary(x => x.Path, x => x.File);
                    var shpk = mtrlGroup.Shpk;
                    var material = new Material(mtrlGroup.File, mtrlGroup.ResolvedPath, shpk.File, textures);
                    //var builder = MaterialUtility.ParseMaterial(material);
                    
                    var name = $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_{Path.GetFileNameWithoutExtension(material.ShaderPackageName)}";

                    var builder = material.ShaderPackageName switch
                    {
                        "bg.shpk" => MaterialUtility.BuildBg(material, name),
                        "bgprop.shpk" => MaterialUtility.BuildBgProp(material, name),
                        "character.shpk" => MaterialUtility.BuildCharacter(material, name),
                        "characterocclusion.shpk" => MaterialUtility.BuildCharacterOcclusion(material, name),
                        "characterlegacy.shpk" => MaterialUtility.BuildCharacterLegacy(material, name),
                        "charactertattoo.shpk" => MaterialUtility.BuildCharacterTattoo(material, name, materialParameters),
                        "hair.shpk" => MaterialUtility.BuildHair(material, name, materialParameters),
                        "skin.shpk" => MaterialUtility.BuildSkin(material, name, materialParameters),
                        "iris.shpk" => MaterialUtility.BuildIris(material, name),
                        _ => MaterialUtility.BuildFallback(material, name)
                    };
                    
                    materials.Add(builder);
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
            
            var outputPath = Path.Combine("output", "model.mdl");
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
            foreach (var mdlGroup in models)
            {
                foreach (var mtrlGroup in mdlGroup.Value.Mtrls)
                {
                    foreach (var texGroup in mtrlGroup.Textures)
                    {
                        var texPath = texGroup.Path;
                        var fileName = Path.GetFileName(texPath);
                        var texOutputPath = Path.Combine("output", "textures", fileName);
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
            
            Process.Start("explorer.exe", folder);
        }
    }

    private void Indent(Action action)
    {
        try
        {
            ImGui.Indent();
            action();
        } finally
        {
            ImGui.Unindent();
        }
    }
}