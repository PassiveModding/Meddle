using System.Numerics;
using System.Reflection.Metadata;
using FFXIVClientStructs.FFXIV.Shader;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SkiaSharp;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildFallback(Material material, string name)
    {
        var output = BuildSharedBase(material, name)
                              .WithMetallicRoughnessShader()
                              .WithBaseColor(Vector4.One);

        foreach (var texture in material.Textures)
        {
            var usage = texture.Usage;
            var image = texture.ToTexture();
            var channelName = usage switch
            {
                TextureUsage.g_SamplerDiffuse => "diffuse",
                TextureUsage.g_SamplerNormal => "normal",
                TextureUsage.g_SamplerMask => "mask",
                TextureUsage.g_SamplerSpecular => "specular",
                TextureUsage.g_SamplerCatchlight => "catchlight",
                TextureUsage.g_SamplerIndex => "index",
                TextureUsage.g_SamplerFlow => "flow",
                TextureUsage.g_SamplerWaveMap => "waveMap",
                TextureUsage.g_SamplerWaveMap1 => "waveMap1",
                TextureUsage.g_SamplerWhitecapMap => "whitecapMap",
                TextureUsage.g_SamplerWaveletMap0 => "waveletMap0",
                TextureUsage.g_SamplerWaveletMap1 => "waveletMap1",
                TextureUsage.g_SamplerColorMap0 => "colorMap0",
                TextureUsage.g_SamplerNormalMap0 => "normalMap0",
                TextureUsage.g_SamplerSpecularMap0 => "specularMap0",
                TextureUsage.g_SamplerColorMap1 => "colorMap1",
                TextureUsage.g_SamplerNormalMap1 => "normalMap1",
                TextureUsage.g_SamplerSpecularMap1 => "specularMap1",
                TextureUsage.g_SamplerColorMap => "colorMap",
                TextureUsage.g_SamplerNormalMap => "normalMap",
                TextureUsage.g_SamplerSpecularMap => "specularMap",
                TextureUsage.g_SamplerEnvMap => "envMap",
                TextureUsage.g_SamplerSphareMapCustum => "sphareMapCustum",
                TextureUsage.g_Sampler0 => "sampler0",
                TextureUsage.g_Sampler1 => "sampler1",
                TextureUsage.g_Sampler => "sampler",
                TextureUsage.g_SamplerGradationMap => "gradationMap",
                TextureUsage.g_SamplerNormal2 => "normal2",
                TextureUsage.g_SamplerWrinklesMask => "wrinklesMask",
                // _ => throw new ArgumentOutOfRangeException($"Unknown texture usage: {usage}")
                _ => $"unknown_{usage}"
            };

            KnownChannel knownChannel = usage switch
            {
                TextureUsage.g_SamplerDiffuse => KnownChannel.BaseColor,
                TextureUsage.g_SamplerNormal => KnownChannel.Normal,
                TextureUsage.g_SamplerMask => KnownChannel.SpecularFactor,
                TextureUsage.g_SamplerSpecular => KnownChannel.SpecularColor,
                TextureUsage.g_SamplerCatchlight => KnownChannel.Emissive,
                //TextureUsage.g_SamplerIndex => KnownChannel.Unknown,
                //TextureUsage.g_SamplerFlow => KnownChannel.Unknown,
                //TextureUsage.g_SamplerWaveMap => KnownChannel.Unknown,
                //TextureUsage.g_SamplerWaveMap1 => KnownChannel.Unknown,
                //TextureUsage.g_SamplerWhitecapMap => KnownChannel.Unknown,
                //TextureUsage.g_SamplerWaveletMap0 => KnownChannel.Unknown,
                //TextureUsage.g_SamplerWaveletMap1 => KnownChannel.Unknown,
                TextureUsage.g_SamplerColorMap0 => KnownChannel.BaseColor,
                TextureUsage.g_SamplerNormalMap0 => KnownChannel.Normal,
                TextureUsage.g_SamplerSpecularMap0 => KnownChannel.SpecularColor,
                TextureUsage.g_SamplerColorMap1 => KnownChannel.BaseColor,
                TextureUsage.g_SamplerNormalMap1 => KnownChannel.Normal,
                TextureUsage.g_SamplerSpecularMap1 => KnownChannel.SpecularColor,
                TextureUsage.g_SamplerColorMap => KnownChannel.BaseColor,
                TextureUsage.g_SamplerNormalMap => KnownChannel.Normal,
                TextureUsage.g_SamplerSpecularMap => KnownChannel.SpecularColor,
                //TextureUsage.g_SamplerEnvMap => KnownChannel.Unknown,
                //TextureUsage.g_SamplerSphareMapCustum => KnownChannel.Unknown,
                //TextureUsage.g_Sampler0 => KnownChannel.Unknown,
                //TextureUsage.g_Sampler1 => KnownChannel.Unknown,
                //TextureUsage.g_Sampler => KnownChannel.Unknown,
                //TextureUsage.g_SamplerGradationMap => KnownChannel.Unknown,
                TextureUsage.g_SamplerNormal2 => KnownChannel.Normal,
                //TextureUsage.g_SamplerWrinklesMask => KnownChannel.Unknown,
                _ => (KnownChannel)999
            };
            
            if (knownChannel != (KnownChannel)999)
            {
                output.WithChannelImage(knownChannel, BuildImage(image, name, channelName));
            }
        }
        
        return output;
    }
    
    public static KnownChannel? MapTextureUsageToChannel(TextureUsage usage)
    {
        return usage switch
        {
            TextureUsage.g_SamplerDiffuse => KnownChannel.BaseColor,
            TextureUsage.g_SamplerNormal => KnownChannel.Normal,
            TextureUsage.g_SamplerMask => KnownChannel.SpecularFactor,
            TextureUsage.g_SamplerSpecular => KnownChannel.SpecularColor,
            TextureUsage.g_SamplerCatchlight => KnownChannel.Emissive,
            TextureUsage.g_SamplerColorMap0 => KnownChannel.BaseColor,
            TextureUsage.g_SamplerNormalMap0 => KnownChannel.Normal,
            TextureUsage.g_SamplerSpecularMap0 => KnownChannel.SpecularColor,
            TextureUsage.g_SamplerColorMap1 => KnownChannel.BaseColor,
            TextureUsage.g_SamplerNormalMap1 => KnownChannel.Normal,
            TextureUsage.g_SamplerSpecularMap1 => KnownChannel.SpecularColor,
            TextureUsage.g_SamplerColorMap => KnownChannel.BaseColor,
            TextureUsage.g_SamplerNormalMap => KnownChannel.Normal,
            TextureUsage.g_SamplerSpecularMap => KnownChannel.SpecularColor,
            TextureUsage.g_SamplerNormal2 => KnownChannel.Normal,
            _ => null
        };
    }
    
    public static MaterialBuilder BuildSharedBase(Material material, string name)
    {
        const uint backfaceMask  = 0x1;
        var        showBackfaces = (material.ShaderFlags & backfaceMask) == 0;
        
        return new MaterialBuilder(name)
            .WithDoubleSide(showBackfaces);
    }

    public static Vector4 ToVector4(this SKColor color) => new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);
    //public static SKColor ToSkColor(this Vector4 color) => 
    //    new((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), (byte)(color.W * 255));
    public static SKColor ToSkColor(this Vector4 color)
    {
        var c = color.Clamp(0, 1);
        return new SKColor((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), (byte)(c.W * 255));
    }
    public static Vector4 Clamp(this Vector4 v, float min, float max)
    {
        return new Vector4(
            Math.Clamp(v.X, min, max),
            Math.Clamp(v.Y, min, max),
            Math.Clamp(v.Z, min, max),
            Math.Clamp(v.W, min, max)
        );
    }
    
    public static SKColor ToSkColor(this Vector3 color) => 
        new((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), byte.MaxValue);
    
    /*public static ImageBuilder BuildImage(SKTexture texture, string materialName, string suffix)
    {
        var name = $"{Path.GetFileNameWithoutExtension(materialName)}_{suffix}";
        
        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
            textureBytes = memoryStream.ToArray();
        }

        var imageBuilder = ImageBuilder.From(textureBytes, name);
        imageBuilder.AlternateWriteFileName = $"{name}.*";
        return imageBuilder;
    }*/
    public static ImageBuilder BuildImage(SKTexture texture, string materialName, string suffix)
    {
        var name = $"{Path.GetFileNameWithoutExtension(materialName)}_{suffix}";
        
        byte[] textureBytes;
        using (var memoryStream = new MemoryStream())
        {
            texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
            textureBytes = memoryStream.ToArray();
        }
        
        var tempPath = Path.GetTempFileName();
        File.WriteAllBytes(tempPath, textureBytes);

        var imageBuilder = ImageBuilder.From(new MemoryImage(() => File.ReadAllBytes(tempPath)), name);
        imageBuilder.AlternateWriteFileName = $"{name}.*";
        return imageBuilder;
    }
    
    public static SKTexture MultiplyBitmaps(SKTexture target, SKTexture multiplier, bool preserveTargetAlpha = true)
    {
        if (target.Width != multiplier.Width || target.Height != multiplier.Height)
            throw new ArgumentException("Bitmaps must be the same size");
        
        var result = new SKTexture(target.Width, target.Height);
        for (var x = 0; x < target.Width; x++)
        for (var y = 0; y < target.Height; y++)
        {
            var targetPixel = ToVector4(target[x, y]);
            var multPixel = ToVector4(multiplier[x, y]);
            var resultPixel = targetPixel * multPixel;
            resultPixel.W = !preserveTargetAlpha ? targetPixel.W * multPixel.W : targetPixel.W;

            result[x, y] = ToSkColor(resultPixel);
        }

        return result;
    }
}
