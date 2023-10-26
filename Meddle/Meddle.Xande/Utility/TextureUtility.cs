using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using Meddle.Lumina.Materials;
using SharpGLTF.Materials;
using Xande;

namespace Meddle.Xande.Utility;

public static class TextureUtility
{
    public static IEnumerable<(TextureUsage, Bitmap)> ComputeCharacterModelTextures(Material xivMaterial,
        BitmapData normal, BitmapData? initDiffuse)
    {
        var diffuse = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var specular = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var emission = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);

        var colorSetInfo = xivMaterial.File!.ColorSetInfo;

        // copy alpha from normal to original diffuse if it exists
        if (initDiffuse != null) CopyNormalBlueChannelToDiffuseAlphaChannel(normal, initDiffuse);

        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = GetPixel(normal, x, y);

            var colorSetIndex1 = normalPixel.A / 17 * 16;
            var colorSetBlend = normalPixel.A % 17 / 17.0;
            var colorSetIndexT2 = normalPixel.A / 17;
            var colorSetIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

            // to fix transparency issues 
            // normal.SetPixel( x, y, Color.FromArgb( normalPixel.B, normalPixel.R, normalPixel.G, 255 ) );
            SetPixel(normal, x, y, Color.FromArgb(normalPixel.B, normalPixel.R, normalPixel.G, 255));

            var diffuseBlendColour = ColorUtility.BlendColorSet(in colorSetInfo, colorSetIndex1, colorSetIndex2,
                normalPixel.B, colorSetBlend, ColorUtility.TextureType.Diffuse);
            var specularBlendColour = ColorUtility.BlendColorSet(in colorSetInfo, colorSetIndex1, colorSetIndex2, 255,
                colorSetBlend, ColorUtility.TextureType.Specular);
            var emissionBlendColour = ColorUtility.BlendColorSet(in colorSetInfo, colorSetIndex1, colorSetIndex2, 255,
                colorSetBlend, ColorUtility.TextureType.Emissive);

            // Set the blended colors in the respective bitmaps
            diffuse.SetPixel(x, y, diffuseBlendColour);
            specular.SetPixel(x, y, specularBlendColour);
            emission.SetPixel(x, y, emissionBlendColour);
        }

        return new List<(TextureUsage, Bitmap)>
        {
            (TextureUsage.SamplerDiffuse, diffuse),
            (TextureUsage.SamplerSpecular, specular),
            (TextureUsage.SamplerReflection, emission)
        };
    }

    // Workaround for transparency on diffuse textures
    public static void CopyNormalBlueChannelToDiffuseAlphaChannel(BitmapData normal, BitmapData diffuse)
    {
        // need to scale normal map lookups to diffuse size since the maps are often smaller
        // will look blocky but its better than nothing
        var scaleX = (float) diffuse.Width / normal.Width;
        var scaleY = (float) diffuse.Height / normal.Height;

        for (var x = 0; x < diffuse.Width; x++)
        for (var y = 0; y < diffuse.Height; y++)
        {
            //var diffusePixel = diffuse.GetPixel( x, y );
            //var normalPixel = normal.GetPixel( ( int )( x / scaleX ), ( int )( y / scaleY ) );
            //diffuse.SetPixel( x, y, Color.FromArgb( normalPixel.B, diffusePixel.R, diffusePixel.G, diffusePixel.B ) );
            var diffusePixel = GetPixel(diffuse, x, y);
            var normalPixel = GetPixel(normal, (int) (x / scaleX), (int) (y / scaleY));

            SetPixel(diffuse, x, y, Color.FromArgb(normalPixel.B, diffusePixel.R, diffusePixel.G, diffusePixel.B));
        }
    }

    public static void SetPixel(BitmapData data, int x, int y, Color color)
    {
        try
        {
            if (x < 0 || x >= data.Width || y < 0 || y >= data.Height)
                throw new ArgumentOutOfRangeException(nameof(x), nameof(y), "x or y is out of bounds");

            var bytesPerPixel = Image.GetPixelFormatSize(data.PixelFormat) / 8;
            var offset = y * data.Stride + x * bytesPerPixel;

            if (offset < 0 || offset + bytesPerPixel > data.Stride * data.Height)
                throw new ArgumentOutOfRangeException("Memory access error");

            var pixel = new byte[bytesPerPixel];
            Marshal.Copy(data.Scan0 + offset, pixel, 0, bytesPerPixel);

            pixel[0] = color.B; // Blue
            pixel[1] = color.G; // Green
            pixel[2] = color.R; // Red

            if (bytesPerPixel == 4) pixel[3] = color.A; // Alpha

            Marshal.Copy(pixel, 0, data.Scan0 + offset, bytesPerPixel);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            PluginLog.Error(ex, ex.Message);
        }
    }

    public static Color GetPixel(BitmapData data, int x, int y, IPluginLog? logger = null)
    {
        try
        {
            if (x < 0 || x >= data.Width || y < 0 || y >= data.Height)
                throw new ArgumentOutOfRangeException(nameof(x), nameof(y), "x or y is out of bounds");

            var bytesPerPixel = Image.GetPixelFormatSize(data.PixelFormat) / 8;
            var offset = y * data.Stride + x * bytesPerPixel;

            if (offset < 0 || offset + bytesPerPixel > data.Stride * data.Height)
                throw new InvalidOperationException("Memory access error");

            var pixel = new byte[bytesPerPixel];
            Marshal.Copy(data.Scan0 + offset, pixel, 0, bytesPerPixel);

            if (bytesPerPixel == 4)
                return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
            if (bytesPerPixel == 3)
                return Color.FromArgb(255, pixel[2], pixel[1], pixel[0]);
            throw new InvalidOperationException("Unsupported pixel format");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            logger?.Error(ex, ex.Message);
            return Color.Transparent;
        }
        catch (InvalidOperationException ex)
        {
            logger?.Error(ex, ex.Message);
            return Color.Transparent;
        }
    }


    public static Bitmap ComputeOcclusion(BitmapData mask, BitmapData specularMap)
    {
        var occlusion = new Bitmap(mask.Width, mask.Height, PixelFormat.Format32bppArgb);

        for (var x = 0; x < mask.Width; x++)
        for (var y = 0; y < mask.Height; y++)
        {
            var maskPixel = GetPixel(mask, x, y);
            var specularPixel = GetPixel(specularMap, x, y);

            // Calculate the new RGB channels for the specular pixel based on the mask pixel
            SetPixel(specularMap, x, y, Color.FromArgb(
                specularPixel.A,
                Convert.ToInt32(specularPixel.R * Math.Pow(maskPixel.G / 255.0, 2)),
                Convert.ToInt32(specularPixel.G * Math.Pow(maskPixel.G / 255.0, 2)),
                Convert.ToInt32(specularPixel.B * Math.Pow(maskPixel.G / 255.0, 2))
            ));

            // Oops all red
            occlusion.SetPixel(x, y, Color.FromArgb(
                255,
                maskPixel.R,
                maskPixel.R,
                maskPixel.R
            ));
        }

        return occlusion;
    }
    
    /// <summary>
    /// Safely get a texture buffer copy from Lumina.
    /// </summary>
    /// <param name="manager"></param>
    /// <param name="path"></param>
    /// <param name="origPath"></param>
    /// <returns></returns>
    public static Bitmap GetTextureBufferCopy(LuminaManager manager, string path, string? origPath = null)
    {
        var textureBuffer = manager.GetTextureBuffer(path, origPath);
        var copy = new Bitmap(textureBuffer);
        textureBuffer.Dispose();
        return copy;
    }
    
    public static async Task ExportTextures(MaterialBuilder glTfMaterial, Dictionary<TextureUsage, Bitmap> xivTextureMap,
        string outputDir)
    {
        foreach (var xivTexture in xivTextureMap)
        {
            await ExportTexture(glTfMaterial, xivTexture.Key, xivTexture.Value, outputDir);
        }

        // Set the metallic roughness factor to 0
        glTfMaterial.WithMetallicRoughness(0);
    }

    private static SemaphoreSlim TextureSemaphore { get; } = new(1, 1);
    public static async Task ExportTexture(MaterialBuilder glTfMaterial, TextureUsage textureUsage, Bitmap bitmap,
        string outputDir)
    {
        await TextureSemaphore.WaitAsync();
        try
        {
            // tbh can overwrite or delete these after use but theyre helpful for debugging
            var name = glTfMaterial.Name.Replace("\\", "/").Split("/").Last().Split(".").First();
            string path;

            // Save the texture to the output directory and update the glTF material with respective image paths
            switch (textureUsage)
            {
                case TextureUsage.SamplerColorMap0:
                case TextureUsage.SamplerDiffuse:
                    path = Path.Combine(outputDir, $"{name}_diffuse.png");
                    bitmap.Save(path);
                    glTfMaterial.WithBaseColor(path);
                    break;
                case TextureUsage.SamplerNormalMap0:
                case TextureUsage.SamplerNormal:
                    path = Path.Combine(outputDir, $"{name}_normal.png");
                    bitmap.Save(path);
                    glTfMaterial.WithNormal(path);
                    break;
                case TextureUsage.SamplerSpecularMap0:
                case TextureUsage.SamplerSpecular:
                    path = Path.Combine(outputDir, $"{name}_specular.png");
                    bitmap.Save(path);
                    glTfMaterial.WithSpecularColor(path);
                    break;
                case TextureUsage.SamplerWaveMap:
                    path = Path.Combine(outputDir, $"{name}_occlusion.png");
                    bitmap.Save(path);
                    glTfMaterial.WithOcclusion(path);
                    break;
                case TextureUsage.SamplerReflection:
                    path = Path.Combine(outputDir, $"{name}_emissive.png");
                    bitmap.Save(path);
                    glTfMaterial.WithEmissive(path, new Vector3(255, 255, 255));
                    break;
                case TextureUsage.SamplerMask:
                    path = Path.Combine(outputDir, $"{name}_mask.png");
                    // Do something with this texture
                    bitmap.Save(path);
                    break;
                default:
                    path = Path.Combine(outputDir, $"{name}_{textureUsage}.png");
                    bitmap.Save(path);
                    break;
            }
        }
        finally
        {
            TextureSemaphore.Release();
        }
    }
}