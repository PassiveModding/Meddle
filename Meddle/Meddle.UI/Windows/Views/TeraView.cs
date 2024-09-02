using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Meddle.UI.Models;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SqPack = Meddle.Utils.Files.SqPack.SqPack;

namespace Meddle.UI.Windows.Views;

public class TeraView : IView
{
    private readonly TeraFile teraFile;
    private readonly string? handlePath;
    private readonly SqPack sqPack;
    private readonly Configuration config;
    private readonly ImageHandler imageHandler;
    private readonly PathManager pathManager;
    private readonly List<(string Path, LgbFile.Group.InstanceObject ObjectInfo)> bgObjects = new();
    private readonly HexView hexView;
    private readonly List<(string Path, Vector2 Position, MdlFile file, MdlView view)> mdlFiles = new();

    public TeraView(
        TeraFile teraFile, string? handlePath, SqPack sqPack, Configuration config, ImageHandler imageHandler,
        PathManager pathManager)
    {
        this.teraFile = teraFile;
        this.handlePath = handlePath;
        this.sqPack = sqPack;
        this.config = config;
        this.imageHandler = imageHandler;
        this.pathManager = pathManager;
        hexView = new HexView(teraFile.RawData);

        if (handlePath != null)
        {
            var bgLgbPath = handlePath.Replace("bgplate/terrain.tera", "level/bg.lgb");
            var bgLgbData = sqPack.GetFile(bgLgbPath);
            if (bgLgbData != null)
            {
                var lgbFile = new LgbFile(bgLgbData.Value.file.RawData);
                for (var i = 0; i < lgbFile.Groups.Length; i++)
                {
                    var group = lgbFile.Groups[i];
                    for (var j = 0; j < group.InstanceObjects.Length; j++)
                    {
                        var instanceObject = group.InstanceObjects[j];
                        if (instanceObject.Type == LgbFile.LayerEntryType.BG)
                        {
                            var bgData = LgbFileExtensions.GetBgInstanceObject(lgbFile, i, j);
                            bgObjects.Add((bgData.ModelPath, instanceObject));
                        }
                    }
                }
            }
            var mdlRoot = handlePath.Replace("/terrain.tera", "");
            for (int i = 0; i < teraFile.Header.PlateCount; i++)
            {
                var pos = teraFile.GetPlatePosition(i);
                var mdlPath = $"{mdlRoot}/{i:D4}.mdl";
                var mdlData = sqPack.GetFile(mdlPath);
                if (mdlData != null)
                {
                    var mdlFile = new MdlFile(mdlData.Value.file.RawData);
                    var view = new MdlView(mdlFile, mdlPath, sqPack, this.imageHandler);
                    mdlFiles.Add((mdlPath, pos, mdlFile, view));
                }
            }
        }
    }

    private Task exportTask = Task.CompletedTask;


    private BgObjectGroup ParseBgObject(
        LgbFile.Group.InstanceObject obj, string mdlPath, Dictionary<string, MtrlFileGroup> mtrlCache)
    {
        var mdlFile = sqPack.GetFile(mdlPath);
        if (mdlFile == null) throw new Exception($"Failed to find {mdlPath}");
        var mdl = new MdlFile(mdlFile.Value.file.RawData);

        var mtrlPaths = mdl.GetMaterialNames().Select(x => x.Value).ToArray();
        var mtrlFiles = new List<MtrlFileGroup>();
        foreach (var mtrlFilePath in mtrlPaths)
        {
            if (mtrlCache.TryGetValue(mtrlFilePath, out var group))
            {
                mtrlFiles.Add(group);
                continue;
            }

            var mtrlFile = sqPack.GetFile(mtrlFilePath);
            Console.WriteLine($"Getting {mtrlFilePath}");
            if (mtrlFile == null) throw new Exception($"Failed to find {mtrlFilePath}");

            var mtrlFileData = new MtrlFile(mtrlFile.Value.file.RawData);
            var shpkName = mtrlFileData.GetShaderPackageName();
            var shpkPath = $"shader/sm5/shpk/{shpkName}";
            var shpkFile = sqPack.GetFile(shpkPath);
            if (shpkFile == null) throw new Exception($"Failed to find {shpkPath}");
            var shpkData = new ShpkFile(shpkFile.Value.file.RawData);

            var texPaths = mtrlFileData.GetTexturePaths().Select(x => x.Value).ToArray();
            var texFiles = new List<TexFileGroup>();
            foreach (var texPath in texPaths)
            {
                var texFile = sqPack.GetFile(texPath);
                if (texFile == null) throw new Exception($"Failed to find {texPath}");
                var texData = new TexFile(texFile.Value.file.RawData);
                texFiles.Add(new TexFileGroup(texPath, texData));
            }

            group = new MtrlFileGroup(mtrlFilePath, mtrlFileData, shpkName, shpkData, texFiles.ToArray());
            mtrlFiles.Add(group);
            mtrlCache.Add(mtrlFilePath, group);
        }

        return new BgObjectGroup(obj, new MdlFileGroup(mdlPath, mdl, mtrlFiles.ToArray(), null));
    }

    private void RunExport()
    {
        try
        {
            var mdlGroups = new List<(MdlFileGroup, LgbFile.Group.InstanceObject)>();
            var mdlFileCache = new Dictionary<string, MdlFileGroup>();
            var mtrlCache = new Dictionary<string, MtrlFileGroup>();
            foreach (var bgObject in bgObjects)
            {
                if (mdlFileCache.TryGetValue(bgObject.Path, out var group))
                {
                    var mdlGroup = group with {Path = bgObject.Path, ShapeAttributeGroup = null};
                    mdlGroups.Add((mdlGroup, bgObject.ObjectInfo));
                    continue;
                }

                Console.WriteLine($"Parsing {bgObject.Path}");
                var bgGroup = ParseBgObject(bgObject.ObjectInfo, bgObject.Path, mtrlCache);
                mdlFileCache.Add(bgObject.Path, bgGroup.MdlGroup);
                mdlGroups.Add((bgGroup.MdlGroup, bgObject.ObjectInfo));
            }

            var materialBuilderCache = new Dictionary<string, MaterialBuilder>();

            var scene = new SceneBuilder();
            foreach (var mdlGroup in mdlGroups)
            {
                Console.WriteLine($"Exporting {mdlGroup.Item1.Path}");
                var mdl = mdlGroup.Item1;
                var model = new Model(mdl.Path, mdl.MdlFile, mdl.ShapeAttributeGroup);
                var materials = new MaterialBuilder[mdlGroup.Item1.MtrlFiles.Length];
                for (var i = 0; i < mdlGroup.Item1.MtrlFiles.Length; i++)
                {
                    var mtrlGroup = mdlGroup.Item1.MtrlFiles[i];
                    var material = new Material(mtrlGroup.Path, mtrlGroup.MtrlFile, mtrlGroup.TexFiles.ToDictionary(x => x.Path, x => x.TexFile), mtrlGroup.ShpkFile);
                    Console.WriteLine($"Compiling {material!.ShaderPackageName} - {material.HandlePath}");
                    if (!materialBuilderCache.ContainsKey(material!.HandlePath))
                    {
                        var name =
                            $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_{Path.GetFileNameWithoutExtension(material.ShaderPackageName)}";
                        var builder = material.ShaderPackageName switch
                        {
                            "bg.shpk" => MaterialUtility.BuildBg(material, name),
                            "bgprop.shpk" => MaterialUtility.BuildBgProp(material, name),
                            _ => MaterialUtility.BuildFallback(material, name)
                        };

                        materialBuilderCache.Add(material.HandlePath, builder);
                    }

                    materials[i] = materialBuilderCache[material.HandlePath];
                }

                var meshes = ModelBuilder.BuildMeshes(model, materials, Array.Empty<BoneNodeBuilder>(), null);
                var translation = Matrix4x4.CreateTranslation(mdlGroup.Item2.Translation);
                var rotation =
                    Matrix4x4.CreateRotationX(mdlGroup.Item2.Rotation.X) *
                    Matrix4x4.CreateRotationY(mdlGroup.Item2.Rotation.Y) *
                    Matrix4x4.CreateRotationZ(mdlGroup.Item2.Rotation.Z);
                var scale = Matrix4x4.CreateScale(mdlGroup.Item2.Scale);
                var transform = translation * rotation * scale;

                // check identity column for validity
                // ensure invertable
                // ensure decomposable
                var isValid = Matrix4x4.Decompose(transform, out var scale2, out var rotation2, out var translation2);
                if (!isValid)
                {
                    Console.WriteLine("Invalid transform");
                    continue;
                }
                
                var isValid2 = Matrix4x4.Invert(transform, out var inverted);
                if (!isValid2)
                {
                    Console.WriteLine("Invalid transform");
                    continue;
                }
                
                foreach (var mesh in meshes)
                {
                    scene.AddRigidMesh(mesh.Mesh, transform);
                }
            }

            var gltf = scene.ToGltf2();
            var folderName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var outputFolder = Path.Combine("output", folderName);
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            var gltfPath = Path.Combine(outputFolder, "scene.gltf");
            gltf.SaveGLTF(gltfPath);
            Process.Start("explorer.exe", outputFolder);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public void Draw()
    {
        foreach (var (mdlPath, pos, mdlFile, view) in mdlFiles)
        {
            ImGui.PushID(mdlPath);
            if (ImGui.TreeNode(mdlPath))
            {
                ImGui.Text($"Position: {pos}");
                view.Draw();
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        
        hexView.DrawHexDump();

        ImGui.BeginDisabled(!exportTask.IsCompleted);
        if (ImGui.Button("Export"))
        {
            exportTask = Task.Run(RunExport);
        }

        ImGui.EndDisabled();
        if (!exportTask.IsCompleted)
        {
            ImGui.SameLine();
            ImGui.Text("Exporting...");
        }
        else if (exportTask.IsFaulted)
        {
            ImGui.Text($"Export failed: {exportTask.Exception?.Message}");
        }
    }
}
