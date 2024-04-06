using System.Numerics;
using Lumina.Data.Parsing;
using Meddle.Plugin.Models;
using SharpGLTF.Materials;
using SkiaSharp;

namespace Meddle.Plugin.Utility.Materials;

public static class MaterialUtility
{
    private static readonly Vec4Ext DefaultEyeColor       = new Vec4Ext(21, 176, 172, 255) / 255f;
    private static readonly Vec4Ext DefaultHairColor      = new Vec4Ext(130, 64,  13, 255) / 255f;
    private static readonly Vec4Ext DefaultHighlightColor = new Vec4Ext(77,  126, 240, 255) / 255f;
    
    public static MaterialBuilder BuildCharacter(Material material, string name)
    {
        var normal = material.GetTexture(TextureUsage.SamplerNormal);
        
        var operation = new ProcessCharacterNormalOperation(normal.Resource.ToTexture(), material.ColorTable!.Value).Run();
        
        var baseColor = operation.BaseColor;
        if (material.TryGetTexture(TextureUsage.SamplerDiffuse, out var diffuseTexture))
        {
            var diffuse = diffuseTexture.Resource.ToTexture((baseColor.Width, baseColor.Height));
            baseColor = MultiplyBitmaps(baseColor, diffuse);
        }
        
        var specular = operation.Specular;
        if (material.TryGetTexture(TextureUsage.SamplerSpecular, out var specularTexture))
        {
            var spec = specularTexture.Resource.ToTexture((specular.Width, specular.Height));
            specular = MultiplyBitmaps(spec, specular);
        }
        
        if (material.TryGetTexture(TextureUsage.SamplerMask, out var mask))
        {
            var maskImage = mask.Resource.ToTexture((baseColor.Width, baseColor.Height));

            for (var x = 0; x < baseColor.Width; x++)
            for (var y = 0; y < baseColor.Height; y++)
            {
                var maskPixel = ToVector4(maskImage[x,y]);
                var baseColorPixel = ToVector4(baseColor[x, y]);
                var alpha = baseColorPixel.W;
                baseColor[x, y] = (baseColorPixel * maskPixel.X).WithW(alpha);
            }
        }
        
        var specularImage = BuildImage(specular, name, "specular");
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor,         name, "basecolor"))
               .WithNormal(BuildImage(operation.Normal,     name, "normal"))
               .WithEmissive(BuildImage(operation.Emissive, name, "emissive"), Vector3.One, 1)
               .WithSpecularFactor(specularImage, 1)
               .WithSpecularColor(specularImage);
    }
    
    /// <summary> Build a material following the semantics of hair.shpk. </summary>
    public static MaterialBuilder BuildHair(Material material, string name, HairShaderParameters? customizeParameters)
    {
        // Trust me bro.
        const uint categoryHairType = 0x24826489;
        const uint valueFace        = 0x6E5B8F10;
        
        var hairCol      = customizeParameters?.MainColor ?? DefaultHairColor.XYZ;
        
        var isFace = material.ShaderKeys
            .Any(key => key is { Category: categoryHairType, Value: valueFace });
        
        var normalTexture = material.GetTexture(TextureUsage.SamplerNormal);
        var maskTexture   = material.GetTexture(TextureUsage.SamplerMask);
        
        var normal = normalTexture.Resource.ToTexture();
        var mask   = maskTexture.Resource.ToTexture((normal.Width, normal.Height));
        
        var baseColor = new SKTexture(normal.Width, normal.Height);
        var specular = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = ToVector4(normal[x, y]);
            var maskPixel = ToVector4(mask[x, y]);
            if (isFace && customizeParameters?.OptionColor != null)
            {
                // Mask Alpha = Option color intensity
                var color = Vector3.Lerp(hairCol, customizeParameters.OptionColor,  maskPixel.W);
                baseColor[x, y] = ToSkColor(new Vector4(color, normalPixel.W));
            }

            if (!isFace)
            {
                var color = Vector3.Lerp(hairCol, customizeParameters?.MeshColor ?? DefaultHighlightColor.XYZ, 
                                         maskPixel.W);
                baseColor[x, y] = ToSkColor(new Vector4(color, normalPixel.W));
            }
            
            // mask green channel is specular
            specular[x, y] = ToSkColor(new Vector4(maskPixel.Y));
        }
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
               .WithNormal(BuildImage(normal,       name, "normal"))
               .WithSpecularFactor( BuildImage(specular, name, "specular"), 1)
               .WithAlpha(isFace ? AlphaMode.BLEND : AlphaMode.MASK, 0.5f);
        
    }
    
    public static MaterialBuilder BuildIris(Material material, string name, Vec4Ext? leftEyeColor)
    {
        var normalTexture = material.GetTexture(TextureUsage.SamplerNormal);
        var maskTexture   = material.GetTexture(TextureUsage.SamplerMask);
        
        var normal = normalTexture.Resource.ToTexture();
        var mask   = maskTexture.Resource.ToTexture((normal.Width, normal.Height));
        
        var baseColor = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = ToVector4(normal[x, y]);
            var maskPixel = ToVector4(mask[x, y]);

            // Not sure if we can set it per eye since it's done by the shader
            // NOTE: W = Face paint (UV2) U multiplier. since we set it using the alpha it gets ignored for iris either way
            var color = (leftEyeColor ?? DefaultEyeColor) * maskPixel.X;
            color.W = normalPixel.W;

            baseColor[x, y] = color;
            normal[x, y] = normalPixel.WithW(1);
        }

        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
               .WithNormal(BuildImage(normal, name, "normal"));
    }

    /// <summary> Build a material following the semantics of skin.shpk. </summary>
    public static MaterialBuilder BuildSkin(Material material, string name, SkinShaderParameters? customizeParameter)
    {
        // Trust me bro.
        const uint categorySkinType = 0x380CAED0;
        const uint valueFace = 0xF5673524;

        // Face is the default for the skin shader, so a lack of skin type category is also correct.
        var isFace = !material.ShaderKeys
                              .Any(key => key.Category == categorySkinType && key.Value != valueFace);

        var diffuse = material.GetTexture(TextureUsage.SamplerDiffuse).Resource.ToTexture();
        var normal = material.GetTexture(TextureUsage.SamplerNormal).Resource.ToTexture();
        var mask = material.GetTexture(TextureUsage.SamplerMask).Resource.ToTexture();

        
        var resizedNormal = normal.Resize(diffuse.Width, diffuse.Height);
        for (var x = 0; x < diffuse.Width; x++)
        for (var y = 0; y < diffuse.Height; y++)
        {
            var diffusePixel = ToVector4(diffuse[x, y]);
            var normalPixel = ToVector4(resizedNormal[x, y]);
            diffuse[x, y] = diffusePixel.WithW(normalPixel.Z);
        }

        var resizedMask = mask.Resize(diffuse.Width, diffuse.Height);
        if (customizeParameter != null)
        {
            for (var x = 0; x < diffuse.Width; x++)
            for (var y = 0; y < diffuse.Height; y++)
            {
                // R: Skin color intensity
                // G: Specular intensity - todo maybe
                // B: Lip intensity
                var maskPixel = ToVector4(resizedMask[x, y]);
                var diffusePixel = ToVector4(diffuse[x, y]);
                var alpha = diffusePixel.W;
                if (maskPixel.X > 0)
                {
                    var skinColorIntensity = maskPixel.X;
                    // NOTE: SkinColor alpha channel is muscle tone
                    diffusePixel = Vector4.Lerp(diffusePixel, customizeParameter.SkinColor, skinColorIntensity * 0.5f);
                }

                if (customizeParameter.IsHrothgar && !isFace)
                {
                    // Mask G is hair color intensity
                    // Mask B is hair highlight intensity
                    var hairColorIntensity = maskPixel.Y;
                    var highlightIntensity = maskPixel.Z;
                    var hairColor = Vector3.Lerp(diffusePixel.XYZ, customizeParameter.MainColor, hairColorIntensity);
                    var highlightColor = Vector3.Lerp(hairColor, customizeParameter.MeshColor, highlightIntensity);
                    diffusePixel = new Vec4Ext(highlightColor, diffusePixel.W);
                }

                if (!customizeParameter.IsHrothgar && isFace && customizeParameter.ApplyLipColor)
                {
                    // Lerp between base colour and lip colour based on the blue channel
                    var lipIntensity = maskPixel.Z;
                    diffusePixel = Vector4.Lerp(diffusePixel, customizeParameter.LipColor, 
                                                lipIntensity * customizeParameter.LipColor.W);
                }

                // keep original alpha
                diffuse[x, y] = diffusePixel.WithW(alpha);
            }
        }

        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(diffuse, name, "basecolor"))
               .WithNormal(BuildImage(normal, name, "normal"))
               .WithOcclusion(BuildImage(mask, name, "mask"))
               .WithAlpha(isFace ? AlphaMode.MASK : AlphaMode.OPAQUE, 0.5f);
    }

    public static MaterialBuilder BuildFallback(Material material, string name)
    {
        var materialBuilder = BuildSharedBase(material, name)
                              .WithMetallicRoughnessShader()
                              .WithBaseColor(Vector4.One);

        if (material.TryGetTexture(TextureUsage.SamplerDiffuse, out var diffuse))
            materialBuilder.WithBaseColor(BuildImage(diffuse.Resource.ToTexture(), name, "basecolor"));

        if (material.TryGetTexture(TextureUsage.SamplerNormal, out var normal))
            materialBuilder.WithNormal(BuildImage(normal.Resource.ToTexture(), name, "normal"));

        return materialBuilder;
    }
    
    public static MaterialBuilder BuildSharedBase(Material material, string name)
    {
        const uint backfaceMask  = 0x1;
        var        showBackfaces = (material.ShaderFlags & backfaceMask) == 0;
        
        return new MaterialBuilder(name)
            .WithDoubleSide(showBackfaces);
    }

    public static Vec4Ext ToVector4(this SKColor color) => new(color);
    public static SKColor ToSkColor(this Vector4 color) => 
        new((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), (byte)(color.W * 255));
    public static SKColor ToSkColor(this Vector3 color) => 
        new((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), byte.MaxValue);
    
    public static ImageBuilder BuildImage(SKTexture texture, string materialName, string suffix)
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
    }
    
    public static MaterialBuilder ParseMaterial(Material material, CustomizeParameters? customizeParameter = null)
    {
        var name = $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_{Path.GetFileNameWithoutExtension(material.ShaderPackage.Name)}";

        return material.ShaderPackage.Name switch
        {
            "character.shpk" => MaterialParamUtility.BuildCharacter(material),
            "characterglass.shpk" => MaterialParamUtility.BuildCharacter(material),
            "hair.shpk" => BuildHair(material, name, HairShaderParameters.From(customizeParameter)),
            "iris.shpk"           => BuildIris(material, name, customizeParameter?.LeftColor),
            "skin.shpk"           => BuildSkin(material, name, SkinShaderParameters.From(customizeParameter)),
            _ => BuildFallback(material, name),
        };
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

            result[x, y] = resultPixel;
        }

        return result;
    }
}
