using System.Numerics;
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
        ImGui.ColorEdit4("Skin Color", ref customize.SkinColor);
        ImGui.ColorEdit4("Skin Fresnel Value", ref customize.SkinFresnelValue0);
        ImGui.ColorEdit4("Lip Color", ref customize.LipColor);
        ImGui.ColorEdit3("Main Color", ref customize.MainColor);
        ImGui.ColorEdit3("Hair Fresnel Value", ref customize.HairFresnelValue0);
        ImGui.ColorEdit3("Mesh Color", ref customize.MeshColor);
        ImGui.ColorEdit4("Left Color", ref customize.LeftColor);
        ImGui.BeginDisabled();
        ImGui.ColorEdit4("Right Color", ref customize.RightColor);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("?");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Right Eye Color will not apply to computed textures as it is selected using the vertex shaders");
            ImGui.EndTooltip();
        }
        
        ImGui.ColorEdit3("Option Color", ref customize.OptionColor);
    }
    
    public static void DrawCustomizeData(CustomizeData customize)
    {
        ImGui.Checkbox("Lipstick", ref customize.LipStick);
        ImGui.Checkbox("Highlights", ref customize.Highlights);
    }
    
    /*public static void DrawColorTable(MaterialResourceHandle.ColorTableRow[] rows)
    {
        ImGui.Columns(9, "ColorTable", true);
        ImGui.Text("Row");
        ImGui.NextColumn();
        ImGui.Text("Diffuse");
        ImGui.NextColumn();
        ImGui.Text("Specular");
        ImGui.NextColumn();
        ImGui.Text("Emissive");
        ImGui.NextColumn();
        ImGui.Text("Material Repeat");
        ImGui.NextColumn();
        ImGui.Text("Material Skew");
        ImGui.NextColumn();
        ImGui.Text("Specular");
        ImGui.NextColumn();
        ImGui.Text("Gloss");
        ImGui.NextColumn();
        ImGui.Text("Tile Set");
        ImGui.NextColumn();

        for (var i = 0; i < rows.Length; i++)
        {
            ref var row = ref rows[i];
            ImGui.Text($"{i}");
            ImGui.NextColumn();
            ImGui.ColorButton($"##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
            ImGui.NextColumn();
            ImGui.ColorButton($"##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
            ImGui.NextColumn();
            ImGui.ColorButton($"##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
            ImGui.NextColumn();
            ImGui.Text($"{row.TileScaleU}");
            ImGui.NextColumn();
            ImGui.Text($"{row.TileScaleV}");
            ImGui.NextColumn();
            ImGui.Text($"{row.SpecularStrength}");
            ImGui.NextColumn();
            ImGui.Text($"{row.GlossStrength}");
            ImGui.NextColumn();
            ImGui.Text($"{row.TileIndex}");
            ImGui.NextColumn();
        }

        ImGui.Columns(1);
    }*/


    public static void DrawColorTable(ColorTable table, ColorDyeTable? dyeTable = null)
    {
        ImGui.Columns(9, "ColorTable", true);
        ImGui.Text("Row");
        ImGui.NextColumn();
        ImGui.Text("Diffuse");
        ImGui.NextColumn();
        ImGui.Text("Specular");
        ImGui.NextColumn();
        ImGui.Text("Emissive");
        ImGui.NextColumn();
        ImGui.Text("Material Repeat");
        ImGui.NextColumn();
        ImGui.Text("Material Skew");
        ImGui.NextColumn();
        ImGui.Text("Specular");
        ImGui.NextColumn();
        ImGui.Text("Gloss");
        ImGui.NextColumn();
        ImGui.Text("Tile Set");
        ImGui.NextColumn();

        for (var i = 0; i < ColorTable.NumRows; i++)
        {
            DrawRow(i, table, dyeTable);
        }

        ImGui.Columns(1);
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
        ref var row = ref table.GetRow(i);
        ImGui.Text($"{i}");
        ImGui.NextColumn();
        ImGui.ColorButton($"##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
        if (dyeTable != null)
        {
            ImGui.SameLine();
            var diff = dyeTable.Value[i].Diffuse;
            ImGui.Checkbox($"##rowdiff", ref diff);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
        if (dyeTable != null)
        {
            ImGui.SameLine();
            var spec = dyeTable.Value[i].Specular;
            ImGui.Checkbox($"##rowspec", ref spec);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
        if (dyeTable != null)
        {
            ImGui.SameLine();
            var emm = dyeTable.Value[i].Emissive;
            ImGui.Checkbox($"##rowemm", ref emm);
        }

        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialRepeat}");
        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialSkew}");
        ImGui.NextColumn();
        if (dyeTable != null)
        {
            var spec = dyeTable.Value[i].SpecularStrength;
            ImGui.Checkbox($"##rowspecstr", ref spec);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.SpecularStrength}");
        ImGui.NextColumn();
        if (dyeTable != null)
        {
            var gloss = dyeTable.Value[i].Gloss;
            ImGui.Checkbox($"##rowgloss", ref gloss);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.GlossStrength}");
        ImGui.NextColumn();
        ImGui.Text($"{row.TileIndex}");
        ImGui.NextColumn();
    }
}
