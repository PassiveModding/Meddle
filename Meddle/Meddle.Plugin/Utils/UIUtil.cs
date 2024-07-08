using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using ImGuiNET;
using Meddle.Utils;
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
        ImGui.ColorEdit4("Right Color", ref customize.RightColor);
        ImGui.ColorEdit3("Option Color", ref customize.OptionColor);
    }
    
    public static void DrawCustomizeData(CustomizeData customize)
    {
        ImGui.Checkbox("Lipstick", ref customize.LipStick);
        ImGui.Checkbox("Highlights", ref customize.Highlights);
    }
    
    public static void DrawColorTable(MaterialResourceHandle.ColorTableRow[] rows)
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

        for (var i = 0; i < (file.LargeColorTable ? ColorTable.NumRows : ColorTable.LegacyNumRows); i++)
        {
            if (file.LargeColorTable)
            {
                DrawRow(i, file);
            }
            else
            {
                DrawLegacyRow(i, file);
            }
        }

        ImGui.Columns(1);
    }

    private static void DrawRow(int i, MtrlFile file)
    {
        ref var row = ref file.ColorTable.GetRow(i);
        ImGui.Text($"{i}");
        ImGui.NextColumn();
        ImGui.ColorButton($"##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var diff = file.ColorDyeTable[i].Diffuse;
            ImGui.Checkbox($"##rowdiff", ref diff);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var spec = file.ColorDyeTable[i].Specular;
            ImGui.Checkbox($"##rowspec", ref spec);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var emm = file.ColorDyeTable[i].Emissive;
            ImGui.Checkbox($"##rowemm", ref emm);
        }

        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialRepeat}");
        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialSkew}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var specStr = file.ColorDyeTable[i].SpecularStrength;
            ImGui.Checkbox($"##rowspecstr", ref specStr);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.SpecularStrength}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var gloss = file.ColorDyeTable[i].Gloss;
            ImGui.Checkbox($"##rowgloss", ref gloss);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.GlossStrength}");
        ImGui.NextColumn();
        ImGui.Text($"{row.TileSet}");
        ImGui.NextColumn();
    }

    private static void DrawLegacyRow(int i, MtrlFile file)
    {
        ref var row = ref file.ColorTable.GetLegacyRow(i);
        ImGui.Text($"{i}");
        ImGui.NextColumn();
        ImGui.ColorButton($"##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var diff = file.ColorDyeTable[i].Diffuse;
            ImGui.Checkbox($"##rowdiff", ref diff);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var spec = file.ColorDyeTable[i].Specular;
            ImGui.Checkbox($"##rowspec", ref spec);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var emm = file.ColorDyeTable[i].Emissive;
            ImGui.Checkbox($"##rowemm", ref emm);
        }

        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialRepeat}");
        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialSkew}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var specStr = file.ColorDyeTable[i].SpecularStrength;
            ImGui.Checkbox($"##rowspecstr", ref specStr);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.SpecularStrength}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var gloss = file.ColorDyeTable[i].Gloss;
            ImGui.Checkbox($"##rowgloss", ref gloss);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.GlossStrength}");
        ImGui.NextColumn();
        ImGui.Text($"{row.TileSet}");
        ImGui.NextColumn();
    }
    
}
