using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Models.ExportRequest;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utility;

namespace Meddle.Plugin.UI;

public partial class CharacterTab
{
    public static void DrawLogMessage(ExportLogger logger)
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

    public static void HandleExportRequest(
        IExportRequest? request, ExportLogger logger, ExportManager exportManager, Configuration configuration)
    {
        if (request == null)
        {
            return;
        }

        exportManager.CancellationTokenSource.Cancel();
        switch (request)
        {
            case ExportAttachRequest ear:
                Task.Run(async () =>
                             await exportManager.Export(
                                 logger,
                                 configuration.GetExportConfig(),
                                 ear.Child.Models.ToArray(),
                                 [],
                                 ear.Child.Skeleton,
                                 ear.RaceCode,
                                 ear.CustomizeParameters));
                break;
            case ExportPartialTreeRequest eptr:
                Task.Run(async () =>
                             await exportManager.Export(
                                 logger,
                                 configuration.GetExportConfig(),
                                 eptr.SelectedModels,
                                 eptr.AttachedChildren,
                                 eptr.Skeleton,
                                 eptr.RaceCode,
                                 eptr.CustomizeParameters));
                break;
            case ExportTreeRequest etr:
                Task.Run(async () =>
                             await exportManager.Export(
                                 logger,
                                 configuration.GetExportConfig(),
                                 etr.Tree));
                break;
            case ExportModelRequest emr:
                Task.Run(async () =>
                             await exportManager.Export(
                                 logger,
                                 configuration.GetExportConfig(), 
                                 [emr.Model], 
                                 [],
                                 emr.Skeleton,
                                 emr.RaceCode,
                                 emr.CustomizeParameters));
                break;
            case MaterialExportRequest mer:
                Task.Run(async () =>
                             await exportManager.ExportMaterial(
                                 mer.Material,
                                 logger,
                                 mer.CustomizeParameters));
                break;
        }
    }

    public static void DrawModel(
        Model model, bool isExporting, CancellationTokenSource? cts, out Material? materialExport,
        out Model? modelExport)
    {
        materialExport = null;
        modelExport = null;
        var displayPath = model.ResolvedPath;
        if (displayPath != null && model.HandlePath != model.ResolvedPath)
        {
            displayPath = $"{model.ResolvedPath} -> {model.HandlePath}";
        }
        else
        {
            displayPath = model.HandlePath;
        }

        using var modelNode =
            ImRaii.TreeNode($"{displayPath}##{model.GetHashCode()}", ImGuiTreeNodeFlags.CollapsingHeader);
        if (!modelNode.Success) return;

        if (DrawExportButton($"Export##{model.GetHashCode()}", isExporting))
        {
            modelExport = model;
        }

        DrawCancelExportButton(isExporting, cts);

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
            var export = DrawExportButton($"Export Textures##{material.GetHashCode()}", isExporting);
            DrawCancelExportButton(isExporting, cts);
            DrawMaterial(material);
            if (export)
            {
                materialExport = material;
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

    public static void DrawCustomizeParameters(CharacterTree tree)
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

    public static bool DrawExportButton(string text, bool isExporting)
    {
        using var d = ImRaii.Disabled(isExporting);
        return ImGui.Button(text) && !isExporting;
    }

    public static void DrawCancelExportButton(bool isExporting, CancellationTokenSource? cts)
    {
        if (isExporting)
        {
            ImGui.SameLine();
            using var d = ImRaii.Disabled(cts?.IsCancellationRequested ?? false);
            if (ImGui.Button("Cancel"))
            {
                cts?.Cancel();
            }
        }
    }

    public static bool DrawColorEdit3(string label, Vector3 vec, out Vector3 result)
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

    public static bool DrawColorEdit4(string label, Vector4 vec, out Vector4 result)
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

    public static void DrawMesh(Mesh mesh)
    {
        ImGui.Text($"Vertices: {mesh.Vertices.Count}");
        ImGui.Text($"Indices: {mesh.Indices.Count}");
        if (mesh.BoneTable != null)
        {
            ImGui.Text($"Bones: {mesh.BoneTable.Count}");
            if (ImGui.CollapsingHeader($"Bone Table##{mesh.GetHashCode()}"))
            {
                using var btable = ImRaii.Table("BoneTable", 1, ImGuiTableFlags.Borders);
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

        ImGui.Text($"Submeshes: {mesh.SubMeshes.Count}");
        using var smtable = ImRaii.Table("SubMeshTable", 4, ImGuiTableFlags.Borders);
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

    public static void DrawMaterial(Material material)
    {
        ImGui.BulletText($"{material.HandlePath}");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(material.HandlePath);
        }

        ImGui.BulletText($"Shader: {material.ShaderPackage.Name}");
        // Display Material Textures in the same table
        using (var textable = ImRaii.Table("TextureTable", 3, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Texture Usage", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var texture in material.Textures)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"{texture.Usage}");
                ImGui.TableNextColumn();
                ImGui.Text($"{texture.Resource.Format}");
                ImGui.TableNextColumn();
                ImGui.Text($"{texture.HandlePath}");
            }
        }

        if (material.ColorTable != null)
        {
            if (ImGui.CollapsingHeader($"Color Table##{material.GetHashCode()}"))
                DrawColorTable(material.ColorTable.Value);
        }
    }

    public static void DrawColorTable(ColorTable table)
    {
        using var rt = ImRaii.Table("ColorTable", 4, ImGuiTableFlags.Borders);
        // headers
        ImGui.TableSetupColumn("Row", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Diffuse", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Specular", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Emissive", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableHeadersRow();

        for (var i = 0; i < ColorTable.NumRows; i++)
        {
            var row = table[i];
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
