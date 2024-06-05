﻿using System.Numerics;
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
        ImGui.Text($"Shader Package Name: {file.GetShaderPackageName()}");

        ImGui.Text("UV Color Sets:");
        foreach (var (key, value) in file.GetUvColorSetStrings())
        {
            ImGui.Text($"[{key:X4}] {value}");
        }

        ImGui.Text("Color Sets:");
        foreach (var (key, value) in file.GetColorSetStrings())
        {
            ImGui.Text($"[{key:X4}] {value}");
        }
        
        ImGui.Text("Texture Paths:");
        var texturePaths = file.GetTexturePaths();
        for (var i = 0; i < file.TextureOffsets.Length; i++)
        {
            var off = file.TextureOffsets[i];
            var path = texturePaths[off.Offset];
            ImGui.Text($"[{i}] {path}");
        }

        if (ImGui.CollapsingHeader("Textures"))
        {
            foreach (var (key, value) in texturePaths)
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
        
        if (ImGui.CollapsingHeader("Shader Values"))
        {
            ImGui.Text($"Shader Keys [{file.ShaderKeys.Length}]");
            for (var i = 0; i < file.ShaderKeys.Length; i++)
            {
                var key = file.ShaderKeys[i];
                ImGui.Text($"[{i}][{key.Category:X4}] {key.Value:X4}");
            }

            ImGui.Text($"Constants [{file.Constants.Length}]");
            for (var i = 0; i < file.Constants.Length; i++)
            {
                var constant = file.Constants[i];
                var index = constant.ValueOffset / 4;
                var count = constant.ValueSize / 4;
                var buf = new List<byte>();
                for (var j = 0; j < count; j++)
                {
                    var value = file.ShaderValues[index + j];
                    var bytes = BitConverter.GetBytes(value);
                    buf.AddRange(bytes);
                }
                ImGui.Text($"[{i}][{constant.ConstantId:X4}|{constant.ConstantId}] off:{constant.ValueOffset:X2} size:{constant.ValueSize:X2} [{BitConverter.ToString(buf.ToArray())}]");
            }

            ImGui.Text($"Samplers [{file.Samplers.Length}]");
            for (var i = 0; i < file.Samplers.Length; i++)
            {
                var sampler = file.Samplers[i];
                ImGui.Text($"[{i}][{sampler.SamplerId:X4}|{sampler.SamplerId}] texIdx:{sampler.TextureIndex} flags:{sampler.Flags:X8}");
            }

            ImGui.Text($"Shader Values [{file.ShaderValues.Length}]");
            for (var i = 0; i < file.ShaderValues.Length; i++)
            {
                var value = file.ShaderValues[i];
                ImGui.Text($"[{i}]{value:X8}");
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
        
        ImGui.Columns(1);
    }
}
