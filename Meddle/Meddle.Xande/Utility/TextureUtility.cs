using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using SharpGLTF.Materials;
using SkiaSharp;
using System.Numerics;
using System.Runtime.InteropServices;
using Xande;

namespace Meddle.Xande.Utility;

public static class TextureUtility
{
    //public sealed unsafe class SKTexture : IDisposable
    //{
    //    public SKBitmap Bitmap { get; }
    //    private SKCanvas Canvas { get; }

    //    public int Width => Bitmap.Width;
    //    public int Height => Bitmap.Height;

    //    public SKTexture(SKBitmap bitmap)
    //    {
    //        Bitmap = bitmap;
    //        Canvas = new SKCanvas(bitmap);
    //    }

    //    public SKTexture(int width, int height) : this(new(new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
    //    {

    //    }

    //    public SKColor this[int x, int y]
    //    {
    //        get => Bitmap.GetPixel(x, y);
    //        set => Canvas.DrawPoint(x, y, value);
    //    }

    //    public void Flush()
    //    {
    //        Canvas.Flush();
    //    }

    //    public void Dispose()
    //    {
    //        Canvas.Dispose();
    //        Bitmap.Dispose();
    //    }
    //}

    public sealed class SKTexture
    {
        private byte[] Data { get; }

        public int Width { get; }
        public int Height { get; }

        public SKBitmap Bitmap
        {
            get
            {
                var ret = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                ret.Erase(new(0));
                var p = ret.GetPixels(out var l);
                if (l != Data.Length)
                    throw new InvalidOperationException("Invalid length");
                Marshal.Copy(Data, 0, p, Data.Length);
                if (!ret.Bytes.SequenceEqual(Data))
                    throw new InvalidOperationException("Invalid copied data");
                return ret;
            }
        }

        public SKTexture(int width, int height)
        {
            Data = new byte[width * height * 4];
            Width = width;
            Height = height;
        }

        public SKTexture(SKBitmap bitmap) : this(bitmap.Width, bitmap.Height)
        {
            if (bitmap.ColorType != SKColorType.Rgba8888 || bitmap.AlphaType != SKAlphaType.Unpremul)
            {
                using var newBitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                using (var canvas = new SKCanvas(newBitmap))
                    canvas.DrawBitmap(bitmap, 0, 0);

                if (newBitmap.ByteCount != Data.Length)
                    throw new ArgumentException("Invalid byte count");
                newBitmap.Bytes.CopyTo(Data, 0);
                newBitmap.SaveToFile(@$"C:\Users\Asriel\AppData\Local\Temp\Meddle.Export\tex2\{bitmap.GetHashCode():X8}.png");
                bitmap.SaveToFile(@$"C:\Users\Asriel\AppData\Local\Temp\Meddle.Export\tex2\{bitmap.GetHashCode():X8}_ORIG.png");

                if (!newBitmap.Bytes.SequenceEqual(Data))
                    throw new InvalidOperationException("Invalid cloned data");
            }
            else
            {
                if (bitmap.ByteCount != Data.Length)
                    throw new ArgumentException("Invalid byte count");
                bitmap.Bytes.CopyTo(Data, 0);
                bitmap.SaveToFile(@$"C:\Users\Asriel\AppData\Local\Temp\Meddle.Export\tex2\{bitmap.GetHashCode():X8}_ORIG3.png");
            }
        }

        public SKTexture Copy()
        {
            var ret = new SKTexture(Width, Height);
            Data.CopyTo(ret.Data, 0);
            return ret;
        }

        private Span<byte> GetPixelData(int x, int y) =>
            Data.AsSpan().Slice((Width * y + x) * 4, 4);

        public SKColor this[int x, int y]
        {
            get
            {
                var s = GetPixelData(x, y);
                return new(s[0], s[1], s[2], s[3]);
            }
            set
            {
                var s = GetPixelData(x, y);
                s[0] = value.Red;
                s[1] = value.Green;
                s[2] = value.Blue;
                s[3] = value.Alpha;
            }
        }

        public void Flush()
        {

        }
    }

    public static IEnumerable<(TextureUsage, SKTexture)> ComputeCharacterModelTextures(NewMaterial xivMaterial,
        SKTexture normal, SKTexture? initDiffuse, bool copyNormalAlphaToDiffuse = true)
    {
        var diffuse = new SKTexture(normal.Width, normal.Height);
        var specular = new SKTexture(normal.Width, normal.Height);
        var emission = new SKTexture(normal.Width, normal.Height);

        var colorSetInfo = xivMaterial.ColorTable ?? throw new ArgumentException($"Expected color table for {xivMaterial.HandlePath}");

        // copy alpha from normal to original diffuse if it exists
        if (initDiffuse != null && copyNormalAlphaToDiffuse) CopyNormalBlueChannelToDiffuseAlphaChannel(normal, initDiffuse);

        for (var x = 0; x < normal.Width; x++)
            for (var y = 0; y < normal.Height; y++)
            {
                var normalPixel = normal[x, y];

                var colorSetIndex1 = normalPixel.Alpha / 17 * 16;
                var colorSetBlend = normalPixel.Alpha % 17 / 17.0;
                var colorSetIndexT2 = normalPixel.Alpha / 17;
                var colorSetIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

                // to fix transparency issues 
                // normal.SetPixel( x, y, Color.FromArgb( normalPixel.B, normalPixel.R, normalPixel.G, 255 ) );
                normal[x, y] = new(normalPixel.Red, normalPixel.Green, 255, normalPixel.Blue);

                var diffuseBlendColour = ColorUtility.BlendColorSet(colorSetInfo, colorSetIndex1, colorSetIndex2,
                    normalPixel.Blue, colorSetBlend, ColorUtility.TextureType.Diffuse);
                var specularBlendColour = ColorUtility.BlendColorSet(colorSetInfo, colorSetIndex1, colorSetIndex2, 255,
                    colorSetBlend, ColorUtility.TextureType.Specular);
                var emissionBlendColour = ColorUtility.BlendColorSet(colorSetInfo, colorSetIndex1, colorSetIndex2, 255,
                    colorSetBlend, ColorUtility.TextureType.Emissive);

                // Set the blended colors in the respective bitmaps
                diffuse[x, y] = diffuseBlendColour;
                specular[x, y] = specularBlendColour;
                emission[x, y] = emissionBlendColour;
            }

        normal.Flush();
        diffuse.Flush();
        specular.Flush();
        emission.Flush();

        return new List<(TextureUsage, SKTexture)>
        {
            (TextureUsage.SamplerDiffuse, diffuse),
            (TextureUsage.SamplerSpecular, specular),
            (TextureUsage.SamplerReflection, emission)
        };
    }

    // Workaround for transparency on diffuse textures
    public static void CopyNormalBlueChannelToDiffuseAlphaChannel(SKTexture normal, SKTexture diffuse)
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
                var diffusePixel = diffuse[x, y];
                var normalPixel = normal[(int)(x / scaleX), (int)(y / scaleY)];

                diffuse[x, y] = diffusePixel.WithAlpha(normalPixel.Blue);
            }

        diffuse.Flush();
    }

    public static SKTexture ComputeOcclusion(SKTexture mask, SKTexture specularMap)
    {
        var occlusion = new SKTexture(mask.Width, mask.Height);

        for (var x = 0; x < mask.Width; x++)
            for (var y = 0; y < mask.Height; y++)
            {
                var maskPixel = mask[x, y];
                var specularPixel = specularMap[x, y];

                // Calculate the new RGB channels for the specular pixel based on the mask pixel
                specularMap[x, y] = new(
                    (byte)Convert.ToInt32(specularPixel.Red * Math.Pow(maskPixel.Green / 255.0, 2)),
                    (byte)Convert.ToInt32(specularPixel.Green * Math.Pow(maskPixel.Green / 255.0, 2)),
                    (byte)Convert.ToInt32(specularPixel.Blue * Math.Pow(maskPixel.Green / 255.0, 2)),
                    specularPixel.Alpha
                );

                // Oops all red
                occlusion[x, y] = new(
                    maskPixel.Red,
                    maskPixel.Red,
                    maskPixel.Red,
                    255
                );
            }

        specularMap.Flush();
        occlusion.Flush();

        return occlusion;
    }

    public static void ExportTextures(MaterialBuilder glTfMaterial,
        Dictionary<TextureUsage, SKTexture> xivTextureMap,
        string outputDir)
    {
        foreach (var xivTexture in xivTextureMap)
            ExportTexture(glTfMaterial, xivTexture.Key, xivTexture.Value.Bitmap, outputDir);

        // Set the metallic roughness factor to 0
        glTfMaterial.WithMetallicRoughness(0);
    }

    private static object TextureSemaphore { get; } = new();

    public static void SaveToFile(this SKBitmap bitmap, string path)
    {
        using var f = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();

        stream.CopyTo(f);
    }

    public static void ExportTexture(MaterialBuilder glTfMaterial, TextureUsage textureUsage, SKBitmap bitmap,
        string outputDir)
    {
        lock (TextureSemaphore)
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
                    bitmap.SaveToFile(path);
                    glTfMaterial.WithBaseColor(path);
                    break;
                case TextureUsage.SamplerNormalMap0:
                case TextureUsage.SamplerNormal:
                    path = Path.Combine(outputDir, $"{name}_normal.png");
                    bitmap.SaveToFile(path);
                    glTfMaterial.WithNormal(path);
                    break;
                case TextureUsage.SamplerSpecularMap0:
                case TextureUsage.SamplerSpecular:
                    path = Path.Combine(outputDir, $"{name}_specular.png");
                    bitmap.SaveToFile(path);
                    glTfMaterial.WithSpecularColor(path);
                    break;
                case TextureUsage.SamplerWaveMap:
                    path = Path.Combine(outputDir, $"{name}_occlusion.png");
                    bitmap.SaveToFile(path);
                    glTfMaterial.WithOcclusion(path);
                    break;
                case TextureUsage.SamplerReflection:
                    path = Path.Combine(outputDir, $"{name}_emissive.png");
                    bitmap.SaveToFile(path);
                    glTfMaterial.WithEmissive(path, new Vector3(255, 255, 255));
                    break;
                case TextureUsage.SamplerMask:
                    path = Path.Combine(outputDir, $"{name}_mask.png");
                    // Do something with this texture
                    bitmap.SaveToFile(path);
                    break;
                default:
                    path = Path.Combine(outputDir, $"{name}_{textureUsage}.png");
                    bitmap.SaveToFile(path);
                    break;
            }
        }
    }

    public static void ParseIrisTextures(Dictionary<TextureUsage, SKTexture> xivTextureMap, NewMaterial xivMaterial,
        IPluginLog log)
    {
        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal)) return;
        var specular = new SKTexture(normal.Width, normal.Height);
        var colorSetInfo = xivMaterial.ColorTable ?? throw new ArgumentException($"Expected color table for {xivMaterial.HandlePath}");

        for (int x = 0; x < normal.Width; x++)
        {
            for (int y = 0; y < normal.Height; y++)
            {
                //var normalPixel = normal.GetPixel( x, y );
                var normalPixel = normal[x, y];
                var colorSetIndex1 = normalPixel.Alpha / 17 * 16;
                var colorSetBlend = normalPixel.Alpha % 17 / 17.0;
                var colorSetIndexT2 = normalPixel.Alpha / 17;
                var colorSetIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

                var specularBlendColour = ColorUtility.BlendColorSet(colorSetInfo,
                    colorSetIndex1, colorSetIndex2, 255, colorSetBlend,
                    ColorUtility.TextureType.Specular);

                specular[x, y] = specularBlendColour.WithAlpha(255);
            }
        }

        specular.Flush();

        xivTextureMap.Add(TextureUsage.SamplerSpecular, specular);
    }

    public static void ParseHairTextures(Dictionary<TextureUsage, SKTexture> xivTextureMap, NewMaterial xivMaterial,
        IPluginLog log)
    {
        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal)) return;

        var specular = new SKTexture(normal.Width, normal.Height);
        var colorSetInfo = xivMaterial.ColorTable ?? throw new ArgumentException($"Expected color table for {xivMaterial.HandlePath}");

        for (int x = 0; x < normal.Width; x++)
        {
            for (int y = 0; y < normal.Height; y++)
            {
                var normalPixel = normal[x, y];
                var colorSetIndex1 = normalPixel.Alpha / 17 * 16;
                var colorSetBlend = normalPixel.Alpha % 17 / 17.0;
                var colorSetIndexT2 = normalPixel.Alpha / 17;
                var colorSetIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

                var specularBlendColour = ColorUtility.BlendColorSet(colorSetInfo,
                    colorSetIndex1, colorSetIndex2, 255, colorSetBlend,
                    ColorUtility.TextureType.Specular);

                // Use normal blue channel for opacity
                specular[x, y] = specularBlendColour.WithAlpha(normalPixel.Alpha);
            }
        }

        specular.Flush();

        xivTextureMap.Add(TextureUsage.SamplerSpecular, specular);

        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerMask, out var mask) ||
            !xivTextureMap.TryGetValue(TextureUsage.SamplerSpecular, out var specularMap)) return;

        // TODO: Diffuse is to be generated using character options for colors
        // Currently based on the mask it seems I am blending it in a weird way
        var diffuse = mask.Copy();
        var specularScaleX = specularMap.Width / (float)diffuse.Width;
        var specularScaleY = specularMap.Height / (float)diffuse.Height;

        var normalScaleX = normal.Width / (float)diffuse.Width;
        var normalScaleY = normal.Height / (float)diffuse.Height;

        for (var x = 0; x < mask.Width; x++)
        {
            for (var y = 0; y < mask.Height; y++)
            {
                var maskPixel = mask[x, y];
                var specularPixel = specularMap[(int)(x * specularScaleX), (int)(y * specularScaleY)];
                var normalPixel = normal[(int)(x * normalScaleX), (int)(y * normalScaleY)];

                mask[x, y] = new(
                    (byte)Convert.ToInt32(specularPixel.Red * Math.Pow(maskPixel.Green / 255.0, 2)),
                    (byte)Convert.ToInt32(specularPixel.Green * Math.Pow(maskPixel.Green / 255.0, 2)),
                    (byte)Convert.ToInt32(specularPixel.Blue * Math.Pow(maskPixel.Green / 255.0, 2)),
                    normalPixel.Alpha
                );

                // Copy alpha channel from normal to diffuse
                // var diffusePixel = TextureUtility.GetPixel( diffuseData, x, y, _log );

                // TODO: Blending using mask
                diffuse[x, y] = SKColors.White.WithAlpha(normalPixel.Alpha);
            }
        }

        mask.Flush();
        diffuse.Flush();

        // Add the specular occlusion texture to xivTextureMap
        xivTextureMap.Add(TextureUsage.SamplerDiffuse, diffuse);
    }

    public static void ParseSkinTextures(Dictionary<TextureUsage, SKTexture> xivTextureMap, NewMaterial xivMaterial,
        IPluginLog log)
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
                var normalPixel = normal[x, y];
                normal[x, y] = new(normalPixel.Red, normalPixel.Green, 255, normalPixel.Blue);
            }
        }

        normal.Flush();
    }

    public static void ParseCharacterTextures(Dictionary<TextureUsage, SKTexture> xivTextureMap, NewMaterial xivMaterial,
        IPluginLog log, bool copyNormalAlphaToDiffuse = true)
    {
        if (xivTextureMap.TryGetValue(TextureUsage.SamplerNormal, out var normal))
        {
            xivTextureMap.TryGetValue(TextureUsage.SamplerDiffuse, out var initDiffuse);
            if (!xivTextureMap.ContainsKey(TextureUsage.SamplerDiffuse) ||
                !xivTextureMap.ContainsKey(TextureUsage.SamplerSpecular) ||
                !xivTextureMap.ContainsKey(TextureUsage.SamplerReflection))
            {
                var characterTextures = ComputeCharacterModelTextures(xivMaterial, normal, initDiffuse, copyNormalAlphaToDiffuse);

                // If the textures already exist, tryAdd will make sure they are not overwritten
                foreach (var (usage, texture) in characterTextures)
                    xivTextureMap.TryAdd(usage, texture);
            }
        }

        if (!xivTextureMap.TryGetValue(TextureUsage.SamplerMask, out var mask) ||
            !xivTextureMap.TryGetValue(TextureUsage.SamplerSpecular, out var specularMap)) return;
        
        var occlusion = ComputeOcclusion(mask, specularMap);

        // Add the specular occlusion texture to xivTextureMap
        xivTextureMap.Add(TextureUsage.SamplerWaveMap, occlusion);
    }
}
