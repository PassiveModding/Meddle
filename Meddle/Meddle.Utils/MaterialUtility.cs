using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SkiaSharp;

namespace Meddle.Utils;

public static class MaterialUtility
{
    private static readonly Vector4 DefaultEyeColor       = new Vector4(21, 176, 172, 255) / 255f;
    private static readonly Vector4 DefaultHairColor      = new Vector4(130, 64,  13, 255) / 255f;
    private static readonly Vector4 DefaultHighlightColor = new Vector4(77,  126, 240, 255) / 255f;
    
    public static MaterialBuilder BuildCharacter(Material material, string name)
    {
        var normal = material.GetTexture(TextureUsage.g_SamplerNormal);
        
        var operation = new ProcessCharacterNormalOperation(normal.ToTexture(), material.ColorTable!.Value).Run();
        
        var baseColor = operation.BaseColor;
        if (material.TryGetTexture(TextureUsage.g_SamplerDiffuse, out var diffuseTexture))
        {
            var diffuse = diffuseTexture.ToTexture((baseColor.Width, baseColor.Height));
            baseColor = MultiplyBitmaps(baseColor, diffuse);
        }
        
        var specular = operation.Specular;
        if (material.TryGetTexture(TextureUsage.g_SamplerSpecular, out var specularTexture))
        {
            var spec = specularTexture.ToTexture((specular.Width, specular.Height));
            specular = MultiplyBitmaps(spec, specular);
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerMask, out var mask))
        {
            var maskImage = mask.ToTexture((baseColor.Width, baseColor.Height));

            for (var x = 0; x < baseColor.Width; x++)
            for (var y = 0; y < baseColor.Height; y++)
            {
                var maskPixel = ToVector4(maskImage[x,y]);
                var baseColorPixel = ToVector4(baseColor[x, y]);
                var alpha = baseColorPixel.W;
                baseColor[x, y] = ToSkColor((baseColorPixel * maskPixel.X) with{ W = alpha });
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
    public static MaterialBuilder BuildHair(Material material, string name)
    {
        // Trust me bro.
        const uint categoryHairType = 0x24826489;
        const uint valueFace        = 0x6E5B8F10;
        
        var hairCol      = new Vector3(DefaultHairColor.X, DefaultHairColor.Y, DefaultHairColor.Z);
        
        var isFace = material.ShaderKeys
            .Any(key => key is { Category: categoryHairType, Value: valueFace });
        
        var normalTexture = material.GetTexture(TextureUsage.g_SamplerNormal);
        var maskTexture   = material.GetTexture(TextureUsage.g_SamplerMask);
        
        var normal = normalTexture.ToTexture();
        var mask   = maskTexture.ToTexture((normal.Width, normal.Height));
        
        var baseColor = new SKTexture(normal.Width, normal.Height);
        var specular = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = ToVector4(normal[x, y]);
            var maskPixel = ToVector4(mask[x, y]);

            if (!isFace)
            {
                var highlight = new Vector3(DefaultHighlightColor.X, DefaultHighlightColor.Y, DefaultHighlightColor.Z);
                var color = Vector3.Lerp(hairCol, highlight, 
                                         maskPixel.W);
                baseColor[x, y] = ToSkColor(new Vector4(color, normalPixel.W));
            }
            
            // mask green channel is specular
            specular[x, y] = ToSkColor(new Vector4(maskPixel.Y));
        }
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
               .WithNormal(BuildImage(normal,       name, "normal"))
               //.WithSpecularFactor( BuildImage(specular, name, "specular"), 1)
               .WithAlpha(isFace ? AlphaMode.BLEND : AlphaMode.MASK, 0.5f);
        
    }
    
    public static MaterialBuilder BuildIris(Material material, string name)
    {
        var normalTexture = material.GetTexture(TextureUsage.g_SamplerNormal);
        var maskTexture   = material.GetTexture(TextureUsage.g_SamplerMask);
        
        var normal = normalTexture.ToTexture();
        var mask   = maskTexture.ToTexture((normal.Width, normal.Height));
        
        var baseColor = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = ToVector4(normal[x, y]);
            //var maskPixel = ToVector4(mask[x, y]);

            // Not sure if we can set it per eye since it's done by the shader
            // NOTE: W = Face paint (UV2) U multiplier. since we set it using the alpha it gets ignored for iris either way
            //var color = DefaultEyeColor * maskPixel.X;
            //color.W = normalPixel.W;

            //baseColor[x, y] = ToSkColor(color);
            normal[x, y] = ToSkColor(normalPixel with { W = 1 });
        }

        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
               .WithNormal(BuildImage(normal, name, "normal"));
    }

    /// <summary> Build a material following the semantics of skin.shpk. </summary>
    public static MaterialBuilder BuildSkin(Material material, string name)
    {
        // Trust me bro.
        const uint categorySkinType = 0x380CAED0;
        const uint valueFace = 0xF5673524;

        // Face is the default for the skin shader, so a lack of skin type category is also correct.
        var isFace = !material.ShaderKeys
                              .Any(key => key.Category == categorySkinType && key.Value != valueFace);

        var diffuse = material.GetTexture(TextureUsage.g_SamplerDiffuse).ToTexture();
        var normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        var mask = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture();

        
        var resizedNormal = normal.Resize(diffuse.Width, diffuse.Height);
        for (var x = 0; x < diffuse.Width; x++)
        for (var y = 0; y < diffuse.Height; y++)
        {
            var diffusePixel = ToVector4(diffuse[x, y]);
            var normalPixel = ToVector4(resizedNormal[x, y]);
            //diffuse[x, y] = ToSkColor(diffusePixel with { W = normalPixel.W });
        }

        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(diffuse, name, "basecolor"))
               .WithNormal(BuildImage(normal, name, "normal"))
               //.WithSpecularColor(BuildImage(mask, name, "mask"))
               .WithAlpha(isFace ? AlphaMode.MASK : AlphaMode.OPAQUE, 0.5f);
    }

    public static MaterialBuilder BuildFallback(Material material, string name)
    {
        var materialBuilder = BuildSharedBase(material, name)
                              .WithMetallicRoughnessShader()
                              .WithBaseColor(Vector4.One);

        if (material.TryGetTexture(TextureUsage.g_SamplerDiffuse, out var diffuse))
            materialBuilder.WithBaseColor(BuildImage(diffuse.ToTexture(), name, "basecolor"));

        if (material.TryGetTexture(TextureUsage.g_SamplerNormal, out var normal))
            materialBuilder.WithNormal(BuildImage(normal.ToTexture(), name, "normal"));

        return materialBuilder;
    }
    
    public static MaterialBuilder BuildSharedBase(Material material, string name)
    {
        const uint backfaceMask  = 0x1;
        var        showBackfaces = (material.ShaderFlags & backfaceMask) == 0;
        
        return new MaterialBuilder(name)
            .WithDoubleSide(showBackfaces);
    }

    public static Vector4 ToVector4(this SKColor color) => new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);
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
    
    public static MaterialBuilder ParseMaterial(Material material)
    {
        var name = $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_{Path.GetFileNameWithoutExtension(material.ShaderPackageName)}";

        return material.ShaderPackageName switch
        {
            "character.shpk" => BuildCharacter(material, name),
            "characterglass.shpk" => BuildCharacter(material, name),
            "hair.shpk" => BuildHair(material, name),
            "iris.shpk" => BuildIris(material, name),
            "skin.shpk" => BuildSkin(material, name),
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

            result[x, y] = ToSkColor(resultPixel);
        }

        return result;
    }
}
