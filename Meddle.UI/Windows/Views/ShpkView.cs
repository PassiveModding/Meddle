using System.Numerics;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Havok;

namespace Meddle.UI.Windows.Views;

public class ShpkView : IView
{
    private readonly ShpkFile file;
    private readonly HexView hexView;
    private readonly HexView remainingView;

    public ShpkView(ShpkFile file)
    {
        this.file = file;
        this.hexView = new(file.RawData);
        this.remainingView = new(file.RemainingData);
    }

    private void DrawShader(ShpkFile.Shader shader)
    {
        void DrawResource(ShpkFile.Resource resource, ref SpanBinaryReader r)
        {
            // ignore string size because wtf
            ImGui.Text($"Name: {r.ReadString((int)resource.StringOffset)}");
            ImGui.Text($"Slot: {resource.Slot}");
            ImGui.Text($"Id: {resource.Id}");
            ImGui.Text($"Size: {resource.Size}");
            ImGui.Text($"String Offset: {resource.StringOffset}");
            ImGui.Text($"String Size: {resource.StringSize}");
        }

        if (ImGui.CollapsingHeader($"Definition##{shader.GetHashCode()}"))
        {
            ImGui.Text($"Constant Count: {shader.Definition.ConstantCount}");
            ImGui.Text($"Sampler Count: {shader.Definition.SamplerCount}");
            ImGui.Text($"Uav Count: {shader.Definition.UavCount}");
            ImGui.Text($"Pad: {shader.Definition.Pad}");
            ImGui.Text($"Blob Offset?: {shader.Definition.BlobOffset}");
            ImGui.Text($"Blob Size: {shader.Definition.BlobSize}");
        }
        
        var stringReader = new SpanBinaryReader(file.Strings);
        
        if (ImGui.CollapsingHeader($"Constants ({shader.Constants.Length})##{shader.GetHashCode()}"))
        {
            for (var i = 0; i < shader.Constants.Length; i++)
            {
                var constant = shader.Constants[i];
                ImGui.Text($"Constant {i}");
                DrawResource(constant, ref stringReader);
            }
        }
        
        if (ImGui.CollapsingHeader($"Samplers ({shader.Samplers.Length})##{shader.GetHashCode()}"))
        {
            for (var i = 0; i < shader.Samplers.Length; i++)
            {
                var sampler = shader.Samplers[i];
                ImGui.Text($"Sampler {i}");
                DrawResource(sampler, ref stringReader);
            }
        }
        
        if (ImGui.CollapsingHeader($"Uavs ({shader.Uavs.Length})##{shader.GetHashCode()}"))
        {
            for (var i = 0; i < shader.Uavs.Length; i++)
            {
                var uav = shader.Uavs[i];
                ImGui.Text($"Uav {i}");
                DrawResource(uav, ref stringReader);
            }
        }
    }
    
    public void Draw()
    {
        ImGui.Text($"Version: {file.FileHeader.Version} [{(uint)file.FileHeader.Version:X8}]");
        ImGui.Text($"DX: {file.FileHeader.DxVersion}");
        
        if (ImGui.CollapsingHeader("Vertex Shaders"))
        {
            for (var i = 0; i < file.VertexShaders.Length; i++)
            {
                var shader = file.VertexShaders[i];
                ImGui.Text($"Vertex Shader {i}");
                DrawShader(shader);
            }
        }
        
        if (ImGui.CollapsingHeader("Pixel Shaders"))
        {
            for (var i = 0; i < file.PixelShaders.Length; i++)
            {
                var shader = file.PixelShaders[i];
                ImGui.Text($"Pixel Shader {i}");
                DrawShader(shader);
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
}
