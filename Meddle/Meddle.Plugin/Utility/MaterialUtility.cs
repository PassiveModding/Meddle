using System.Numerics;
using Lumina.Data.Parsing;
using Meddle.Plugin.Models;
using SharpGLTF.Materials;
using SkiaSharp;

namespace Meddle.Plugin.Utility;

public static class MaterialUtility
{
    private static readonly Vector4 DefaultEyeColor = new Vector4(21, 176, 172, 255) / new Vector4(255);
    private static readonly Vector3 DefaultHairColor      = new Vector3(130, 64,  13) / new Vector3(255);
    private static readonly Vector3 DefaultHighlightColor = new Vector3(77,  126, 240) / new Vector3(255);
    
    public static MaterialBuilder BuildCharacter(Material material, string name)
    {
        var normal = material.GetTexture(TextureUsage.SamplerNormal);
        
        var operation = new ProcessCharacterNormalOperation(normal.Resource.ToTexture(), material.ColorTable!).Run();
        
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
                var maskPixel = maskImage[x,y];
                var baseColorPixel = baseColor[x, y];
                // multiply base by mask red channel
                var r = maskPixel.Red / 255f;

                var result = new SKColor(
                    (byte)(baseColorPixel.Red * r),
                    (byte)(baseColorPixel.Green * r),
                    (byte)(baseColorPixel.Blue * r),
                    baseColorPixel.Alpha
                );
                baseColor[x, y] = result;
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
        
        var hairCol      = customizeParameters?.MainColor ?? DefaultHairColor;
        
        var isFace = material.ShaderKeys
            .Any(key => key is { Category: categoryHairType, Value: valueFace });
        
        var normalTexture = material.GetTexture(TextureUsage.SamplerNormal);
        var maskTexture   = material.GetTexture(TextureUsage.SamplerMask);
        
        var normal = normalTexture.Resource.ToTexture();
        var mask   = maskTexture.Resource.ToTexture((normal.Width, normal.Height));
        
        var baseColor = new SKTexture(normal.Width, normal.Height);
        var occlusion = new SKTexture(normal.Width, normal.Height);
        var specular = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal[x, y];
            var maskPixel = mask[x, y];
            if (isFace && customizeParameters?.OptionColor != null)
            {
                // Alpha = Tattoo/Limbal/Ear Clasp Color (OptionColor)
                var alpha = maskPixel.Alpha / 255f;
                
                var color = Vector3.Lerp(hairCol, customizeParameters.OptionColor, alpha);
                baseColor[x, y] = ToSkColor(color).WithAlpha(normalPixel.Alpha);
            }

            if (!isFace)
            {
                var color = Vector3.Lerp(hairCol, 
                                         customizeParameters?.MeshColor ?? DefaultHighlightColor, 
                                         maskPixel.Alpha / 255f);
                baseColor[x, y] = ToSkColor(color).WithAlpha(normalPixel.Alpha);
                
                // Mask red channel is occlusion supposedly
                occlusion[x, y] = new SKColor(maskPixel.Red, maskPixel.Red, maskPixel.Red, maskPixel.Red);
            }
            
            // mask green channel is specular
            specular[x, y] = new SKColor(maskPixel.Green, maskPixel.Green, maskPixel.Green, maskPixel.Green);
        }
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
               .WithNormal(BuildImage(normal,       name, "normal"))
               .WithSpecularFactor( BuildImage(specular, name, "specular"), 1)
               .WithAlpha(isFace ? AlphaMode.BLEND : AlphaMode.MASK, 0.5f);
        
    }
    
    public static MaterialBuilder BuildIris(Material material, string name, Vector4? leftEyeColor)
    {
        var normalTexture = material.GetTexture(TextureUsage.SamplerNormal);
        var maskTexture   = material.GetTexture(TextureUsage.SamplerMask);
        
        var normal = normalTexture.Resource.ToTexture();
        var mask   = maskTexture.Resource.ToTexture((normal.Width, normal.Height));
        
        var baseColor = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal[x, y];
            var maskPixel = mask[x, y];

            // Not sure if we can set it per eye since it's done by the shader
            // NOTE: W = Face paint (UV2) U multiplier. since we set it using the alpha it gets ignored for iris either way
            var color = (leftEyeColor ?? DefaultEyeColor) * new Vector4(maskPixel.Red / 255f);
            color.W = normalPixel.Alpha / 255f;

            baseColor[x, y] = ToSkColor(color);
            normal[x, y] = normalPixel.WithAlpha(byte.MaxValue);
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
            var diffusePixel = diffuse[x, y];
            var normalPixel = resizedNormal[x, y];
            diffuse[x, y] = new SKColor(diffusePixel.Red, diffusePixel.Green, diffusePixel.Blue, normalPixel.Blue);
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
                var maskPixel = resizedMask[x, y];
                var diffusePixel = diffuse[x, y];
                var diffuseVec = new Vector4(diffusePixel.Red, diffusePixel.Green, diffusePixel.Blue,
                                             diffusePixel.Alpha) / 255f;

                if (maskPixel.Red > 0)
                {
                    var skinColorIntensity = maskPixel.Red / 255f;
                    // NOTE: SkinColor alpha channel is muscle tone
                    diffuseVec = Vector4.Lerp(diffuseVec, customizeParameter.SkinColor, skinColorIntensity * 0.5f);
                }

                if (customizeParameter.IsHrothgar && !isFace)
                {
                    // Mask G is hair color intensity
                    // Mask B is hair highlight intensity
                    var hairColorIntensity = maskPixel.Green / 255f;
                    var highlightIntensity = maskPixel.Blue / 255f;
                    var diffuseCol = new Vector3(diffuseVec.X, diffuseVec.Y, diffuseVec.Z);
                    var hairColor = Vector3.Lerp(diffuseCol, customizeParameter.MainColor, hairColorIntensity);
                    var highlightColor = Vector3.Lerp(hairColor, customizeParameter.MeshColor, highlightIntensity);
                    diffuseVec = new Vector4(highlightColor, diffuseVec.W);
                }

                if (!customizeParameter.IsHrothgar && isFace && customizeParameter.ApplyLipColor)
                {
                    // Lerp between base colour and lip colour based on the blue channel
                    var lipIntensity = maskPixel.Blue / 255f;
                    diffuseVec = Vector4.Lerp(diffuseVec, customizeParameter.LipColor, 
                                              lipIntensity * customizeParameter.LipColor.W);
                }

                // keep original alpha
                diffuse[x, y] = ToSkColor(diffuseVec).WithAlpha(diffusePixel.Alpha);
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
    
    private static MaterialBuilder BuildSharedBase(Material material, string name)
    {
        const uint backfaceMask  = 0x1;
        var        showBackfaces = (material.ShaderFlags & backfaceMask) == 0;
        
        return new MaterialBuilder(name)
            .WithDoubleSide(showBackfaces);
    }
    
    public static Vector4 ToVector4(this SKColor color) => 
        new Vector4(color.Red, color.Green, color.Blue, color.Alpha) / 255f;
    public static SKColor ToSkColor(this Vector4 color) => 
        new((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), (byte)(color.W * 255));
    public static SKColor ToSkColor(this Vector3 color) => 
        new((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), byte.MaxValue);
    
    private static ImageBuilder BuildImage(SKTexture texture, string materialName, string suffix)
    {
        var name = materialName.Replace("/", "").Replace(".mtrl", "") + $"_{suffix}";
        
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
        var name = $"{Path.GetFileName(material.HandlePath)}_{material.ShaderPackage.Name.Replace(".shpk", "")}";

        return material.ShaderPackage.Name switch
        {
            "character.shpk" => BuildCharacter(material, name).WithAlpha(AlphaMode.MASK, 0.5f),
            "characterglass.shpk" => BuildCharacter(material, name).WithAlpha(AlphaMode.BLEND),
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
            var targetPixel = target[x, y];
            var multPixel = multiplier[x, y];
            var resultPixel = new SKColor(
                (byte)(targetPixel.Red * multPixel.Red / 255f),
                (byte)(targetPixel.Green * multPixel.Green / 255f),
                (byte)(targetPixel.Blue * multPixel.Blue / 255f),
                preserveTargetAlpha ? targetPixel.Alpha : (byte)(targetPixel.Alpha * multPixel.Alpha / 255f)
            );
            result[x, y] = resultPixel;
        }

        return result;
    }
    
    private class ProcessCharacterNormalOperation(SKTexture normal, ColorTable table)
    {
        public SKTexture Normal    { get; } = normal.Copy();
        public SKTexture BaseColor { get; } = new(normal.Width, normal.Height);
        public SKTexture Specular  { get; } = new(normal.Width, normal.Height);
        public SKTexture Emissive  { get; } = new(normal.Width, normal.Height);

        private static TableRow GetTableRowIndices(float input)
        {
            // These calculations are ported from character.shpk.
            var smoothed = (MathF.Floor(input * 7.5f % 1.0f * 2) 
                            * (-input * 15 + MathF.Floor(input * 15 + 0.5f)))
                            + (input * 15);

            var stepped = MathF.Floor(smoothed + 0.5f);

            return new TableRow
            {
                Stepped  = (int)stepped,
                Previous = (int)MathF.Floor(smoothed),
                Next     = (int)MathF.Ceiling(smoothed),
                Weight   = smoothed % 1,
            };
        }
        
        private ref struct TableRow
        {
            public int   Stepped;
            public int   Previous;
            public int   Next;
            public float Weight;
        }
        
        public ProcessCharacterNormalOperation Run()
        {
            for (var y = 0; y < normal.Height; y++)
            for (var x = 0; x < normal.Width; x++)
            {
                var normalPixel = Normal[x, y];
            
                // Table row data (.a)
                var tableRow = GetTableRowIndices(normalPixel.Alpha / 255f);
                var prevRow  = table.Rows[tableRow.Previous];
                var nextRow  = table.Rows[tableRow.Next];
            
                // Base colour (table, .b)
                var lerpedDiffuse = Vector3.Lerp(prevRow.Diffuse, nextRow.Diffuse, tableRow.Weight);
                BaseColor[x, y] = ToSkColor(new Vector4(lerpedDiffuse, 1)).WithAlpha(normalPixel.Blue);
            
                // Specular (table)
                var lerpedSpecularColor = Vector3.Lerp(prevRow.Specular, nextRow.Specular, tableRow.Weight);
                var lerpedSpecularFactor = float.Lerp(prevRow.SpecularStrength, nextRow.SpecularStrength, tableRow.Weight);
                Specular[x, y] = ToSkColor(new Vector4(lerpedSpecularColor, lerpedSpecularFactor));
            
                // Emissive (table)
                var lerpedEmissive = Vector3.Lerp(prevRow.Emissive, nextRow.Emissive, tableRow.Weight);
                Emissive[x, y] = ToSkColor(new Vector4(lerpedEmissive, 1));
            
                // Normal (.rg)
                Normal[x, y] = new SKColor(normalPixel.Red, normalPixel.Green, byte.MaxValue, normalPixel.Blue);
            }
            
            return this;
        }
    }
}
