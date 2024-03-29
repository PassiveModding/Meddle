using System.Diagnostics;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System.Numerics;
using Meddle.Plugin.Enums;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utility;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;

namespace Meddle.Plugin.UI;

public partial class CharacterTab
{
    private class CharacterTreeSet(
        Character character,
        CharacterTree tree,
        ExportLogger logger,
        DateTime time,
        bool[] selectedModels,
        bool[] selectedChildren)
    {
        public Character Character { get; } = character;
        public CharacterTree Tree { get; } = tree;
        public ExportLogger Logger { get; } = logger;
        public DateTime Time { get; } = time;
        public bool[] EnabledModels { get; } = selectedModels;
        public bool[] EnabledChildren { get; } = selectedChildren;
        public Model[] SelectedModels => Tree.Models.Where((x, i) => EnabledModels[i]).ToArray();
        public AttachedChild[] SelectedChildren => Tree.AttachedChildren.Where((x, i) => EnabledChildren[i]).ToArray();
    }
    
    private Task? ExportTask { get; set; }
    private CancellationTokenSource? ExportCts { get; set; }
    private CharacterTreeSet? CharacterTreeCache { get; set; }

    private unsafe CharacterTreeSet InitTree(Character character, bool refresh)
    {
        if (CharacterTreeCache != null)
        {
            if (character != CharacterTreeCache.Character || 
                character.ObjectId != CharacterTreeCache.Character.ObjectId)
            {
                CharacterTreeCache = null;
            }
        }

        if (refresh)
        {
            CharacterTreeCache = null;
        }

        if (CharacterTreeCache == null)
        {
            var address = (CSCharacter*)character.Address;
            var tree = new CharacterTree(address);
            CharacterTreeCache = new CharacterTreeSet(character, tree, 
                      new ExportLogger(Log), 
                      DateTime.Now, 
                      new bool[tree.Models.Count], 
                      new bool[tree.AttachedChildren.Count]);
        }
        
        return CharacterTreeCache;
    }

    private void DrawCharacterTree(Character character)
    {
        var tree = InitTree(character, false);
        using (var d = ImRaii.Disabled(ExportManager.IsExporting))
        {
            if (ImGui.Button("Refresh") && !ExportManager.IsExporting)
            {
                tree = InitTree(character, true);
            }
        }
        DrawCharacterTree(tree);
    }
    
    private void DrawCharacterTree(CharacterTreeSet set)
    {
        ImGui.Text($"At: {set.Time}");
        var tree = set.Tree;

        using (var d = ImRaii.Disabled(ExportManager.IsExporting))
        {
            if (ImGui.Button("Export") && !ExportManager.IsExporting)
            {
                ExportCts?.Cancel();
                ExportCts = new();
                ExportTask = ExportManager.Export(
                    set.Logger,
                    new ExportConfig
                    {
                        ExportType = ExportType.Gltf,
                        IncludeReaperEye = false,
                        OpenFolderWhenComplete = true
                    },
                    tree,
                    ExportCts.Token);
            }
            
            ImGui.SameLine();
            var selectedCount = set.EnabledModels.Count(x => x) + set.EnabledChildren.Count(x => x);
            using (var s = ImRaii.Disabled(selectedCount == 0))
            {
                if (ImGui.Button($"Export {selectedCount} Selected") && !ExportManager.IsExporting)
                {
                    ExportCts?.Cancel();
                    ExportCts = new();
                    ExportTask = ExportManager.Export(
                        set.Logger,
                        new ExportConfig
                        {
                            ExportType = ExportType.Gltf,
                            IncludeReaperEye = false,
                            OpenFolderWhenComplete = true
                        },
                        set.SelectedModels,
                        set.SelectedChildren,
                        tree.Skeleton,
                        tree.RaceCode ?? 0,
                        tree.CustomizeParameter,
                        ExportCts.Token);
                }
            }
        }

        if (ExportManager.IsExporting)
        {
            ImGui.SameLine();
            using var d = ImRaii.Disabled(ExportCts?.IsCancellationRequested ?? false);
            if (ImGui.Button("Cancel"))
            {
                ExportCts?.Cancel();
            }
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Open export folder"))
        {
            Process.Start("explorer.exe", Plugin.TempDirectory);
        }

        DrawLogMessage(set.Logger);
        DrawCustomizeParameters(tree);
        DrawModelView(tree, out var modelViewExport);
        DrawAttachView(tree, out var attachViewExport);
        
        HandleExportRequest(modelViewExport ?? attachViewExport, set.Logger, tree);
    }

    private void HandleExportRequest(IExportRequest? request, ExportLogger logger, CharacterTree tree)
    {
        if (request == null)
        {
            return;
        }
        
        ExportCts?.Cancel();
        ExportCts = new();
        
        switch (request)
        {
            case ExportModelRequest emr:
                ExportTask = ExportManager.Export(
                    logger,
                    new ExportConfig
                    {
                        ExportType = ExportType.Gltf,
                        IncludeReaperEye = false,
                        OpenFolderWhenComplete = true
                    }, [emr.Model], [], 
                    emr.SkeletonOverride ?? tree.Skeleton, 
                    tree.RaceCode ?? 0, 
                    tree.CustomizeParameter, ExportCts.Token);
                break;
            case MaterialExportRequest mer:
                ExportTask = Task.Run(() =>
                {
                    try
                    {
                        var dir = Path.Combine(Plugin.TempDirectory, "Materials", $"{mer.Material.ShaderPackage.Name}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}");
                        ExportManager.ExportMaterial(mer.Material, logger, dir, tree.CustomizeParameter);
                    }
                    catch (Exception e)
                    {
                        logger.Log(ExportLogger.LogEventLevel.Error, e.Message);
                    }
                });
                break;
        }
    }

    private static void DrawLogMessage(ExportLogger logger)
    {
        var (level, message) = logger.GetLastLog();
        if (message != null)
        {
            switch (level)
            {
                case ExportLogger.LogEventLevel.Debug:
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), message);
                    break;
                case ExportLogger.LogEventLevel.Information:
                    ImGui.TextColored(new Vector4(0, 0.5f, 0, 1), message);
                    break;
                case ExportLogger.LogEventLevel.Warning:
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0, 1), message);
                    break;
                case ExportLogger.LogEventLevel.Error:
                    ImGui.TextColored(new Vector4(0.5f, 0, 0, 1), message);
                    break;
                default:
                    ImGui.Text(message);
                    break;
            }
        }
    }
    
    private static void DrawCustomizeParameters(CharacterTree tree)
    {
        ImGui.Text("Customize");
        if (tree.CustomizeParameter == null)
        {
            ImGui.Text("No customize parameters");
            return;
        }

        var c = tree.CustomizeParameter;
        
        var sk3 = new Vector3(c.SkinColor.X, c.SkinColor.Y, c.SkinColor.Z);
        if (DrawColorEdit3("Skin Color", sk3, out var sc3))
        {
            c.SkinColor = new Vector4(sc3.X, sc3.Y, sc3.Z, c.SkinColor.W);
        }
        
        if (DrawColorEdit4("Lip Color", c.LipColor, out var lc4))
        {
            c.LipColor = lc4;
        }
        
        ImGui.SameLine();
        ImGui.Checkbox("Apply Lip Color", ref c.ApplyLipColor);
        
        // info hover for lip
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Lip color does not apply to Hrothgar");
        }
        
        if (DrawColorEdit3("Hair Color", c.MainColor, out var hc3))
        {
            c.MainColor = hc3;
        }
        
        if (DrawColorEdit3("Hair Highlight", c.MeshColor, out var hh3))
        {
            c.MeshColor = hh3;
        }
        
        if (DrawColorEdit4("Left Iris", c.LeftColor, out var ic4))
        {
            c.LeftColor = ic4;
        }
        
        if (DrawColorEdit3("Race Feature", c.OptionColor, out var oc3))
        {
            c.OptionColor = oc3;
        }
        
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Tattoo Color, Limbal Ring Color, Ear Clasp Color; Varies by race");
        }


        // Not sure about applying separate eye colours yet
        /*
        var irisCol2 = c.RightColor;
        var ic42 = new Vector4(irisCol2.X, irisCol2.Y, irisCol2.Z, irisCol2.W);
        if (ImGui.ColorEdit4("Right Iris", ref ic42))
        {
            c.RightColor = ic42;
        }*/

        tree.CustomizeParameter = c;
    }
    
    private static bool DrawColorEdit3(string label, Vector3 vec, out Vector3 result)
    {
        var v = new Vector3(vec.X, vec.Y, vec.Z);
        if (ImGui.ColorEdit3(label, ref v))
        {
            result = new Vector3(v.X, v.Y, v.Z);
            return true;
        }

        result = vec;
        return false;
    }
    
    private static bool DrawColorEdit4(string label, Vector4 vec, out Vector4 result)
    {
        var v = new Vector4(vec.X, vec.Y, vec.Z, vec.W);
        if (ImGui.ColorEdit4(label, ref v))
        {
            result = new Vector4(v[0], v[1], v[2], v[3]);
            return true;
        }

        result = vec;
        return false;
    }

    private void DrawAttachView(CharacterTree tree, out IExportRequest? exportRequest)
    {
        exportRequest = null;
        if (tree.AttachedChildren.Count > 0)
        {
            ImGui.Text("Attached Children");
            foreach (var child in tree.AttachedChildren)
            {
                ImGui.Text($"Position: {child.Attach.OffsetTransform.Translation}");
                ImGui.Text($"Rotation: {child.Attach.OffsetTransform.Rotation}");
                ImGui.Text($"Scale: {child.Attach.OffsetTransform.Scale}");
                
                using var mainTable = ImRaii.Table("AttachedChildren", 1, ImGuiTableFlags.Borders);
                for (var i = 0; i < child.Models.Count; i++)
                {
                    var model = child.Models[i];
                    DrawModel(model, i, true, out var er);
                    if (er != null && er is ExportModelRequest emr)
                    {
                        emr.SkeletonOverride = child.Skeleton;
                    }
                }
            }
        }
    }
    
    private void DrawModelView(CharacterTree tree, out IExportRequest? exportRequest)
    {
        exportRequest = null!;
        if (tree.Models.Count == 0)
        {
            ImGui.Text("No models found. Try refreshing.");
            return;
        }
        
        ImGui.Text("Models");
        using var mainTable = ImRaii.Table("Models", 1, ImGuiTableFlags.Borders);
        for (var i = 0; i < tree.Models.Count; i++)
        {
            var model = tree.Models[i];
            DrawModel(model, i, false, out var er);
            if (er != null)
            {
                exportRequest = er;
            }
        }
    }

    public interface IExportRequest;
    public class ExportModelRequest(Model model) : IExportRequest
    {
        public Model Model { get; } = model;
        public Skeleton? SkeletonOverride { get; set; }
    }
    
    public class MaterialExportRequest(Material material) : IExportRequest
    {
        public Material Material { get; } = material;
    }

    private void DrawModel(Model model, int index, bool isChild, out IExportRequest? exportRequest)
    {
        exportRequest = null!;
        ImGui.TableNextColumn();
        if (isChild)
        {
            ImGui.Checkbox($"##{model.GetHashCode()}", ref CharacterTreeCache!.EnabledChildren[index]);
        }
        else
        {
            ImGui.Checkbox($"##{model.GetHashCode()}", ref CharacterTreeCache!.EnabledModels[index]);
        }
        ImGui.SameLine();

        using var modelNode = ImRaii.TreeNode($"{model.Path}##{model.GetHashCode()}", ImGuiTreeNodeFlags.CollapsingHeader);
        if (!modelNode.Success) return;
        
        using (var d = ImRaii.Disabled(ExportManager.IsExporting))
        {
            if (ImGui.SmallButton($"Export##{model.GetHashCode()}") && (ExportTask?.IsCompleted ?? true))
            {
                exportRequest = new ExportModelRequest(model);
            }
        }
        
        ImGui.Text($"Handle Path: {model.HandlePath}");
        ImGui.Text($"Resolved Path: {model.ResolvedPath}");

        if (model.Shapes.Count > 0)
        {
            ImGui.TextWrapped($"Shapes: {string.Join(", ", model.Shapes.Select(x => x.Name))}");
            ImGui.TextWrapped($"Enabled Shapes: {string.Join(", ", model.EnabledShapes)}");
        }

        if (model.EnabledAttributes.Count > 0)
        {
            ImGui.TextWrapped($"Enabled Attributes: {string.Join(", ", model.EnabledAttributes)}");
        }

        // Display Materials
        for (var m = 0; m < model.Materials.Count; m++)
        {
            var material = model.Materials[m];
            ImGui.Text($"Material #{m}");
            ImGui.Indent();
            DrawMaterial(material, out var exportMaterial);
            if (exportMaterial)
            {
                exportRequest = new MaterialExportRequest(material);
            }
            ImGui.Unindent();
            ImGui.Separator();
        }
        
        
        for (var m = 0; m < model.Meshes.Count; m++)
        {
            var mesh = model.Meshes[m];
            ImGui.Text($"Mesh #{m}");
            ImGui.Indent();
            DrawMesh(mesh);
            ImGui.Unindent();
            ImGui.Separator();
        }
    }
    
    private void DrawMesh(Mesh mesh)
    {
        ImGui.Text($"Vertices: {mesh.Vertices.Count}");
        ImGui.Text($"Indices: {mesh.Indices.Count}");
        if (mesh.BoneTable != null)
        {
            ImGui.Text($"Bones: {mesh.BoneTable.Count}");
            if (ImGui.CollapsingHeader($"Bone Table##{mesh.GetHashCode()}"))
            {
                using (var btable = ImRaii.Table("BoneTable", 1, ImGuiTableFlags.Borders))
                {
                    ImGui.TableSetupColumn("Bone", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();
                    foreach (var bone in mesh.BoneTable)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(bone);
                    }
                }
            }
        }
        
        ImGui.Text($"Submeshes: {mesh.SubMeshes.Count}");
        using (var smtable = ImRaii.Table("SubMeshTable", 4, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Offset", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Attributes", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            for (var i = 0; i < mesh.SubMeshes.Count; i++)
            {
                var submesh = mesh.SubMeshes[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"{i}");
                ImGui.TableNextColumn();
                ImGui.Text($"{submesh.IndexOffset}");
                ImGui.TableNextColumn();
                ImGui.Text($"{submesh.IndexCount}");
                ImGui.TableNextColumn();
                ImGui.Text($"{string.Join(", ", submesh.Attributes)}");
            }
        }
    }
    
    private void DrawMaterial(Material material, out bool export)
    {
        export = ImGui.Button($"Export Textures##{material.GetHashCode()}");
        
        ImGui.BulletText($"{material.HandlePath}");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(material.HandlePath);
        }

        ImGui.BulletText($"Shader: {material.ShaderPackage.Name}");
        // Display Material Textures in the same table
        using (var textable = ImRaii.Table("TextureTable", 2, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Texture Usage", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            
            foreach (var texture in material.Textures)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"{texture.Usage}");
                ImGui.TableNextColumn();
                ImGui.Text($"{texture.HandlePath}");
            }
        }

        if (material.ColorTable != null)
        {
            if (ImGui.CollapsingHeader($"Color Table##{material.GetHashCode()}"))
                DrawColorTable(material.ColorTable);
        }
    }
    
    private void DrawColorTable(ColorTable table)
    {
        using var rt = ImRaii.Table("ColorTable", 4, ImGuiTableFlags.Borders);
        // headers
        ImGui.TableSetupColumn("Row", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Diffuse", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Specular", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Emissive", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableHeadersRow();
        
        for (var i = 0; i < table.Rows.Length; i++)
        {
            var row = table.Rows[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text($"Row {i}");
            ImGui.TableNextColumn();
            ImGui.ColorButton("##Diffuse", new Vector4(row.Diffuse.X, row.Diffuse.Y, row.Diffuse.Z, 1),
                              ImGuiColorEditFlags.NoAlpha);
            ImGui.TableNextColumn();
            ImGui.ColorButton("##Specular", new Vector4(row.Specular.X, row.Specular.Y, row.Specular.Z, 1),
                              ImGuiColorEditFlags.NoAlpha);
            ImGui.TableNextColumn();
            ImGui.ColorButton("##Emissive", new Vector4(row.Emissive.X, row.Emissive.Y, row.Emissive.Z, 1),
                              ImGuiColorEditFlags.NoAlpha);
                
        }
    }
}
