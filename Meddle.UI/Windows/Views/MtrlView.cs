using System.Numerics;
using ImGuiNET;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Models;

namespace Meddle.UI.Windows.Views;

public class MtrlView : IView
{
    private readonly MtrlFile file;
    private readonly SqPack pack;
    private readonly ImageHandler imageHandler;
    private readonly Dictionary<string, TexView?> mtrlTextureCache = new();
    private readonly HexView hexView;

    public MtrlView(MtrlFile file, SqPack pack, ImageHandler imageHandler)
    {
        this.file = file;
        this.pack = pack;
        this.imageHandler = imageHandler;
        hexView = new HexView(file.RawData);
    }
    
    public void Draw()
    {
        ImGui.Text($"Material Version: {file.FileHeader.Version}");
        var material = new Material(file);
        ImGui.Text($"Shader Package Name: {material.ShaderPackageName}");

        ImGui.Text("UV Color Sets:");
        foreach (var (key, value) in material.UvColorSetStrings)
        {
            ImGui.Text($"[{key:X4}] {value}");
        }

        ImGui.Text("Color Sets:");
        foreach (var (key, value) in material.ColorSetStrings)
        {
            ImGui.Text($"[{key:X4}] {value}");
        }

        if (ImGui.CollapsingHeader("Textures"))
        {
            foreach (var (key, value) in material.TexturePaths)
            {
                ImGui.Text($"[{key:X4}] {value}");
                if (!mtrlTextureCache.TryGetValue(value, out var texView))
                {
                    var sqFile = pack.GetFile(value);
                    if (sqFile != null)
                    {
                        var texFile = new TexFile(sqFile.Value.file.RawData);
                        texView = new TexView(sqFile.Value.hash, texFile, imageHandler, value);
                        mtrlTextureCache.Add(value, texView);
                    }
                }

                texView?.Draw();
            }
        }
        
        if (ImGui.CollapsingHeader("Color Table"))
        {
            DrawColorTable();
        }
        
        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
    }

    private void DrawColorTable()
    {
        if (!file.HasTable)
        {
            ImGui.Text("No color table found.");
            return;
        }

        if (!file.HasDyeTable)
        {
            ImGui.Text("No dye table found.");
        }
        ImGui.Columns(9, "ColorTable", true);
        ImGui.SetColumnWidth(0, 40);
        ImGui.Text("Row");
        ImGui.NextColumn();
        ImGui.SetColumnWidth(1, 60);
        ImGui.Text("Diffuse");
        ImGui.NextColumn();
        ImGui.SetColumnWidth(2, 65);
        ImGui.Text("Specular");
        ImGui.NextColumn();
        ImGui.SetColumnWidth(3, 65);
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
        //foreach (var row in file.ColorTable)
        for (var i = 0; i < ColorTable.NumRows; i++)
        {
            ref var row = ref file.ColorTable[i];
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
            ImGui.Text($"{row.SpecularStrength}");
            if (file.HasDyeTable)
            {
                ImGui.SameLine();
                var specStr = file.ColorDyeTable[i].SpecularStrength;
                ImGui.Checkbox($"##rowspecstr", ref specStr);
            }
            ImGui.NextColumn();
            ImGui.Text($"{row.GlossStrength}");
            if (file.HasDyeTable)
            {
                ImGui.SameLine();
                var gloss = file.ColorDyeTable[i].Gloss;
                ImGui.Checkbox($"##rowgloss", ref gloss);
            }
            ImGui.NextColumn();
            ImGui.Text($"{row.TileSet}");
            ImGui.NextColumn();
        }
        
        ImGui.Columns(1);
    }
}
