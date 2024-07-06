using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Plugin.Utils;

public static class UIUtil
{
    public static bool DrawCustomizeParams(ref CustomizeParameter customize)
    {
        bool updated = false;
        if (ImGui.ColorEdit4("Skin Color", ref customize.SkinColor))
        {
            updated = true;
        }
        if (ImGui.ColorEdit4("Skin Fresnel Value", ref customize.SkinFresnelValue0))
        {
            updated = true;
        }
        if (ImGui.ColorEdit4("Lip Color", ref customize.LipColor))
        {
            updated = true;
        }
        if (ImGui.ColorEdit3("Main Color", ref customize.MainColor))
        {
            updated = true;
        }
        if (ImGui.ColorEdit3("Hair Fresnel Value", ref customize.HairFresnelValue0))
        {
            updated = true;
        }
        if (ImGui.ColorEdit3("Mesh Color", ref customize.MeshColor))
        {
            updated = true;
        }
        if (ImGui.ColorEdit4("Left Color", ref customize.LeftColor))
        {
            updated = true;
        }
        if (ImGui.ColorEdit4("Right Color", ref customize.RightColor))
        {
            updated = true;
        }
        if (ImGui.ColorEdit3("Option Color", ref customize.OptionColor))
        {
            updated = true;
        }
        
        return updated;
    }
    
    public static void DrawCustomizeData(CustomizeData customize)
    {
        var lipstick = customize.Lipstick;
        ImGui.Checkbox("Lipstick", ref lipstick);
        var highlights = customize.Highlights;
        ImGui.Checkbox("Highlights", ref highlights);
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
