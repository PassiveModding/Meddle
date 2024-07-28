﻿using System.Numerics;
using ImGuiNET;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using CustomizeData = Meddle.Utils.Export.CustomizeData;

namespace Meddle.Plugin.Utils;

public static class UIUtil
{
    public static void DrawCustomizeParams(ref CustomizeParameter customize)
    {
        ImGui.ColorEdit3("Skin Color", ref customize.SkinColor);
        ImGui.ColorEdit4("Lip Color", ref customize.LipColor);
        ImGui.ColorEdit3("Main Color", ref customize.MainColor);
        ImGui.ColorEdit3("Mesh Color", ref customize.MeshColor);
        ImGui.ColorEdit4("Left Color", ref customize.LeftColor);
        ImGui.BeginDisabled();
        ImGui.ColorEdit4("Right Color", ref customize.RightColor);
        //ImGui.ColorEdit3("Hair Fresnel Value", ref customize.HairFresnelValue0);
        //ImGui.DragFloat("Muscle Tone", ref customize.MuscleTone, 0.01f, 0f, 1f);
        //ImGui.ColorEdit4("Skin Fresnel Value", ref customize.SkinFresnelValue0);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("?");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(
                "Right Eye Color will not apply to computed textures as it is selected using the vertex shaders");
            ImGui.EndTooltip();
        }

        ImGui.ColorEdit3("Option Color", ref customize.OptionColor);
    }

    public static void DrawCustomizeData(CustomizeData customize)
    {
        ImGui.Checkbox("Lipstick", ref customize.LipStick);
        ImGui.Checkbox("Highlights", ref customize.Highlights);
    }

    public static void DrawColorTable(ColorTable table, ColorDyeTable? dyeTable = null)
    {
        if (ImGui.BeginTable("ColorTable", 9, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Row", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Diffuse", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Specular", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Emissive", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Material Repeat", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Material Skew", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Specular Strength", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Gloss", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Tile Set", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableHeadersRow();

            for (var i = 0; i < table.Rows.Length; i++)
            {
                DrawRow(i, table, dyeTable);
            }

            ImGui.EndTable();
        }
    }

    public static void DrawColorTable(MtrlFile file)
    {
        ImGui.Text($"Color Table: {file.HasTable}");
        ImGui.Text($"Dye Table: {file.HasDyeTable}");
        ImGui.Text($"Extended Color Table: {file.LargeColorTable}");
        if (!file.HasTable)
        {
            return;
        }

        DrawColorTable(file.ColorTable, file.HasDyeTable ? file.ColorDyeTable : null);
    }

    private static void DrawRow(int i, ColorTable table, ColorDyeTable? dyeTable)
    {
        ref var row = ref table.Rows[i];
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text($"{i}");
        ImGui.TableSetColumnIndex(1);
        ImGui.ColorButton("##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
        if (dyeTable != null)
        {
            ImGui.SameLine();
            var diff = dyeTable.Value[i].Diffuse;
            ImGui.Checkbox("##rowdiff", ref diff);
        }

        ImGui.TableSetColumnIndex(2);
        ImGui.ColorButton("##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
        if (dyeTable != null)
        {
            ImGui.SameLine();
            var spec = dyeTable.Value[i].Specular;
            ImGui.Checkbox("##rowspec", ref spec);
        }

        ImGui.TableSetColumnIndex(3);
        ImGui.ColorButton("##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
        if (dyeTable != null)
        {
            ImGui.SameLine();
            var emm = dyeTable.Value[i].Emissive;
            ImGui.Checkbox("##rowemm", ref emm);
        }

        ImGui.TableSetColumnIndex(4);
        ImGui.Text($"{row.MaterialRepeat}");
        ImGui.TableSetColumnIndex(5);
        ImGui.Text($"{row.MaterialSkew}");
        ImGui.TableSetColumnIndex(6);
        if (dyeTable != null)
        {
            var specStrength = dyeTable.Value[i].SpecularStrength;
            ImGui.Checkbox("##rowspecstr", ref specStrength);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.SpecularStrength}");
        ImGui.TableSetColumnIndex(7);
        if (dyeTable != null)
        {
            var gloss = dyeTable.Value[i].Gloss;
            ImGui.Checkbox("##rowgloss", ref gloss);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.GlossStrength}");
        ImGui.TableSetColumnIndex(8);
        ImGui.Text($"{row.TileIndex}");
    }
}
