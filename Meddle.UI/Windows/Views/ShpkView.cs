using System.Numerics;
using ImGuiNET;
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
        for (var i = 0; i < shader.Constants.Length; i++)
        {
            var constant = shader.Constants[i];
            ImGui.Text($"Constant {i}");
            ImGui.Text($"  Name: {constant.String}");
            ImGui.Text($"  Slot: {constant.Slot}");
            ImGui.Text($"  Id: {constant.Id}");
        }
        
        for (var i = 0; i < shader.Samplers.Length; i++)
        {
            var sampler = shader.Samplers[i];
            ImGui.Text($"Sampler {i}");
            ImGui.Text($"  Name: {sampler.String}");
            ImGui.Text($"  Slot: {sampler.Slot}");
            ImGui.Text($"  Id: {sampler.Id}");
        }
        
        for (var i = 0; i < shader.Uavs.Length; i++)
        {
            var uav = shader.Uavs[i];
            ImGui.Text($"Uav {i}");
            ImGui.Text($"  Name: {uav.String}");
            ImGui.Text($"  Slot: {uav.Slot}");
            ImGui.Text($"  Id: {uav.Id}");
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
