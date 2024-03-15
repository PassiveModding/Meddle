using System.Diagnostics;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System.Numerics;
using Meddle.Plugin.Enums;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Utility;
using Serilog.Events;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;

namespace Meddle.Plugin.UI;

public partial class CharacterTab
{
    private Task? ExportTask { get; set; }
    private CancellationTokenSource? ExportCts { get; set; }
    private (Character character, CharacterTree tree, ExportLogger logger, DateTime time)? CharacterTreeCache { get; set; }

    private unsafe (Character character, CharacterTree tree, ExportLogger logger, DateTime time) InitTree(Character character, bool refresh)
    {
        var now = DateTime.Now;
        if (CharacterTreeCache != null)
        {
            if (character != CharacterTreeCache.Value.character || character.ObjectId != CharacterTreeCache.Value.character.ObjectId)
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
            CharacterTreeCache = (character, tree, new ExportLogger(log), now);
        }
        
        return CharacterTreeCache.Value;
    }
    
    private void DrawCharacterTree(Character character)
    {
        var tree = InitTree(character, false);
        
        ImGui.Text($"Character: {tree.tree.Name}");
        ImGui.Text($"At: {tree.time}");

        using (var d = ImRaii.Disabled(!(ExportTask?.IsCompleted ?? true)))
        {
            if (ImGui.Button("Export") && (ExportTask?.IsCompleted ?? true))
            {
                ExportCts?.Cancel();
                ExportCts = new();
                ExportTask = ModelConverter.Export(
                    tree.logger,
                    new ExportConfig
                    {
                        ExportType = ExportType.Gltf,
                        IncludeReaperEye = false,
                        OpenFolderWhenComplete = true
                    },
                    tree.tree,
                    ExportCts.Token);
            }
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Open export folder"))
        {
            Process.Start("explorer.exe", Plugin.TempDirectory);
        }
        
        ImGui.SameLine();
        using (var d = ImRaii.Disabled(!(ExportTask?.IsCompleted ?? true)))
        {
            if (ImGui.Button("Refresh") && (ExportTask?.IsCompleted ?? true))
            {
                tree = InitTree(character, true);
            }
        }

        var (level, message) = tree.logger.GetLastLog();
        if (message != null)
        {
            ImGui.SameLine();
            switch (level)
            {
                case LogEventLevel.Debug:
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), message);
                    break;
                case LogEventLevel.Information:
                    ImGui.TextColored(new Vector4(0, 0.5f, 0, 1), message);
                    break;
                case LogEventLevel.Warning:
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0, 1), message);
                    break;
                case LogEventLevel.Error:
                    ImGui.TextColored(new Vector4(0.5f, 0, 0, 1), message);
                    break;
                default:
                    ImGui.Text(message);
                    break;
            }
        }
        

        DrawCustomizeParameters(tree.tree);
        DrawModelView(tree.tree, tree.logger);
        DrawAttachView(tree.tree, tree.logger);
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

    private void DrawAttachView(CharacterTree tree, ExportLogger logger)
    {
        if (tree.AttachedChildren != null)
        {
            ImGui.Text("Attached Children");
            foreach (var child in tree.AttachedChildren)
            {
                ImGui.Text($"Attached at: {child.Attach.Transform}");
                
                using var mainTable = ImRaii.Table("AttachedChildren", 1, ImGuiTableFlags.Borders);
                foreach (var model in child.Models)
                {
                    if (!DrawModel(model, logger)) continue;
                    ExportCts?.Cancel();
                    ExportCts = new();
                    ExportTask = ModelConverter.Export(
                        logger,
                        new ExportConfig
                        {
                            ExportType = ExportType.Gltf,
                            IncludeReaperEye = false,
                            OpenFolderWhenComplete = true
                        }, model, tree.Skeleton, tree.RaceCode ?? 0, tree.CustomizeParameter, ExportCts.Token);
                }
            }
        }
    }
    
    private void DrawModelView(CharacterTree tree, ExportLogger logger)
    {
        if (tree.Models.Count == 0)
        {
            ImGui.Text("No models found. Try refreshing.");
            return;
        }
        
        ImGui.Text("Models");
        using var mainTable = ImRaii.Table("Models", 1, ImGuiTableFlags.Borders);
        foreach (var model in tree.Models)
        {
            if (!DrawModel(model, logger)) continue;
            ExportCts?.Cancel();
            ExportCts = new();
            ExportTask = ModelConverter.Export(
                logger,
                new ExportConfig
                {
                    ExportType = ExportType.Gltf,
                    IncludeReaperEye = false,
                    OpenFolderWhenComplete = true
                }, model, tree.Skeleton, tree.RaceCode ?? 0, tree.CustomizeParameter, ExportCts.Token);
        }
    }

    private bool DrawModel(Model model, ExportLogger logger)
    {
        ImGui.TableNextColumn();
        using var modelNode = ImRaii.TreeNode($"{model.HandlePath}##{model.GetHashCode()}", ImGuiTreeNodeFlags.CollapsingHeader);
        if (!modelNode.Success) return false;
        
        // Export icon
        using (var d = ImRaii.Disabled(!(ExportTask?.IsCompleted ?? true)))
        {
            if (ImGui.SmallButton($"Export##{model.GetHashCode()}") && (ExportTask?.IsCompleted ?? true))
            {
                return true;
            }
        }

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
            for (var j = 0; j < mesh.SubMeshes.Count; j++)
            {
                var submesh = mesh.SubMeshes[j];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"[{j}] Submesh attributes: {string.Join(", ", submesh.Attributes)}");
                ImGui.TableNextColumn();
                ImGui.TableNextColumn(); // Leave an empty column for spacing
            }
        }

        return false;
    }
}
