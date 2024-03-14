using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System.Numerics;
using Meddle.Plugin.Enums;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;

namespace Meddle.Plugin.UI;

public partial class CharacterTab
{
    private void DrawCharacterTree(CharacterTree tree)
    {
        using (var d = ImRaii.Disabled(!(ExportTask?.IsCompleted ?? true)))
        {
            if (ImGui.Button("Export"))
            {
                ExportCts?.Cancel();
                ExportCts = new();
                ExportTask = ModelConverter.Export(
                    Logger,
                    new ExportConfig
                    {
                        ExportType = ExportType.Gltf,
                        IncludeReaperEye = false,
                        OpenFolderWhenComplete = true
                    },
                    tree,
                    ExportCts.Token);
            }
        }
        
        var log = Logger.GetLastLog();
        if (log != default)
        {
            ImGui.SameLine();
            ImGui.Text($"{log.level}: {log.message}");
        }
        
        ImGui.Text("Customize");
        if (tree.CustomizeParameter != null)
        {
            var c = tree.CustomizeParameter;
            DrawCustomizeParameters(ref c);
            tree.CustomizeParameter = c;
        }
        else
        {
            ImGui.Text("No customize parameters");
        }
        
        DrawModelView(tree);
    }
    
    private static void DrawCustomizeParameters(ref CustomizeParameters c)
    {
        var skinCol = c.SkinColor;
        var sk3 = new Vector3(skinCol.X, skinCol.Y, skinCol.Z);
        // set size
        if (ImGui.ColorEdit3("Skin Color", ref sk3))
        {
            // W is muscle tone
            c.SkinColor = new(sk3.X, sk3.Y, sk3.Z, skinCol.W);
        }

        /*
        // Unused for now
        var muscle = skinCol.W;
        if (ImGui.SliderFloat("Muscle Tone", ref muscle, 0, 1))
        {
            c.SkinColor = new(skinCol.X, skinCol.Y, skinCol.Z, muscle);
        }
        */
            
        var lipCol = c.LipColor;
        var lp4 = new Vector4(lipCol.X, lipCol.Y, lipCol.Z, lipCol.W);
        if (ImGui.ColorEdit4("Lip Color", ref lp4))
        {
            c.LipColor = lp4;
        }
            
        var hairCol = c.MainColor;
        var hc3 = new Vector3(hairCol.X, hairCol.Y, hairCol.Z);
        if (ImGui.ColorEdit3("Hair Color", ref hc3))
        {
            c.MainColor = hc3;
        }
            
        var hairHighlight = c.MeshColor;
        var hh3 = new Vector3(hairHighlight.X, hairHighlight.Y, hairHighlight.Z);
        if (ImGui.ColorEdit3("Hair Highlight", ref hh3))
        {
            c.MeshColor = hh3;
        }
        
        var irisCol = c.LeftColor;
        var ic4 = new Vector4(irisCol.X, irisCol.Y, irisCol.Z, irisCol.W);
        if (ImGui.ColorEdit4("Left Iris", ref ic4))
        {
            c.LeftColor = ic4;
        }
        
        var irisCol2 = c.RightColor;
        var ic42 = new Vector4(irisCol2.X, irisCol2.Y, irisCol2.Z, irisCol2.W);
        if (ImGui.ColorEdit4("Right Iris", ref ic42))
        {
            c.RightColor = ic42;
        }
    }

    private void DrawModelView(CharacterTree tree)
    {
        using var mainTable = ImRaii.Table("Models", 1, ImGuiTableFlags.Borders);
        foreach (var model in tree.Models)
        {

            ImGui.TableNextColumn();
            using var modelNode = ImRaii.TreeNode($"{model.HandlePath}##{model.GetHashCode()}", ImGuiTreeNodeFlags.CollapsingHeader);
            if (!modelNode.Success) continue;
            
            // Export icon
            if (ImGui.SmallButton($"Export##{model.GetHashCode()}"))
            {
                ExportCts?.Cancel();
                ExportCts = new();
                ExportTask = ModelConverter.Export(
                    Logger,
                    new ExportConfig
                    {
                        ExportType = ExportType.Gltf,
                        IncludeReaperEye = false,
                        OpenFolderWhenComplete = true
                    }, model, tree.Skeleton, tree.RaceCode!.Value, tree.CustomizeParameter, ExportCts.Token);
            }
                
            if (model.Shapes.Count > 0)
            {
                ImGui.Text($"Shapes: {string.Join(", ", model.Shapes.Select(x => x.Name))}");
                ImGui.Text($"Enabled Shapes: {string.Join(", ", model.EnabledShapes)}");
            }

            if (model.EnabledAttributes.Length > 0)
            {
                ImGui.Text($"Enabled Attributes: {string.Join(", ", model.EnabledAttributes)}");
            }

            // Display Materials
            using (var table = ImRaii.Table("MaterialsTable", 2,
                                            ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthFixed,
                                       0.75f * ImGui.GetWindowWidth());
                ImGui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthStretch);

                foreach (var material in model.Materials)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{material.HandlePath}");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(material.HandlePath);
                    }
                    ImGui.TableNextColumn();
                    ImGui.Text($"Shader: {material.ShaderPackage.Name} Textures: {material.Textures.Count}");
                    ImGui.Indent();
                    // Display Material Textures in the same table
                    foreach (var texture in material.Textures)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text($"{texture.HandlePath}");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{texture.Usage}");
                    }

                    ImGui.Unindent();
                }
            }


            ImGui.Spacing();

            using var tableMeshes = ImRaii.Table("MeshesTable", 3,
                                                 ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("Mesh", ImGuiTableColumnFlags.WidthFixed, 0.5f * ImGui.GetWindowWidth());
            ImGui.TableSetupColumn("Vertices", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Indices", ImGuiTableColumnFlags.WidthStretch);

            for (var i = 0; i < model.Meshes.Count; ++i)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                ImGui.Text($"Mesh {i}");
                var mesh = model.Meshes[i];
                ImGui.TableNextColumn();
                ImGui.Text($"Vertices: {mesh.Vertices.Count}");
                ImGui.TableNextColumn();
                ImGui.Text($"Indices: {mesh.Indices.Count}");
                for (var j = 0; j < mesh.Submeshes.Count; j++)
                {
                    var submesh = mesh.Submeshes[j];
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"[{j}] Submesh attributes: {string.Join(", ", submesh.Attributes)}");
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn(); // Leave an empty column for spacing
                }
            }
        }
    }
}
