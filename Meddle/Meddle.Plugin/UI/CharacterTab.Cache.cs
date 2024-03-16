using System.Diagnostics;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System.Numerics;
using Meddle.Plugin.Enums;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Config;
using Meddle.Plugin.Utility;
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
            if (character != CharacterTreeCache.Value.character || 
                character.ObjectId != CharacterTreeCache.Value.character.ObjectId)
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
            CharacterTreeCache = (character, tree, new ExportLogger(Log), now);
        }
        
        return CharacterTreeCache.Value;
    }
    
    private void DrawCharacterTree(Character character)
    {
        var tree = InitTree(character, false);
        
        ImGui.Text($"Character: {tree.tree.Name}");
        ImGui.Text($"At: {tree.time}");

        using (var d = ImRaii.Disabled(ModelConverter.IsExporting))
        {
            if (ImGui.Button("Export") && !ModelConverter.IsExporting)
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

        if (ModelConverter.IsExporting)
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
        
        ImGui.SameLine();
        using (var d = ImRaii.Disabled(ModelConverter.IsExporting))
        {
            if (ImGui.Button("Refresh") && !ModelConverter.IsExporting)
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
        
        var sk3 = new Vector3(c.SkinColor.X, c.SkinColor.Y, c.SkinColor.Z);
        if (DrawColorEdit3("Skin Color", sk3, out var sc3))
        {
            c.SkinColor = new(sc3.X, sc3.Y, sc3.Z, c.SkinColor.W);
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
            result = new(v.X, v.Y, v.Z);
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
            result = new(v[0], v[1], v[2], v[3]);
            return true;
        }

        result = vec;
        return false;
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
                    if (!DrawModel(model)) continue;
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
            if (!DrawModel(model)) continue;
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

    private bool DrawModel(Model model)
    {
        ImGui.TableNextColumn();
        using var modelNode = ImRaii.TreeNode($"{model.HandlePath}##{model.GetHashCode()}", ImGuiTreeNodeFlags.CollapsingHeader);
        if (!modelNode.Success) return false;
        
        // Export icon
        using (var d = ImRaii.Disabled(ModelConverter.IsExporting))
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
        using (var table = ImRaii.Table("MaterialsTable", 2, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthFixed,
                                   0.25f * ImGui.GetWindowWidth());

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

                if (material.ColorTable != null)
                {
                    var diffuses = material.ColorTable.Rows.Select(x => x.Diffuse).ToArray();
                    var speculars = material.ColorTable.Rows.Select(x => x.Specular).ToArray();
                    var specularStrengths = material.ColorTable.Rows.Select(x => x.SpecularStrength).ToArray();
                    var emissives = material.ColorTable.Rows.Select(x => x.Emissive).ToArray();

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    for (var i = 0; i < diffuses.Length; i++)
                    {
                        if (i > 0)
                        {
                            ImGui.SameLine();
                        }
                        var diffuse = diffuses[i];
                        ImGui.ColorButton("##Diffuse", new Vector4(diffuse.X, diffuse.Y, diffuse.Z, 1), ImGuiColorEditFlags.NoAlpha);
                    }
                    ImGui.TableNextColumn();
                    ImGui.Text("ColorTable Diffuse");
                    
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    for (var i = 0; i < speculars.Length; i++)
                    {
                        if (i > 0)
                        {
                            ImGui.SameLine();
                        }
                        var specular = speculars[i];
                        ImGui.ColorButton("##Specular", new Vector4(specular.X, specular.Y, specular.Z, 1), ImGuiColorEditFlags.NoAlpha);
                    }
                    ImGui.TableNextColumn();
                    ImGui.Text("ColorTable Specular");
                    
                    /*
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    for (var i = 0; i < specularStrengths.Length; i++)
                    {
                        if (i > 0)
                        {
                            ImGui.SameLine();
                        }
                        var specularStrength = specularStrengths[i];
                        ImGui.Text($"{specularStrength}");
                    }
                    ImGui.TableNextColumn();
                    */
                    
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    for (var i = 0; i < emissives.Length; i++)
                    {
                        if (i > 0)
                        {
                            ImGui.SameLine();
                        }
                        var emissive = emissives[i];
                        ImGui.ColorButton("##Emissive", new Vector4(emissive.X, emissive.Y, emissive.Z, 1), ImGuiColorEditFlags.NoAlpha);
                    }
                    ImGui.TableNextColumn();
                    ImGui.Text("ColorTable Emissive");
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
