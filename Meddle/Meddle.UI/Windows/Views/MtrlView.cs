using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Meddle.UI.Models;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;

namespace Meddle.UI.Windows.Views;

public class MtrlView : IView
{
    private readonly MtrlFile file;
    private readonly HexView hexView;
    private readonly ImageHandler imageHandler;
    private readonly Material material;
    private readonly SqPack pack;
    private readonly ShpkFile shpkFile;
    private readonly ShpkView shpkView;
    private readonly Dictionary<string, TexFile> texFiles = new();
    private readonly Dictionary<string, TexView> texViews = new();

    public MtrlView(MtrlFile file, SqPack pack, ImageHandler imageHandler)
    {
        this.file = file;
        this.pack = pack;
        this.imageHandler = imageHandler;
        hexView = new HexView(file.RawData);
        foreach (var (key, value) in file.GetTexturePaths())
        {
            try
            {
                var sqFile = pack.GetFile(value);
                if (sqFile != null)
                {
                    var texFile = new TexFile(sqFile.Value.file.RawData);
                    texFiles.Add(value, texFile);
                    var texView = new TexView(texFile, imageHandler, value);
                    texViews.Add(value, texView);
                }
                else
                {
                    Console.WriteLine($"Failed to load texture {value}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to load texture {value}: {e.Message}");
            }
        }

        try
        {
            var path = $"shader/sm5/shpk/{file.GetShaderPackageName()}";
            var shpkSqFile = pack.GetFile(path);
            if (shpkSqFile != null)
            {
                shpkFile = new ShpkFile(shpkSqFile.Value.file.RawData);
                shpkView = new ShpkView(shpkFile, path);
                material = new Material("unknown.mdl", file, texFiles, shpkFile);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to load shader package {file.GetShaderPackageName()}: {e.Message}");
        }
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
            try
            {
                ImGui.Indent();
                foreach (var (path, view) in texViews)
                {
                    if (ImGui.CollapsingHeader(path))
                    {
                        ImGui.Text(path);

                        // get usage from material
                        var tex = material.Textures.FirstOrDefault(x => x.HandlePath == path);
                        if (tex != null)
                        {
                            ImGui.Text($"Usage: {tex.Usage}");
                        }

                        view.Draw();
                    }
                }
            } finally
            {
                ImGui.Unindent();
            }
        }

        if (ImGui.CollapsingHeader("Shader Package"))
        {
            try
            {
                ImGui.Indent();
                shpkView?.Draw();
            } finally
            {
                ImGui.Unindent();
            }
        }

        if (ImGui.CollapsingHeader("Shader Values"))
        {
            ImGui.Text($"Shader Keys [{file.ShaderKeys.Length}]");
            ImGui.BeginTable("ShaderKeys", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("Index");
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Category");
            ImGui.TableSetupColumn("Value");
            ImGui.TableHeadersRow();
            
            for (var i = 0; i < file.ShaderKeys.Length; i++)
            {
                var key = file.ShaderKeys[i];
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text($"{i}");
                ImGui.TableSetColumnIndex(1);
                if (Enum.IsDefined((ShaderCategory)key.Category))
                {
                    ImGui.Text($"{((ShaderCategory)key.Category).ToString()}");
                }
                ImGui.TableSetColumnIndex(2);
                ImGui.Text($"{key.Category:X4}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"0x{key.Category:X4}");
                    ImGui.EndTooltip();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.SetClipboardText($"0x{key.Category:X4}");
                }
                ImGui.TableSetColumnIndex(3);
                ImGui.Text($"0x{key.Value:X4}");
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"0x{key.Value:X4}");
                    ImGui.EndTooltip();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.SetClipboardText($"0x{key.Value:X4}");
                }
            }
            ImGui.EndTable();
            
            ImGui.Text($"Constants [{file.Constants.Length}]");
            ImGui.BeginTable("Constants", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("Index");
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Key");
            ImGui.TableSetupColumn("Value");
            ImGui.TableHeadersRow();
            for (var i = 0; i < file.Constants.Length; i++)
            {
                ImGui.TableNextRow();
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
                var floatBuf = MemoryMarshal.Cast<byte, float>(buf.ToArray()).ToArray();

                ImGui.TableSetColumnIndex(0);
                ImGui.Text($"{i}");
                ImGui.TableSetColumnIndex(1);
                if (Enum.IsDefined((MaterialConstant)constant.ConstantId))
                {
                    ImGui.Text($"{((MaterialConstant)constant.ConstantId).ToString()}");
                }
                ImGui.TableSetColumnIndex(2);
                ImGui.Text($"{constant.ConstantId:X4}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"0x{constant.ConstantId:X4}");
                    ImGui.EndTooltip();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.SetClipboardText($"0x{constant.ConstantId:X4}");
                }
                ImGui.TableSetColumnIndex(3);
                ImGui.Text($"{string.Join(", ", floatBuf)}");
            }
            ImGui.EndTable();

            ImGui.Text($"Samplers [{file.Samplers.Length}]");
            ImGui.BeginTable("Samplers", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("Index");
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Id");
            ImGui.TableSetupColumn("Flags");
            ImGui.TableHeadersRow();
            for (var i = 0; i < file.Samplers.Length; i++)
            {
                ImGui.TableNextRow();
                var sampler = file.Samplers[i];
                ImGui.TableSetColumnIndex(0);
                ImGui.Text($"{i}");
                ImGui.TableSetColumnIndex(1);
                if (Enum.IsDefined((TextureUsage)sampler.SamplerId))
                {
                    ImGui.Text($"{((TextureUsage)sampler.SamplerId).ToString()}");
                }
                ImGui.TableSetColumnIndex(2);
                ImGui.Text($"{sampler.SamplerId:X4}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"0x{sampler.SamplerId:X4}");
                    ImGui.EndTooltip();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.SetClipboardText($"0x{sampler.SamplerId:X4}");
                }
                ImGui.TableSetColumnIndex(3);
                ImGui.Text($"0x{sampler.Flags:X4}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"0x{sampler.Flags:X4}");
                    ImGui.EndTooltip();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.SetClipboardText($"0x{sampler.Flags:X4}");
                }
            }
            ImGui.EndTable();
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

        // for (var i = 0; i < file.ColorTable.Rows.Length; i++)
        // {
        //     if (file.LargeColorTable)
        //     {
        //         DrawRow(i);
        //     }
        //     else
        //     {
        //         ImGui.Text($"Legacy Row {i}");
        //         DrawRow(i);
        //     }
        // }

        ImGui.Columns(1);
    }

    /*private void DrawRow(int i)
    {
        ref var row = ref file.ColorTable.Rows[i];
        ImGui.Text($"{i}");
        ImGui.NextColumn();
        ImGui.ColorButton("##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var diff = file.ColorDyeTable[i].Diffuse;
            ImGui.Checkbox("##rowdiff", ref diff);
        }

        ImGui.NextColumn();
        ImGui.ColorButton("##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var spec = file.ColorDyeTable[i].Specular;
            ImGui.Checkbox("##rowspec", ref spec);
        }

        ImGui.NextColumn();
        ImGui.ColorButton("##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var emm = file.ColorDyeTable[i].Emissive;
            ImGui.Checkbox("##rowemm", ref emm);
        }

        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialRepeat}");
        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialSkew}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var specStr = file.ColorDyeTable[i].SpecularStrength;
            ImGui.Checkbox("##rowspecstr", ref specStr);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.SpecularStrength}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var gloss = file.ColorDyeTable[i].Gloss;
            ImGui.Checkbox("##rowgloss", ref gloss);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.GlossStrength}");
        ImGui.NextColumn();
        ImGui.Text($"{row.TileIndex}");
        ImGui.NextColumn();
    }*/
}
