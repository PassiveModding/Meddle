using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Models;

namespace Meddle.UI.Windows.Views;

public class ShpkView : IView
{
    private readonly ShpkFile file;
    private readonly ShaderPackage shaderPackage;
    private readonly HexView hexView;
    private readonly HexView remainingView;
    private Dictionary<ShpkFile.Shader, HexView> blobViews = new();

    public ShpkView(ShpkFile file, string? path)
    {
        this.file = file;
        this.hexView = new(file.RawData);
        this.remainingView = new(file.RemainingData);
        shaderPackage = new(file, path != null ? Path.GetFileName(path) : "Unknown");
    }
    
    private readonly Dictionary<ShpkFile.Shader, string> decompiledShader = new();

    private void DrawResourceTable(ShpkFile.Resource[] resources, ref SpanBinaryReader stringReader)
    {
        try
        {
            ImGui.Columns(4);
            foreach (var resource in resources)
            {
                var name = stringReader.ReadString((int)resource.StringOffset);
                ImGui.Text(name);
                if (ImGui.BeginPopupContextItem($"Copy##{resource.GetHashCode()}"))
                {
                    if (ImGui.Selectable($"Copy Name ({name})"))
                    {
                        ImGui.SetClipboardText(name);
                    }
                    if (ImGui.Selectable($"Copy Id ({resource.Id})"))
                    {
                        ImGui.SetClipboardText(resource.Id.ToString());
                    }
                    if (ImGui.Selectable($"Copy Id (0x{resource.Id:X8})"))
                    {
                        ImGui.SetClipboardText($"0x{resource.Id:X8}");
                    }
                    ImGui.EndPopup();
                }
                ImGui.NextColumn();
                ImGui.Text(resource.Slot.ToString());
                ImGui.NextColumn();
                ImGui.Text($"{resource.Id} (0x{resource.Id:X8})");
                ImGui.NextColumn();
                ImGui.Text($"Size: {resource.StringSize} Offset: {resource.StringOffset}");
                ImGui.NextColumn();
            }
        } finally
        {
            ImGui.Columns(1);
        }
    }
    
    private void DrawShader(ShpkFile.Shader shader)
    {
        
        if (ImGui.CollapsingHeader($"Blob##{shader.GetHashCode()}"))
        {
            // export blob to file button
            if (ImGui.Button($"Export##btn_{shader.GetHashCode()}"))
            {
                var path = Path.Combine("blobs", $"{shader.GetHashCode()}.blob");
                Directory.CreateDirectory("blobs");
                File.WriteAllBytes(path, shader.Blob);
            }
            
            if (ImGui.Button($"Disassemble##btn_{shader.GetHashCode()}"))
            {
                var disassembled = ShaderUtils.Disassemble(shader.Blob);
                decompiledShader[shader] = disassembled;
            }
                    
            if (decompiledShader.TryGetValue(shader, out var value))
            {
                // multiline textbox
                ImGui.InputTextMultiline($"##{shader.GetHashCode()}", ref value, 0, new Vector2(0, 600));
                // copy button
                if (ImGui.Button($"Copy##{shader.GetHashCode()}"))
                {
                    ImGui.SetClipboardText(value);
                }
            }
            
            if (ImGui.CollapsingHeader($"Hex Dump##{shader.GetHashCode()}"))
            {
                if (!blobViews.TryGetValue(shader, out var view))
                {
                    view = new HexView(shader.Blob);
                    blobViews[shader] = view;
                }
                view.DrawHexDump();
            }
        }

        try
        {
            ImGui.Columns(4);
            ImGui.Text("Name");
            ImGui.NextColumn();
            ImGui.Text("Slot");
            ImGui.NextColumn();
            ImGui.Text("Id");
            ImGui.NextColumn();
            ImGui.Text("String");
            ImGui.NextColumn();
            ImGui.Columns(1);
            
            var stringReader = new SpanBinaryReader(file.RawData[(int)file.FileHeader.StringsOffset..]);

            if (shader.Constants.Length > 0)
            {
                ImGui.SeparatorText($"Constants ({shader.Definition.ConstantCount})");
                DrawResourceTable(shader.Constants, ref stringReader);
            }

            if (shader.Samplers.Length > 0)
            {
                ImGui.SeparatorText($"Samplers ({shader.Definition.SamplerCount})");
                DrawResourceTable(shader.Samplers, ref stringReader);
            }

            if (shader.Uavs.Length > 0)
            {
                ImGui.SeparatorText($"UAVs ({shader.Definition.UavCount})");
                DrawResourceTable(shader.Uavs, ref stringReader);
            }

            if (shader.Textures.Length > 0)
            {
                ImGui.SeparatorText($"Textures ({shader.Definition.TextureCount})");
                DrawResourceTable(shader.Textures, ref stringReader);
            }
        } 
        finally
        {
            ImGui.Columns(1);
        }
    }
    
    public void Draw()
    {
        ImGui.Text($"Version: {file.FileHeader.Version} [{(uint)file.FileHeader.Version:X8}]");
        ImGui.Text($"DX: {file.FileHeader.DxVersion}");
        ImGui.Text($"Length: {file.FileHeader.Length}");
        ImGui.Text($"Blobs Offset: {file.FileHeader.BlobsOffset:X8}");
        ImGui.Text($"Strings Offset: {file.FileHeader.StringsOffset:X8}");
        ImGui.Text($"Vertex Shader Count: {file.FileHeader.VertexShaderCount}");
        ImGui.Text($"Pixel Shader Count: {file.FileHeader.PixelShaderCount}");
        ImGui.Text($"Material Params Size: {file.FileHeader.MaterialParamsSize}");
        ImGui.Text($"Material Param Count: {file.FileHeader.MaterialParamCount}");
        ImGui.Text($"Has Mat Param Defaults: {file.FileHeader.HasMatParamDefaults}");
        ImGui.Text($"Constant Count: {file.FileHeader.ConstantCount}");
        ImGui.Text($"Unk1: {file.FileHeader.Unk1}");
        ImGui.Text($"Sampler Count: {file.FileHeader.SamplerCount}");
        ImGui.Text($"Texture Count: {file.FileHeader.TextureCount}");
        ImGui.Text($"Uav Count: {file.FileHeader.UavCount}");
        ImGui.Text($"System Key Count: {file.FileHeader.SystemKeyCount}");
        ImGui.Text($"Scene Key Count: {file.FileHeader.SceneKeyCount}");
        ImGui.Text($"Material Key Count: {file.FileHeader.MaterialKeyCount}");
        ImGui.Text($"Node Count: {file.FileHeader.NodeCount}");
        ImGui.Text($"Node Alias Count: {file.FileHeader.NodeAliasCount}");
        
        ImGui.Text($"Shader Package Name: {shaderPackage.Name}");

        if (ImGui.CollapsingHeader("Texture Lookup"))
        {
            foreach (var texture in shaderPackage.TextureLookup)
            {
                ImGui.Text($"[{texture.Key:X8}] {texture.Value}");
            }
        }

        if (shaderPackage.ResourceKeys != null && ImGui.CollapsingHeader("Resource Keys"))
        {
            foreach (var resource in shaderPackage.ResourceKeys)
            {
                ImGui.Text($"[{resource.Key:X8}] {resource.Value}");
            }
        }

        // disassemble all button
        if (ImGui.Button("Disassemble All"))
        {
            var disassembledVertexShaders = new Dictionary<int, string>();
            for (var i = 0; i < file.VertexShaders.Length; i++)
            {
                disassembledVertexShaders[i] = ShaderUtils.Disassemble(file.VertexShaders[i].Blob);
            }
            
            File.WriteAllText($"vertex_shaders_{Path.GetFileNameWithoutExtension(shaderPackage.Name)}.txt", string.Join("\n\n", disassembledVertexShaders.Values));
            
            var disassembledPixelShaders = new Dictionary<int, string>();
            for (var i = 0; i < file.PixelShaders.Length; i++)
            {
                disassembledPixelShaders[i] = ShaderUtils.Disassemble(file.PixelShaders[i].Blob);
                File.WriteAllText($"pixel_shaders_{Path.GetFileNameWithoutExtension(shaderPackage.Name)}_{i}.txt", disassembledPixelShaders[i]);
            }
            
            File.WriteAllText($"pixel_shaders_{Path.GetFileNameWithoutExtension(shaderPackage.Name)}.txt", string.Join("\n\n", disassembledPixelShaders.Values));
        }
        
        if (ImGui.CollapsingHeader("Vertex Shaders"))
        {
            ImGui.Indent();
            for (var i = 0; i < file.VertexShaders.Length; i++)
            {
                var shader = file.VertexShaders[i];
                if (ImGui.CollapsingHeader($"Vertex Shader {i}"))
                {
                    try
                    {
                        ImGui.Indent();
                        DrawShader(shader);
                    } finally
                    {
                        ImGui.Unindent();
                    }
                }
            }
            ImGui.Unindent();
        }
        
        if (ImGui.CollapsingHeader("Pixel Shaders"))
        {
            ImGui.Indent();
            for (var i = 0; i < file.PixelShaders.Length; i++)
            {
                var shader = file.PixelShaders[i];
                if (ImGui.CollapsingHeader($"Pixel Shader {i}"))
                {
                    try
                    {
                        ImGui.Indent();
                        DrawShader(shader);
                    } finally
                    {
                        ImGui.Unindent();
                    }
                }
            }
            ImGui.Unindent();
        }
        
        try
        {
            ImGui.Columns(4);
            ImGui.Text("Name");
            ImGui.NextColumn();
            ImGui.Text("Slot");
            ImGui.NextColumn();
            ImGui.Text("Id");
            ImGui.NextColumn();
            ImGui.Text("String");
            ImGui.NextColumn();
            ImGui.Columns(1);
            
            var stringReader = new SpanBinaryReader(file.RawData[(int)file.FileHeader.StringsOffset..]);

            if (file.Constants.Length > 0)
            {
                ImGui.SeparatorText($"Constants ({file.Constants.Length})");
                DrawResourceTable(file.Constants, ref stringReader);
            }

            if (file.Samplers.Length > 0)
            {
                ImGui.SeparatorText($"Samplers ({file.Samplers.Length})");
                DrawResourceTable(file.Samplers, ref stringReader);
            }
            
            if (file.Uavs.Length > 0)
            {
                ImGui.SeparatorText($"UAVs ({file.Uavs.Length})");
                DrawResourceTable(file.Uavs, ref stringReader);
            }
            
            if (file.Textures.Length > 0)
            {
                ImGui.SeparatorText($"Textures ({file.Textures.Length})");
                DrawResourceTable(file.Textures, ref stringReader);
            }
        } 
        finally
        {
            ImGui.Columns(1);
        }
        

        if (ImGui.CollapsingHeader("Keys"))
        {
            ImGui.Columns(2);
            ImGui.Text("Id");
            ImGui.NextColumn();
            ImGui.Text("DefaultValue");
            ImGui.NextColumn();
            ImGui.Columns(1);
            ImGui.SeparatorText($"SystemKeys ({file.SystemKeys.Length})");
            DrawKeys(file.SystemKeys);
            
            ImGui.SeparatorText($"SceneKeys ({file.SceneKeys.Length})");
            DrawKeys(file.SceneKeys);
            
            ImGui.SeparatorText($"MaterialKeys ({file.MaterialKeys.Length})");
            DrawKeys(file.MaterialKeys);
            
            ImGui.SeparatorText($"SubViewKeys ({file.SubViewKeys.Length})");
            DrawKeys(file.SubViewKeys);
        }
        
        if (ImGui.CollapsingHeader("Material Params"))
        {
            if (ImGui.Button("Copy"))
            {
                var sb = new StringBuilder();
                foreach (var materialParam in file.MaterialParams)
                {
                    var idString = Enum.IsDefined((MaterialConstant)materialParam.Id) ? $"{(MaterialConstant)materialParam.Id}" : $"0x{materialParam.Id:X8}";
                    var defaults = file.MaterialParamDefaults
                                       .Skip(materialParam.ByteOffset / 4).Take(materialParam.ByteSize / 4)
                                       .ToArray();
                    var msg = $"[{string.Join(", ", defaults.Select(x => $"{x}"))}]";
                    sb.AppendLine($"{idString} = {msg}");
                }
                ImGui.SetClipboardText(sb.ToString());
            }
            
            if (ImGui.BeginTable("MaterialParams", 5, ImGuiTableFlags.Sortable | ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Idx", ImGuiTableColumnFlags.WidthFixed, 0.0f, 0);
                ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("ByteOffset", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("ByteSize", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Defaults", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                
                var orderedMaterialParams = file.MaterialParams.Select((x, idx) => (x, idx)).ToArray();
                var sortSpecs = ImGui.TableGetSortSpecs();
                var spec = sortSpecs.Specs;
                var column = spec.ColumnIndex;
                var descending = spec.SortDirection == ImGuiSortDirection.Descending;
                orderedMaterialParams = column switch
                {
                    0 => descending
                             ? orderedMaterialParams.OrderByDescending(x => x.idx).ToArray()
                             : orderedMaterialParams.OrderBy(x => x.idx).ToArray(),
                    1 => descending
                             ? orderedMaterialParams.OrderByDescending(x => x.x.Id).ToArray()
                             : orderedMaterialParams.OrderBy(x => x.x.Id).ToArray(),
                    2 => descending
                             ? orderedMaterialParams.OrderByDescending(x => x.x.ByteOffset).ToArray()
                             : orderedMaterialParams.OrderBy(x => x.x.ByteOffset).ToArray(),
                    3 => descending
                             ? orderedMaterialParams.OrderByDescending(x => x.x.ByteSize).ToArray()
                             : orderedMaterialParams.OrderBy(x => x.x.ByteSize).ToArray(),
                    _ => orderedMaterialParams
                };

                foreach (var (materialParam, i) in orderedMaterialParams)
                {
                    // get defaults from byteoffset -> byteoffset + bytesize
                    var defaults = file.MaterialParamDefaults.Skip((int)materialParam.ByteOffset / 4).Take((int)materialParam.ByteSize / 4).ToArray();
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text($"0x{materialParam.Id:X8}");
                    if (Enum.IsDefined((MaterialConstant)materialParam.Id))
                    {
                        ImGui.SameLine();
                        ImGui.Text($"({(MaterialConstant)materialParam.Id})");
                    }
                    ImGui.TableNextColumn();
                    ImGui.Text(materialParam.ByteOffset.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(materialParam.ByteSize.ToString());
                    ImGui.TableNextColumn();
                    var msg = defaults.Length switch
                    {
                        0 => "None",
                        1 => $"{defaults[0]} (0x{BitConverter.SingleToInt32Bits(defaults[0]):X8})",
                        _ => $"[{string.Join(", ", defaults.Select(x => $"{x}"))}] ({string.Join(", ", defaults.Select(x => $"0x{BitConverter.SingleToInt32Bits(x):X8}"))})"
                    };
                    ImGui.Text(msg);
                }
                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader("Material Params (cbuf)"))
        {
            var defaults = file.MaterialParamDefaults;
            for (var i = 0; i < defaults.Length / 4; i++)
            {
                var elements = defaults.Skip(i * 4).Take(4).ToArray();
                var v4 = new Vector4(elements[0], elements[1], elements[2], elements[3]);
                ImGui.Text($"[{i}] {v4}");
            }
        }
        
        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
        if (ImGui.CollapsingHeader("Remaining Data"))
        {
            remainingView.DrawHexDump();
        }
    }
    
    private void DrawKeys(ShpkFile.Key[] keys)
    {
        foreach (var key in keys)
        {
            ImGui.Columns(2);
            ImGui.Text(key.Id.ToString());
            ImGui.NextColumn();
            ImGui.Text($"{key.DefaultValue} (0x{key.DefaultValue:X8})");
            ImGui.NextColumn();
            ImGui.Columns(1);
        }
    }
}
