using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Meddle.Plugin.Models.Composer;

public static class ArrayTextureUtil
{
    private static string GetOutDir(string cacheDir)
    {
        var outDir = Path.Combine(cacheDir, "array_textures");
        Directory.CreateDirectory(outDir);
        return outDir;
    }
    
    public static void SaveSphereTextures(SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        var catchlight = pack.GetFileOrReadFromDisk("chara/common/texture/sphere_d_array.tex");
        if (catchlight == null) throw new Exception("Failed to load catchlight texture");
        var catchLightTex = new TexFile(catchlight);
        var catchlightOutDir = Path.Combine(outDir, "chara/common/texture/sphere_d_array");
        Directory.CreateDirectory(catchlightOutDir);
        for (int i = 0; i < catchLightTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(catchLightTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(catchlightOutDir, $"sphere_d_array.{i}.png"), texture.ToArray());
        }
        
        SaveAsVerticalArrayTexture(catchLightTex, catchlightOutDir, "sphere_d_array", catchLightTex.Header.CalculatedArraySize);
    }

    public static void SaveTileTextures(SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        var tileNorm = pack.GetFileOrReadFromDisk("chara/common/texture/tile_norm_array.tex");
        if (tileNorm == null) throw new Exception("Failed to load tile norm texture");
        var tileNormTex = new TexFile(tileNorm);
        var tileNormOutDir = Path.Combine(outDir, "chara/common/texture/tile_norm_array");
        Directory.CreateDirectory(tileNormOutDir);
        for (int i = 0; i < tileNormTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(tileNormTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(tileNormOutDir, $"tile_norm_array.{i}.png"), texture.ToArray());
        }
        
        var tileOrb = pack.GetFileOrReadFromDisk("chara/common/texture/tile_orb_array.tex");
        if (tileOrb == null) throw new Exception("Failed to load tile orb texture");
        var tileOrbTex = new TexFile(tileOrb);
        var tileOrbOutDir = Path.Combine(outDir, "chara/common/texture/tile_orb_array");
        Directory.CreateDirectory(tileOrbOutDir);
        for (int i = 0; i < tileOrbTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(tileOrbTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(tileOrbOutDir, $"tile_orb_array.{i}.png"), texture.ToArray());
        }
        
        SaveAsVerticalArrayTexture(tileNormTex, tileNormOutDir, "tile_norm_array", tileNormTex.Header.CalculatedArraySize);
        SaveAsVerticalArrayTexture(tileOrbTex, tileOrbOutDir, "tile_orb_array", tileOrbTex.Header.CalculatedArraySize);
    }
    
    public static void SaveBgSphereTextures(SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        var catchlight = pack.GetFileOrReadFromDisk("bgcommon/texture/sphere_d_array.tex");
        if (catchlight == null) throw new Exception("Failed to load catchlight texture");
        var catchLightTex = new TexFile(catchlight);
        var catchlightOutDir = Path.Combine(outDir, "bgcommon/texture/sphere_d_array");
        Directory.CreateDirectory(catchlightOutDir);

        for (int i = 0; i < catchLightTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(catchLightTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(catchlightOutDir, $"sphere_d_array.{i}.png"), texture.ToArray());
        }
        
        SaveAsVerticalArrayTexture(catchLightTex, catchlightOutDir, "sphere_d_array", catchLightTex.Header.CalculatedArraySize);
    }

    public static void SaveBgDetailTextures(SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        var detailD = pack.GetFileOrReadFromDisk("bgcommon/nature/detail/texture/detail_d_array.tex");
        if (detailD == null) throw new Exception("Failed to load detail diffuse texture");
        var detailDTex = new TexFile(detailD);
        var detailDOutDir = Path.Combine(outDir, "bgcommon/nature/detail/texture/detail_d_array");
        Directory.CreateDirectory(detailDOutDir);

        for (int i = 0; i < detailDTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(detailDTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(detailDOutDir, $"detail_d_array.{i}.png"), texture.ToArray());
        }

        var detailN = pack.GetFileOrReadFromDisk("bgcommon/nature/detail/texture/detail_n_array.tex");
        if (detailN == null) throw new Exception("Failed to load tile orb texture");
        var detailNTex = new TexFile(detailN);
        var detailNOutDir = Path.Combine(outDir, "bgcommon/nature/detail/texture/detail_n_array");
        Directory.CreateDirectory(detailNOutDir);

        for (int i = 0; i < detailNTex.Header.CalculatedArraySize; i++)
        {
            var img = ImageUtils.GetTexData(detailNTex, i, 0, 0);
            var texture = img.ImageAsPng();
            File.WriteAllBytes(Path.Combine(detailNOutDir, $"detail_n_array.{i}.png"), texture.ToArray());
        }
        
        SaveAsVerticalArrayTexture(detailDTex, detailDOutDir, "detail_d_array", detailDTex.Header.CalculatedArraySize);
        SaveAsVerticalArrayTexture(detailNTex, detailNOutDir, "detail_n_array", detailNTex.Header.CalculatedArraySize);
    }
    
    private static void SaveAsVerticalArrayTexture(
        TexFile texFile, string outDir, string fileName, int arraySize)
    {
        // Combines all images in the array into a single vertical array texture
        var width = texFile.Header.Width;
        var height = texFile.Header.Height;
        var totalHeight = height * arraySize;
        var combinedImage = new SkTexture(width, totalHeight);
        for (int i = 0; i < arraySize; i++)
        {
            var buf = ImageUtils.GetRawRgbaData(texFile, i, 0, 0);
            var startY = i * height;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var index = (y * width + x) * 4;
                    var r = buf[index];
                    var g = buf[index + 1];
                    var b = buf[index + 2];
                    var a = buf[index + 3];
                    // Set the pixel in the combined image
                    // Adjust y position based on the array index
                    combinedImage[x, startY + y] = new SKColor(r, g, b, a);
                }
            }
        }
        
        // Save the combined image as a PNG
        var combinedImagePath = Path.Combine(outDir, $"{fileName}.{width}x{height}.vertical.png");
        using var memoryStream = new MemoryStream();
        combinedImage.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
        var textureData = memoryStream.ToArray();
        File.WriteAllBytes(combinedImagePath, textureData);
        Plugin.Logger.LogInformation("Saved vertical array texture to {Path}", combinedImagePath);
    }
}
