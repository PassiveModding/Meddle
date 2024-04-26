using ImGuiNET;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Models;

namespace Meddle.UI.Windows.Views;

public class MtrlView : IView
{
    private readonly MtrlFile file;
    private readonly SqPack pack;
    private readonly ImageHandler imageHandler;
    private readonly Dictionary<string, TexView?> mtrlTextureCache = new();

    public MtrlView(MtrlFile file, SqPack pack, ImageHandler imageHandler)
    {
        this.file = file;
        this.pack = pack;
        this.imageHandler = imageHandler;
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
        
        ImGui.Text("Textures:"); 
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
}
