using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utility;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.UI;

public unsafe partial class CharacterTab : ITab
{
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

    private static bool IsHuman(Character obj)
    {
        var drawObject = ((CSCharacter*)obj.Address)->GameObject.DrawObject;
        if (drawObject == null)
            return false;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
            return false;
        if (((CharacterBase*)drawObject)->GetModelType() != CharacterBase.ModelType.Human)
            return false;
        return true;
    }

    private static void DrawMesh(Mesh mesh)
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

    private static void DrawMaterial(Material material)
    {
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

    private static void DrawColorTable(ColorTable table)
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
