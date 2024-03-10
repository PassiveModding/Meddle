using Dalamud.Plugin.Services;
using Meddle.Plugin.Utility;
using OtterTex;
using Penumbra.GameData.Data;
using Penumbra.GameData.Files;
using SharpGLTF.Materials;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace Meddle.Plugin.Services;

public class ImageService : IService
{
    private readonly FileService _fileService;
    private readonly IPluginLog _log;

    public ImageService(FileService fileService, IPluginLog log)
    {
        _fileService = fileService;
        _log = log;
    }
    
    /// <summary>
    /// Convert a texture into an ImageSharp Image.
    /// </summary>
    public Image<Rgba32> ConvertImage(MtrlFile.Texture texture, string? forcedPath = null)
    {
        // Work out the texture's path - the DX11 material flag controls a file name prefix.
        GamePaths.Tex.HandleDx11Path(texture, out var texturePath);
        _log.Debug($"Resolved {texture.Path} -> {texturePath}");
        var bytes = _fileService.ReadFile(forcedPath ?? texturePath);
        if (bytes == null)
            return CreateDummyImage();

        
        using var textureData = new MemoryStream(bytes);
        var       ddsImage       = TexFileParser.Parse(textureData);
        var rgba = ddsImage.GetRGBA(out var f).ThrowIfError(f);
        var pixels = rgba.Pixels[..(f.Meta.Width * f.Meta.Height * (f.Meta.Format.BitsPerPixel() / 8))].ToArray();
        return Image.LoadPixelData<Rgba32>(pixels, f.Meta.Width, f.Meta.Height);
    }
        
    public Image<Rgba32> CreateDummyImage()
    {
        var image = new Image<Rgba32>(1, 1);
        image[0, 0] = Color.White;
        return image;
    }
}