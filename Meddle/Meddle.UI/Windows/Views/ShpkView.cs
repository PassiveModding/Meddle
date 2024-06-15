using System.Numerics;
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

    private void DrawShader(ShpkFile.Shader shader)
    {
        void DrawResourceTable(ShpkFile.Resource[] resources, ref SpanBinaryReader stringReader)
        {
            try
            {
                ImGui.Columns(4);
                foreach (var resource in resources)
                {
                    var name = stringReader.ReadString((int)resource.StringOffset);
                    ImGui.Text(name);
                    ImGui.NextColumn();
                    ImGui.Text(resource.Slot.ToString());
                    ImGui.NextColumn();
                    ImGui.Text(resource.Id.ToString());
                    // click to copy
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Right click to copy");
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        ImGui.SetClipboardText(resource.Id.ToString());
                    }
                    ImGui.NextColumn();
                    ImGui.Text($"Size: {resource.StringSize} Offset: {resource.StringOffset}");
                    ImGui.NextColumn();
                }
            } finally
            {
                ImGui.Columns(1);
            }
        }
        
        if (ImGui.CollapsingHeader($"Blob##{shader.GetHashCode()}"))
        {
            if (ImGui.Button($"Disassemble##btn_{shader.GetHashCode()}"))
            {
                decompiledShader[shader] = ShaderUtils.Disassemble(shader.Blob);
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
        foreach (var texture in shaderPackage.TextureLookup)
        {
            ImGui.Text($"[{texture.Key:X8}] {texture.Value}");
        }

        if (shaderPackage.ResourceKeys != null)
        {
            foreach (var resource in shaderPackage.ResourceKeys)
            {
                ImGui.Text($"[{resource.Key:X8}] {resource.Value}");
            }
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
        
        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
        if (ImGui.CollapsingHeader("Remaining Data"))
        {
            remainingView.DrawHexDump();
        }
    }
}
