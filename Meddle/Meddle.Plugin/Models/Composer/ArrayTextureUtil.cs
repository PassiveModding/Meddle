using Meddle.Formats.Files;
using Meddle.Formats.Helpers;
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

    public static void SaveSphereTextures(SqPack.SqPack pack, string cacheDir)
    {
        SaveArrayTexture(pack, GetOutDir(cacheDir), "chara/common/texture/sphere_d_array");
    }

    public static void SaveTileTextures(SqPack.SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        SaveArrayTexture(pack, outDir, "chara/common/texture/tile_norm_array");
        SaveArrayTexture(pack, outDir, "chara/common/texture/tile_orb_array");
    }

    public static void SaveBgSphereTextures(SqPack.SqPack pack, string cacheDir)
    {
        SaveArrayTexture(pack, GetOutDir(cacheDir), "bgcommon/texture/sphere_d_array");
    }

    public static void SaveBgDetailTextures(SqPack.SqPack pack, string cacheDir)
    {
        var outDir = GetOutDir(cacheDir);
        SaveArrayTexture(pack, outDir, "bgcommon/nature/detail/texture/detail_d_array");
        SaveArrayTexture(pack, outDir, "bgcommon/nature/detail/texture/detail_n_array");
    }

    // writes each slice as a png and all slices stacked into one vertical png
    private static void SaveArrayTexture(SqPack.SqPack pack, string outDir, string gamePath)
    {
        var data = pack.GetFileOrReadFromDisk($"{gamePath}.tex");
        if (data == null) throw new Exception($"Failed to load texture: {gamePath}");
        var texFile = new TexFile(data);

        var fileName = Path.GetFileName(gamePath);
        var sliceDir = Path.Combine(outDir, gamePath);
        Directory.CreateDirectory(sliceDir);

        var width = texFile.Header.Width;
        var height = texFile.Header.Height;
        var arraySize = texFile.Header.CalculatedArraySize;
        var sliceBytes = width * height * 4;
        var totalBytes = (long)sliceBytes * arraySize;
        if (totalBytes > int.MaxValue)
        {
            throw new InvalidDataException($"Array texture too large: {gamePath} {width}x{height}x{arraySize}");
        }

        using var combined = new SKBitmap(width, height * arraySize, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = combined.GetPixelSpan();
        if (pixels.Length != totalBytes)
        {
            throw new InvalidDataException($"Unexpected bitmap size for {gamePath}: {pixels.Length} != {totalBytes}");
        }

        for (var i = 0; i < arraySize; i++)
        {
            var rgba = ImageUtils.GetRgbaImage(texFile, i, 0, 0).Data;
            if (rgba.Length != sliceBytes)
            {
                throw new InvalidDataException(
                    $"Unexpected slice {i} of {gamePath}: {rgba.Length} bytes, expected {sliceBytes}");
            }

            using (var slice = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul))
            {
                rgba.CopyTo(slice.GetPixelSpan());
                using var sliceStream = File.Create(Path.Combine(sliceDir, $"{fileName}.{i}.png"));
                slice.Encode(sliceStream, SKEncodedImageFormat.Png, 100);
            }

            rgba.CopyTo(pixels.Slice(i * sliceBytes, sliceBytes));
        }

        var combinedImagePath = Path.Combine(sliceDir, $"{fileName}.{width}x{height}.vertical.png");
        using (var stream = File.Create(combinedImagePath))
        {
            combined.Encode(stream, SKEncodedImageFormat.Png, 100);
        }

        Plugin.Logger.LogInformation("Saved vertical array texture to {Path}", combinedImagePath);
    }
}
