using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Havok;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace Meddle.UI.Windows.Views;

public class ExportView(SqPack pack, Configuration configuration, ImageHandler imageHandler) : IView
{
    private string input = "";
    private readonly Dictionary<string, (MdlFile file, MdlView view, Dictionary<string, (MtrlFile file, MtrlView view)?> mtrls)?> mdlViews = new();
    private readonly Dictionary<string, (SklbFile file, SklbView view)?> sklbViews = new();

    private void HandleMdlViews()
    {
        mdlViews.Clear();
            
        var lines = input.Split('\n');
        var mdlLines = lines.Where(x => x.Contains(".mdl")).ToList();
        foreach (var line in mdlLines)
        {
            var path = line.Trim();
            if (mdlViews.TryGetValue(line, out var res))
            {
                continue;
            }
            
            var lookupResult = pack.GetFile(path);
            if (lookupResult == null)
            {
                ImGui.Text($"File not found: {path}");
                mdlViews[path] = null;
                continue;
            }

            var mdlFile = new MdlFile(lookupResult.Value.file.RawData);
            var view = new MdlView(mdlFile, path);
            
            var mtrlNames = mdlFile.GetMaterialNames();
            var mtrlViews = new Dictionary<string, (MtrlFile file, MtrlView view)?>();
            mdlViews[path] = (mdlFile, view, mtrlViews);

            foreach (var unresolvedMtrlName in mtrlNames)
            {
                var mtrlPath = unresolvedMtrlName.StartsWith('/') ? Resolve(path, unresolvedMtrlName) : unresolvedMtrlName;

                var added = false;
                var mtrlLookupResult = pack.GetFile(mtrlPath);
                if (mtrlLookupResult == null)
                {
                    var prefix = mtrlPath[..mtrlPath.LastIndexOf('/')];

                    for (var j = 1; j < 10; j++)
                    {
                        // 1 -> v0001
                        var version = j.ToString("D4");
                        var versionedPath = $"{prefix}/v{version}{unresolvedMtrlName}";
                        var versionedLookupResult = pack.GetFile(versionedPath);
                        if (versionedLookupResult != null)
                        {
                            var mtrlFile = new MtrlFile(versionedLookupResult.Value.file.RawData);
                            var mtrlView = new MtrlView(mtrlFile, pack, imageHandler);
                            mtrlViews[versionedPath] = (mtrlFile, mtrlView);
                            added = true;
                        }
                    }

                    if (!added)
                    {
                        mtrlViews[unresolvedMtrlName] = null;
                    }
                }
                else
                {
                    var mtrlFile = new MtrlFile(mtrlLookupResult.Value.file.RawData);
                    var mtrlView = new MtrlView(mtrlFile, pack, imageHandler);
                    mtrlViews[mtrlPath] = (mtrlFile, mtrlView);
                }
            }
        }
    }
    
    public static string Resolve(string mdlPath, string mtrlPath)
    {
        var mtrlPathRegex = new Regex(@"[a-z]\d{4}");
        var mtrlPathMatches = mtrlPathRegex.Matches(mtrlPath);
        if (mtrlPathMatches.Count != 2)
        {
            throw new Exception($"Invalid mdl path {mdlPath}");
        }

        if (mdlPath.StartsWith("chara/human/"))
        {
            var characterCode = mtrlPathMatches[0].Value;
            var subcategory = mtrlPathMatches[1].Value;
            
            var subCategoryName = subcategory[0] switch
            {
                'b' => "body",
                'f' => "face",
                'h' => "hair",
                't' => "tail",
                _ => throw new Exception($"Unknown subcategory {subcategory}")
            };

            return $"chara/human/{characterCode}/obj/{subCategoryName}/{subcategory}/material{mtrlPath}";
        }

        if (mdlPath.StartsWith("chara/weapon/"))
        {
            var weaponCode = mtrlPathMatches[0].Value;
            var subcategory = mtrlPathMatches[1].Value;
            
            var subCategoryName = subcategory[0] switch
            {
                'b' => "body",
                _ => throw new Exception($"Unknown subcategory {subcategory}")
            };

            return $"chara/weapon/{weaponCode}/obj/{subCategoryName}/{subcategory}/material{mtrlPath}";
        }

        if (mdlPath.StartsWith("chara/equipment/"))
        {
            var characterCode = mtrlPathMatches[0].Value;
            var equipmentCode = mtrlPathMatches[1].Value;
            if (equipmentCode.StartsWith('e'))
            {
                return $"chara/equipment/{equipmentCode}/material{mtrlPath}";
            }

            var subCategoryName = equipmentCode[0] switch
            {
                'b' => "body",
                'f' => "face",
                'h' => "hair",
                't' => "tail",
                _ => throw new Exception($"Unknown subcategory {equipmentCode}")
            };
                
            return $"chara/human/{characterCode}/obj/{subCategoryName}/{equipmentCode}/material{mtrlPath}";
        }
        
        throw new Exception($"Unsupported mdl path {mdlPath}");
    }

    private void HandleSklbViews()
    {
        sklbViews.Clear();
        
        var lines = input.Split('\n');
        var mdlLines = lines.Where(x => x.Contains(".sklb")).ToList();
        foreach (var line in mdlLines)
        {
            var path = line.Trim();
            if (sklbViews.TryGetValue(line, out var res))
            {
                continue;
            }
            
            var lookupResult = pack.GetFile(path);
            if (lookupResult == null)
            {
                sklbViews[path] = null;
                continue;
            }

            var sklbFile = new SklbFile(lookupResult.Value.file.RawData);
            var view = new SklbView(sklbFile, configuration);
            sklbViews[path] = (sklbFile, view);
        }
    }
    
    public void Draw()
    {
        if (ImGui.InputTextMultiline("##Input", ref input, 1000, new Vector2(500, 500)))
        {
            HandleMdlViews();
            HandleSklbViews();
        }

        ImGui.SeparatorText("Models");
        foreach (var (path, value) in mdlViews.Where(x => x.Value != null))
        {
            if (ImGui.CollapsingHeader(path))
            {
                if (value == null) continue;
                Indent(() =>
                {
                    var mdlInfo = value.Value;

                    foreach (var (mtrlPath, mtrlValue) in mdlInfo.mtrls)
                    {
                        if (mtrlValue == null)
                        {
                            ImGui.Text($"Mtrl {mtrlPath} not found");
                        }
                        else
                        {
                            if (ImGui.CollapsingHeader(mtrlPath))
                            {
                                Indent(() => mtrlValue.Value.view.Draw());
                            }
                        }
                    }

                    ImGui.SeparatorText("Model Info");
                    mdlInfo.view.Draw();
                });
            }
        }
        
        ImGui.SeparatorText("Skeletons");
        foreach (var (path, value) in sklbViews.Where(x => x.Value != null))
        {
            if (ImGui.CollapsingHeader(path))
            {
                value?.view.Draw();
            }
        }
        
        if (ImGui.Button("Export as GLTF"))
        {
            var scene = new SceneBuilder();
            var skeletons = sklbViews.Select(x => x.Value?.view).Where(x => x != null).Cast<SklbView>().ToArray();
            var havokXmls = skeletons.Select(x => x.Resolve().GetAwaiter().GetResult()).Select(x => new HavokXml(x))
                                     .ToArray();
            var bones = XmlUtils.GetBoneMap(havokXmls, out var root).ToArray();
            var boneNodes = bones.Cast<NodeBuilder>().ToArray();

            if (root != null)
            {
                scene.AddNode(root);
            }

            foreach (var (path, value) in mdlViews)
            {
                if (value == null)
                {
                    continue;
                }

                var mdlFile = value.Value.file;
                var model = new Utils.Export.Model(mdlFile, path ?? "");
                var materialCount = mdlFile.MaterialNameOffsets.Length; 
                var materials = new MaterialBuilder[materialCount];
                var materialNames = mdlFile.GetMaterialNames();
                for (var i = 0; i < materialNames.Length; i++)
                {
                    var name = materialNames[i];
                    materials[i] = new MaterialBuilder(name);
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
            
            // replace extension with gltf
            outputPath = Path.ChangeExtension(outputPath, ".gltf");
            
            sceneGraph.SaveGLTF(outputPath);
            
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
