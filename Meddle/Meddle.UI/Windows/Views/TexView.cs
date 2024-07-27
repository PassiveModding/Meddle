using System.Numerics;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using OtterTex;

namespace Meddle.UI.Windows.Views;

public class TexView : IView
{
    public TexView(TexFile texFile, ImageHandler imageHandler, string? path)
    {
        this.texFile = texFile;
        this.imageHandler = imageHandler;
        this.path = path;
    }
    
    public record TexFileGroup(string Path, TexFile TexFile);
    private readonly TexFile texFile;
    private readonly Dictionary<(int arrayLevel, int mipLevel, int slice), (Image image, nint binding)> imageCache = new();
    private int arrayLevel;
    private int mipLevel;
    private int slice;
    private readonly ImageHandler imageHandler;
    private readonly string? path;

    public void Draw()
    {
        ImGui.Text($"Size: {texFile.Header.Width}x{texFile.Header.Height}");
        ImGui.Text($"Format: {texFile.Header.Format}");
        ImGui.Text($"Type: {texFile.Header.Type}");
        ImGui.Text($"Depth: {texFile.Header.Depth}");
        ImGui.SameLine();
        ImGui.Text($"Array Size: {texFile.Header.ArraySize}");
        ImGui.SameLine();
        ImGui.Text($"Mips: {texFile.Header.MipLevels}");

        var meta = ImageUtils.GetTexMeta(texFile);
        DrawTexOptions(meta);
        if (!imageCache.TryGetValue((arrayLevel, mipLevel, slice), out var val))
        {
            var image = ImageUtils.GetTexData(texFile, arrayLevel, mipLevel, slice);
            var binding = imageHandler.DrawTexData(image);
            val = (image, binding);
            imageCache.Add((arrayLevel, mipLevel, slice), val);
        }

        // keep aspect ratio and fit in the available space
        if (ImGui.Button($"Save frame as png##{texFile.GetHashCode()}"))
        {
            var data = ImageUtils.GetTexData(texFile, arrayLevel, mipLevel, slice);
            var outFolder = Path.Combine(Program.DataDirectory, "SqPackFiles");
            if (!Directory.Exists(outFolder))
            {
                Directory.CreateDirectory(outFolder);
            }

            string fileName = path != null ? Path.GetFileNameWithoutExtension(path) : "Unk";
            
            var outPath = Path.Combine(outFolder, $"{fileName}.png");
            File.WriteAllBytes(outPath, ImageUtils.ImageAsPng(data).ToArray());
        }

        var availableSize = ImGui.GetContentRegionAvail();
        var size = GetImageSize(meta, (int)availableSize.X);
        ImGui.Image(val.binding, size);
    }
    
    private Vector2 GetImageSize(TexMeta meta, int width)
    {
        Vector2 size = new(meta.Width, meta.Height);
        if (size.X > width)
        {
            size.Y = size.Y * width / size.X;
            size.X = width;
        }

        return size;
    }

    private void DrawTexOptions(TexMeta meta)
    {
        if (meta.ArraySize > 1)
        {
            ImGui.InputInt($"ArrayLevel##{texFile.GetHashCode()}", ref arrayLevel);
            if (this.arrayLevel >= meta.ArraySize)
            {
                arrayLevel = 0;
            }
            else if (this.arrayLevel < 0)
            {
                arrayLevel = Math.Max(0, meta.ArraySize - 1);
            }
        }
        else
        {
            arrayLevel = 0;
        }

        if (meta.MipLevels > 1)
        {
            ImGui.InputInt($"MipLevel##{texFile.GetHashCode()}", ref mipLevel);
            if (this.mipLevel >= meta.MipLevels)
            {
                mipLevel = 0;
            }
            else if (this.mipLevel < 0)
            {
                mipLevel = Math.Max(0, meta.MipLevels - 1);
            }
        }
        else
        {
            mipLevel = 0;
        }

        if (meta.Depth > 1)
        {
            ImGui.InputInt($"Slice##{texFile.GetHashCode()}", ref slice);
            if (this.slice >= meta.Depth)
            {
                slice = 0;
            }
            else if (this.slice < 0)
            {
                slice = Math.Max(0, meta.Depth - 1);
            }
        }
        else
        {
            slice = 0;
        }
    }
}
