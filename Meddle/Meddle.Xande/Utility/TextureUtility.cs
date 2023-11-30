using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using Lumina.Models.Materials;
using SharpGLTF.Materials;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Xande;

namespace Meddle.Xande.Utility;

public static class TextureUtility
{
    public static IEnumerable<(TextureUsage, Bitmap)> ComputeCharacterModelTextures(Material xivMaterial,
        BitmapData normal, BitmapData? initDiffuse, bool copyNormalAlphaToDiffuse = true)
    {
        var diffuse = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var specular = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var emission = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);

        var colorSetInfo = xivMaterial.File!.ColorSetInfo;

        // copy alpha from normal to original diffuse if it exists
        if (initDiffuse != null && copyNormalAlphaToDiffuse) CopyNormalBlueChannelToDiffuseAlphaChannel(normal, initDiffuse);

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
        var scaleX = (float)diffuse.Width / normal.Width;
        var scaleY = (float)diffuse.Height / normal.Height;

        for (var x = 0; x < diffuse.Width; x++)
            for (var y = 0; y < diffuse.Height; y++)
            {
                //var diffusePixel = diffuse.GetPixel( x, y );
                //var normalPixel = normal.GetPixel( ( int )( x / scaleX ), ( int )( y / scaleY ) );
                //diffuse.SetPixel( x, y, Color.FromArgb( normalPixel.B, diffusePixel.R, diffusePixel.G, diffusePixel.B ) );
                var diffusePixel = GetPixel(diffuse, x, y);
                var normalPixel = GetPixel(normal, (int)(x / scaleX), (int)(y / scaleY));

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

    public static async Task ExportTextures(MaterialBuilder glTfMaterial,
        Dictionary<TextureUsage, Bitmap> xivTextureMap,
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

    public static void ParseIrisTextures(Dictionary<TextureUsage, Bitmap> xivTextureMap, Material xivMaterial,
        IPluginLog log)
    {
        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal)) return;
        var specular = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var colorSetInfo = xivMaterial.File!.ColorSetInfo;

        var normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
            ImageLockMode.ReadWrite, normal.PixelFormat);
        try
        {
            for (int x = 0; x < normal.Width; x++)
            {
                for (int y = 0; y < normal.Height; y++)
                {
                    //var normalPixel = normal.GetPixel( x, y );
                    var normalPixel = TextureUtility.GetPixel(normalData, x, y, log);
                    var colorSetIndex1 = normalPixel.A / 17 * 16;
                    var colorSetBlend = normalPixel.A % 17 / 17.0;
                    var colorSetIndexT2 = normalPixel.A / 17;
                    var colorSetIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

                    var specularBlendColour = ColorUtility.BlendColorSet(in colorSetInfo,
                        colorSetIndex1, colorSetIndex2, 255, colorSetBlend,
                        ColorUtility.TextureType.Specular);

                    specular.SetPixel(x, y,
                        Color.FromArgb(255, specularBlendColour.R, specularBlendColour.G,
                            specularBlendColour.B));
                }
            }

            xivTextureMap.Add(TextureUsage.SamplerSpecular, specular);
        }
        finally
        {
            normal.UnlockBits(normalData);
        }
    }

    public static void ParseHairTextures(Dictionary<TextureUsage, Bitmap> xivTextureMap, Material xivMaterial,
        IPluginLog log)
    {
        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal)) return;

        var specular = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var colorSetInfo = xivMaterial.File!.ColorSetInfo;

        var normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
            ImageLockMode.ReadWrite, normal.PixelFormat);
        try
        {
            for (int x = 0; x < normalData.Width; x++)
            {
                for (int y = 0; y < normalData.Height; y++)
                {
                    var normalPixel = GetPixel(normalData, x, y, log);
                    var colorSetIndex1 = normalPixel.A / 17 * 16;
                    var colorSetBlend = normalPixel.A % 17 / 17.0;
                    var colorSetIndexT2 = normalPixel.A / 17;
                    var colorSetIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

                    var specularBlendColour = ColorUtility.BlendColorSet(in colorSetInfo,
                        colorSetIndex1, colorSetIndex2, 255, colorSetBlend,
                        ColorUtility.TextureType.Specular);

                    // Use normal blue channel for opacity
                    specular.SetPixel(x, y, Color.FromArgb(
                        normalPixel.A,
                        specularBlendColour.R,
                        specularBlendColour.G,
                        specularBlendColour.B
                    ));
                }
            }
        }
        finally
        {
            normal.UnlockBits(normalData);
        }

        xivTextureMap.Add(TextureUsage.SamplerSpecular, specular);

        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerMask, out var mask) ||
            !xivTextureMap.TryGetValue(TextureUsage.SamplerSpecular, out var specularMap)) return;

        // TODO: Diffuse is to be generated using character options for colors
        // Currently based on the mask it seems I am blending it in a weird way
        var diffuse = (Bitmap)mask.Clone();
        var specularScaleX = specularMap.Width / (float)diffuse.Width;
        var specularScaleY = specularMap.Height / (float)diffuse.Height;

        var normalScaleX = normal.Width / (float)diffuse.Width;
        var normalScaleY = normal.Height / (float)diffuse.Height;

        var maskData = mask.LockBits(new Rectangle(0, 0, mask.Width, mask.Height),
            ImageLockMode.ReadWrite, mask.PixelFormat);
        var specularMapData = specularMap.LockBits(
            new Rectangle(0, 0, specularMap.Width, specularMap.Height), ImageLockMode.ReadWrite,
            specularMap.PixelFormat);
        var diffuseData = diffuse.LockBits(new Rectangle(0, 0, diffuse.Width, diffuse.Height),
            ImageLockMode.ReadWrite, diffuse.PixelFormat);
        normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
            ImageLockMode.ReadWrite, normal.PixelFormat);
        try
        {
            for (var x = 0; x < maskData.Width; x++)
            {
                for (var y = 0; y < maskData.Height; y++)
                {
                    var maskPixel = GetPixel(maskData, x, y, log);
                    var specularPixel = GetPixel(specularMapData,
                        (int)(x * specularScaleX), (int)(y * specularScaleY), log);
                    var normalPixel = GetPixel(normalData, (int)(x * normalScaleX),
                        (int)(y * normalScaleY), log);

                    SetPixel(maskData, x, y, Color.FromArgb(
                        normalPixel.A,
                        Convert.ToInt32(specularPixel.R * Math.Pow(maskPixel.G / 255.0, 2)),
                        Convert.ToInt32(specularPixel.G * Math.Pow(maskPixel.G / 255.0, 2)),
                        Convert.ToInt32(specularPixel.B * Math.Pow(maskPixel.G / 255.0, 2))
                    ));

                    // Copy alpha channel from normal to diffuse
                    // var diffusePixel = TextureUtility.GetPixel( diffuseData, x, y, _log );

                    // TODO: Blending using mask
                    TextureUtility.SetPixel(diffuseData, x, y, Color.FromArgb(
                        normalPixel.A,
                        255,
                        255,
                        255
                    ));
                }
            }

            diffuse.UnlockBits(diffuseData);
            // Add the specular occlusion texture to xivTextureMap
            xivTextureMap.Add(TextureUsage.SamplerDiffuse, diffuse);
        }
        finally
        {
            mask.UnlockBits(maskData);
            specularMap.UnlockBits(specularMapData);
            normal.UnlockBits(normalData);
        }
    }

    public static void ParseSkinTextures(Dictionary<TextureUsage, Bitmap> xivTextureMap, Material xivMaterial,
        IPluginLog log)
    {
        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal)) return;
        xivTextureMap.TryGetValue(TextureUsage.SamplerDiffuse, out var diffuse);

        if (diffuse == null) throw new Exception("Diffuse texture is null");

        var normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
            ImageLockMode.ReadWrite, normal.PixelFormat);
        var diffuseData = diffuse.LockBits(new Rectangle(0, 0, diffuse.Width, diffuse.Height),
            ImageLockMode.ReadWrite, diffuse.PixelFormat);
        try
        {
            // use blue for opacity
            CopyNormalBlueChannelToDiffuseAlphaChannel(normalData, diffuseData);

            for (var x = 0; x < normal.Width; x++)
            {
                for (var y = 0; y < normal.Height; y++)
                {
                    var normalPixel = GetPixel(normalData, x, y, log);
                    SetPixel(normalData, x, y,
                        Color.FromArgb(normalPixel.B, normalPixel.R, normalPixel.G, 255));
                }
            }
        }
        finally
        {
            normal.UnlockBits(normalData);
            diffuse.UnlockBits(diffuseData);
        }
    }

    public static void ParseCharacterTextures(Dictionary<TextureUsage, Bitmap> xivTextureMap, Material xivMaterial,
        IPluginLog log, bool copyNormalAlphaToDiffuse = true)
    {
        if (xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal))
        {
            xivTextureMap.TryGetValue(TextureUsage.SamplerDiffuse, out var initDiffuse);
            if (!xivTextureMap.ContainsKey(TextureUsage.SamplerDiffuse) ||
                !xivTextureMap.ContainsKey(TextureUsage.SamplerSpecular) ||
                !xivTextureMap.ContainsKey(TextureUsage.SamplerReflection))
            {
                var normalData = normal.LockBits(new Rectangle(0, 0, normal.Width, normal.Height),
                    ImageLockMode.ReadWrite, normal.PixelFormat);
                var initDiffuseData = initDiffuse?.LockBits(
                    new Rectangle(0, 0, initDiffuse.Width, initDiffuse.Height), ImageLockMode.ReadWrite,
                    initDiffuse.PixelFormat);

                try
                {
                    var characterTextures =
                        ComputeCharacterModelTextures(xivMaterial, normalData,
                            initDiffuseData, copyNormalAlphaToDiffuse);

                    // If the textures already exist, tryAdd will make sure they are not overwritten
                    foreach (var (usage, texture) in characterTextures)
                    {
                        xivTextureMap.TryAdd(usage, texture);
                    }
                }
                finally
                {
                    normal.UnlockBits(normalData);
                    if (initDiffuse != null && initDiffuseData != null)
                    {
                        initDiffuse.UnlockBits(initDiffuseData);
                    }
                }
            }
        }

        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerMask, out var mask) ||
            !xivTextureMap.TryGetValue(TextureUsage.SamplerSpecular, out var specularMap)) return;
        var maskData = mask.LockBits(new Rectangle(0, 0, mask.Width, mask.Height),
            ImageLockMode.ReadWrite, mask.PixelFormat);
        var specularMapData = specularMap.LockBits(
            new Rectangle(0, 0, specularMap.Width, specularMap.Height), ImageLockMode.ReadWrite,
            specularMap.PixelFormat);
        var occlusion = ComputeOcclusion(maskData, specularMapData);
        mask.UnlockBits(maskData);
        specularMap.UnlockBits(specularMapData);

        // Add the specular occlusion texture to xivTextureMap
        xivTextureMap.Add(TextureUsage.SamplerWaveMap, occlusion);
    }
}