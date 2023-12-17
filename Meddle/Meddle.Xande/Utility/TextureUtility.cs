using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using Lumina.Models.Materials;
using SharpGLTF.Materials;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using Xande;

namespace Meddle.Xande.Utility;

public static class TextureUtility
{
    public static IEnumerable<(TextureUsage, Bitmap)> ComputeCharacterModelTextures(
        Material xivMaterial,
        Bitmap normal, 
        Bitmap? initDiffuse)
    {
        var diffuse = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var specular = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var emission = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);

        var colorSetInfo = xivMaterial.File!.ColorSetInfo;

        // copy alpha from normal to original diffuse if it exists
        //if (initDiffuse != null && copyNormalAlphaToDiffuse) CopyNormalBlueChannelToDiffuseAlphaChannel(normal, initDiffuse);

        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal.GetPixel(x, y);

            var colorSetIndex1 = normalPixel.A / 17 * 16;
            var colorSetBlend = normalPixel.A % 17 / 17.0;
            var colorSetIndexT2 = normalPixel.A / 17;
            var colorSetIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

            // to fix transparency issues 
            // normal.SetPixel( x, y, Color.FromArgb( normalPixel.B, normalPixel.R, normalPixel.G, 255 ) );
            normal.SetPixel(x, y, Color.FromArgb(normalPixel.B, normalPixel.R, normalPixel.G, 255));

            var diffuseBlendColour = ColorUtility.BlendColorSet(in colorSetInfo, colorSetIndex1, colorSetIndex2,
                normalPixel.B, 
                colorSetBlend, 
                ColorUtility.TextureType.Diffuse);
            var specularBlendColour = ColorUtility.BlendColorSet(in colorSetInfo, colorSetIndex1, colorSetIndex2, 
                255,
                colorSetBlend, 
                ColorUtility.TextureType.Specular);
            var emissionBlendColour = ColorUtility.BlendColorSet(in colorSetInfo, colorSetIndex1, colorSetIndex2, 
                255,
                colorSetBlend, 
                ColorUtility.TextureType.Emissive);

            // Set the blended colors in the respective bitmaps
            diffuse.SetPixel(x, y, diffuseBlendColour);
            specular.SetPixel(x, y, specularBlendColour);
            emission.SetPixel(x, y, emissionBlendColour);
        }
        
        // blend the diffuse with the original diffuse if it exists
        if (initDiffuse != null)
        {
            var scaleX = (float)initDiffuse.Width / diffuse.Width;
            var scaleY = (float)initDiffuse.Height / diffuse.Height;

            for (var x = 0; x < diffuse.Width; x++)
            for (var y = 0; y < diffuse.Height; y++)
            {
                var diffusePixel = diffuse.GetPixel(x, y);
                var initDiffusePixel = initDiffuse.GetPixel((int)(x * scaleX), (int)(y * scaleY));
                
                // blend the diffuse with the original diffuse
                diffuse.SetPixel(x, y, Color.FromArgb(
                    diffusePixel.A,
                    Convert.ToInt32(diffusePixel.R * Math.Pow(initDiffusePixel.G / 255.0, 2)),
                    Convert.ToInt32(diffusePixel.G * Math.Pow(initDiffusePixel.G / 255.0, 2)),
                    Convert.ToInt32(diffusePixel.B * Math.Pow(initDiffusePixel.G / 255.0, 2))
                ));
            }
        }

        return new List<(TextureUsage, Bitmap)>
        {
            (TextureUsage.SamplerDiffuse, diffuse),
            (TextureUsage.SamplerSpecular, specular),
            (TextureUsage.SamplerReflection, emission)
        };
    }

    // Workaround for transparency on diffuse textures
    public static void CopyNormalBlueChannelToDiffuseAlphaChannel(Bitmap normal, Bitmap diffuse)
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
                var diffusePixel = diffuse.GetPixel(x, y);
                var normalPixel = normal.GetPixel((int)(x / scaleX), (int)(y / scaleY));

                diffuse.SetPixel(x, y, Color.FromArgb(normalPixel.B, diffusePixel.R, diffusePixel.G, diffusePixel.B));
            }
    }


    public static Bitmap ComputeOcclusion(Bitmap mask, Bitmap specularMap)
    {
        var occlusion = new Bitmap(mask.Width, mask.Height, PixelFormat.Format32bppArgb);

        for (var x = 0; x < mask.Width; x++)
            for (var y = 0; y < mask.Height; y++)
            {
                var maskPixel = mask.GetPixel(x, y);
                var specularPixel = specularMap.GetPixel(x, y);

                // Calculate the new RGB channels for the specular pixel based on the mask pixel
                specularMap.SetPixel(x, y, Color.FromArgb(
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

    public static void ParseIrisTextures(Dictionary<TextureUsage, Bitmap> xivTextureMap, Material xivMaterial)
    {
        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal)) return;
        var specular = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var colorSetInfo = xivMaterial.File!.ColorSetInfo;

        for (int x = 0; x < normal.Width; x++)
        {
            for (int y = 0; y < normal.Height; y++)
            {
                var normalPixel = normal.GetPixel(x, y);
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

    public static void ParseHairTextures(Dictionary<TextureUsage, Bitmap> xivTextureMap, Material xivMaterial)
    {
        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal)) return;

        var specular = new Bitmap(normal.Width, normal.Height, PixelFormat.Format32bppArgb);
        var colorSetInfo = xivMaterial.File!.ColorSetInfo;

        for (int x = 0; x < normal.Width; x++)
        {
            for (int y = 0; y < normal.Height; y++)
            {
                var normalPixel = normal.GetPixel(x, y);
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

        for (var x = 0; x < mask.Width; x++)
        {
            for (var y = 0; y < mask.Height; y++)
            {
                var maskPixel = mask.GetPixel(x, y);
                var specularPixel = specularMap.GetPixel((int)(x * specularScaleX), (int)(y * specularScaleY));
                var normalPixel = normal.GetPixel((int)(x * normalScaleX),
                    (int)(y * normalScaleY));

                mask.SetPixel(x, y, Color.FromArgb(
                                     normalPixel.A,
                                     Convert.ToInt32(specularPixel.R * Math.Pow(maskPixel.G / 255.0, 2)),
                                     Convert.ToInt32(specularPixel.G * Math.Pow(maskPixel.G / 255.0, 2)),
                                     Convert.ToInt32(specularPixel.B * Math.Pow(maskPixel.G / 255.0, 2))
                                 ));

                // Copy alpha channel from normal to diffuse
                // var diffusePixel = TextureUtility.GetPixel( diffuseData, x, y, _log );

                // TODO: Blending using mask
                diffuse.SetPixel(x, y, Color.FromArgb(
                    normalPixel.A,
                    255,
                    255,
                    255
                ));
            }
        }

        // Add the specular occlusion texture to xivTextureMap
        xivTextureMap.Add(TextureUsage.SamplerDiffuse, diffuse);
    }

    public static void ParseSkinTextures(Dictionary<TextureUsage, Bitmap> xivTextureMap, Material xivMaterial)
    {
        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal)) return;
        xivTextureMap.TryGetValue(TextureUsage.SamplerDiffuse, out var diffuse);

        if (diffuse == null) throw new Exception("Diffuse texture is null");

        // use blue for opacity
        CopyNormalBlueChannelToDiffuseAlphaChannel(normal, diffuse);

        for (var x = 0; x < normal.Width; x++)
        {
            for (var y = 0; y < normal.Height; y++)
            {
                var normalPixel = normal.GetPixel(x, y);
                normal.SetPixel(x, y, Color.FromArgb(normalPixel.B, normalPixel.R, normalPixel.G, 255));
            }
        }
    }

    public static void ParseCharacterTextures(Dictionary<TextureUsage, Bitmap> xivTextureMap, Material xivMaterial,
        IPluginLog log)
    {
        if (xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal))
        {
            xivTextureMap.TryGetValue(TextureUsage.SamplerDiffuse, out var initDiffuse);
            if (!xivTextureMap.ContainsKey(TextureUsage.SamplerDiffuse) ||
                !xivTextureMap.ContainsKey(TextureUsage.SamplerSpecular) ||
                !xivTextureMap.ContainsKey(TextureUsage.SamplerReflection))
            {
                
                try
                {
                    var characterTextures =
                        ComputeCharacterModelTextures(xivMaterial, normal, initDiffuse);

                    // If the textures already exist, tryAdd will make sure they are not overwritten
                    foreach (var (usage, texture) in characterTextures)
                    {
                        if (!xivTextureMap.TryAdd(usage, texture))
                        {
                            log.Warning($"Texture {usage} already exists in the texture map");
                            xivTextureMap[usage] = texture;
                        }
                    }
                }
                catch (Exception e)
                {
                    log.Error(e, "Error computing character textures");
                }
            }
        }

        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerMask, out var mask) ||
            !xivTextureMap.TryGetValue(TextureUsage.SamplerSpecular, out var specularMap)) return;
        var occlusion = ComputeOcclusion(mask, specularMap);

        // Add the specular occlusion texture to xivTextureMap
        xivTextureMap.Add(TextureUsage.SamplerWaveMap, occlusion);
    }
}