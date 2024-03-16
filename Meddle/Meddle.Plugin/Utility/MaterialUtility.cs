using System.Numerics;
using Lumina.Data.Parsing;
using Meddle.Plugin.Models;
using SharpGLTF.Materials;
using SkiaSharp;

namespace Meddle.Plugin.Utility;

public class MaterialUtility
{
    /// <summary> Dependency-less material configuration, for use when no material data can be resolved. </summary>
    public static readonly MaterialBuilder Unknown = new MaterialBuilder("UNKNOWN")
                                                     .WithMetallicRoughnessShader()
                                                     .WithDoubleSide(true)
                                                     .WithBaseColor(Vector4.One);
    
    private static readonly Vector4 DefaultEyeColor = new Vector4(21, 176, 172, 255) / new Vector4(255);
    private static readonly Vector3 DefaultHairColor      = new Vector3(130, 64,  13) / new Vector3(255);
    private static readonly Vector3 DefaultHighlightColor = new Vector3(77,  126, 240) / new Vector3(255);
    
    public static MaterialBuilder ParseMaterial(Material material, string name, CustomizeParameters? customizeParameter = null)
    {
        name = $"{name}_{material.ShaderPackage.Name.Replace(".shpk", "")}";

        return material.ShaderPackage.Name switch
        {
            "character.shpk" => BuildCharacter(material, name).WithAlpha(AlphaMode.MASK, 0.5f),
            "characterglass.shpk" => BuildCharacter(material, name).WithAlpha(AlphaMode.BLEND),
            "hair.shpk" => BuildHair(material, name, customizeParameter),
            "iris.shpk"           => BuildIris(material, name, customizeParameter),
            "skin.shpk"           => BuildSkin(material, name, customizeParameter),
            _ => BuildFallback(material, name),
        };
    }
    
    private static MaterialBuilder BuildCharacter(Material material, string name)
    {
        var normal = material.GetTexture(TextureUsage.SamplerNormal);
        
        var operation = new ProcessCharacterNormalOperation(normal.Resource.ToTexture(), material.ColorTable).Run();
        
        var baseColor = operation.BaseColor;
        if (material.TryGetTexture(TextureUsage.SamplerDiffuse, out var diffuseTexture))
        {
            var diffuse = diffuseTexture.Resource.ToTexture();
            // ensure same size
            if (diffuse.Width < baseColor.Width || diffuse.Height < baseColor.Height)
                diffuse = new SKTexture(diffuse.Bitmap.Resize(new SKImageInfo(baseColor.Width, baseColor.Height), SKFilterQuality.High));
            if (diffuse.Width > baseColor.Width || diffuse.Height > baseColor.Height)
                baseColor = new SKTexture(baseColor.Bitmap.Resize(new SKImageInfo(diffuse.Width, diffuse.Height), SKFilterQuality.High));
            
            baseColor = MultiplyBitmaps(diffuse, baseColor);
        }
        
        var specular = operation.Specular;
        if (material.TryGetTexture(TextureUsage.SamplerSpecular, out var specularTexture))
        {
            var spec = specularTexture.Resource.ToTexture();
            // ensure same size
            if (spec.Width < specular.Width || spec.Height < specular.Height)
                spec = new SKTexture(spec.Bitmap.Resize(new SKImageInfo(specular.Width, specular.Height), SKFilterQuality.High));
            if (spec.Width > specular.Width || spec.Height > specular.Height)
                specular = new SKTexture(specular.Bitmap.Resize(new SKImageInfo(spec.Width, spec.Height), SKFilterQuality.High));
            specular = MultiplyBitmaps(spec, specular);
        }
        
        if (material.TryGetTexture(TextureUsage.SamplerMask, out var mask))
        {
            var maskImage = mask.Resource.ToTexture((baseColor.Width, baseColor.Height));

            for (var x = 0; x < baseColor.Width; x++)
            for (int y = 0; y < baseColor.Height; y++)
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
    private static MaterialBuilder BuildHair(Material material, string name, CustomizeParameters? customizeParameter = null)
    {
        // Trust me bro.
        const uint categoryHairType = 0x24826489;
        const uint valueFace        = 0x6E5B8F10;
        
        var hairCol      = customizeParameter?.MainColor ?? DefaultHairColor;
        
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
            if (isFace && customizeParameter?.OptionColor != null)
            {
                // Alpha = Tattoo/Limbal/Ear Clasp Color (OptionColor)
                var alpha = maskPixel.Alpha / 255f;
                
                var color = Vector3.Lerp(hairCol, customizeParameter.OptionColor, alpha);
                baseColor[x, y] = ToSkColor(color).WithAlpha(normalPixel.Alpha);
            }

            if (!isFace)
            {
                var color = Vector3.Lerp(hairCol, 
                                         customizeParameter?.MeshColor ?? DefaultHighlightColor, 
                                         maskPixel.Alpha / 255f);
                baseColor[x, y] = ToSkColor(color).WithAlpha(normalPixel.Alpha);
                
                // Mask red channel is occlusion supposedly
                occlusion[x, y] = new SKColor(maskPixel.Red, maskPixel.Red, maskPixel.Red, maskPixel.Red);
            }
            
            // mask green channel is specular
            specular[x, y] = new SKColor(maskPixel.Green, maskPixel.Green, maskPixel.Green, maskPixel.Green);
            
            // meh
            if (normalPixel is {Red: 0, Green: 0, Blue: 0})
            {
                normal[x, y] = new SKColor(byte.MaxValue / 2, byte.MaxValue / 2, byte.MaxValue, byte.MinValue);
            }
        }
        
        //if (!isFace)
        //    builder.WithOcclusion(BuildImage(occlusion, name, "occlusion"));
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
               .WithNormal(BuildImage(normal,       name, "normal"))
               .WithSpecularFactor( BuildImage(specular, name, "specular"), 1)
               .WithAlpha(isFace ? AlphaMode.BLEND : AlphaMode.MASK, 0.5f);
        
    }
    
    private static MaterialBuilder BuildIris(Material material, string name, CustomizeParameters? customizeParameter = null)
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
            var color = (customizeParameter?.LeftColor ?? DefaultEyeColor) * new Vector4(maskPixel.Red / 255f);
            color.W = normalPixel.Alpha / 255f;

            baseColor[x, y] = ToSkColor(color);
            normal[x, y] = normalPixel.WithAlpha(byte.MaxValue);
        }

        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"))
               .WithNormal(BuildImage(normal, name, "normal"));
    }
    
        /// <summary> Build a material following the semantics of skin.shpk. </summary>
    private static MaterialBuilder BuildSkin(Material material, string name, CustomizeParameters? customizeParameter = null)
    {
        // Trust me bro.
        const uint categorySkinType = 0x380CAED0;
        const uint valueFace        = 0xF5673524;

        // Face is the default for the skin shader, so a lack of skin type category is also correct.
        var isFace = !material.ShaderKeys
               .Any(key => key.Category == categorySkinType && key.Value != valueFace);
        
        var diffuseTexture = material.GetTexture(TextureUsage.SamplerDiffuse);
        var normalTexture  = material.GetTexture(TextureUsage.SamplerNormal);
        var maskTexture    = material.GetTexture(TextureUsage.SamplerMask);
        
        var diffuse = diffuseTexture.Resource.ToTexture();
        var normal  = normalTexture.Resource.ToTexture();
        var mask    = maskTexture.Resource.ToTexture();
        
        var resizedNormal = new SKTexture(normal.Bitmap.Resize(new SKImageInfo(diffuse.Width, diffuse.Height), SKFilterQuality.High));

        for (var x = 0; x < diffuse.Width; x++)
        for (int y = 0; y < diffuse.Height; y++)
        {
            var diffusePixel = diffuse[x, y];
            var normalPixel = resizedNormal[x, y];
            diffuse[x, y] = new SKColor(diffusePixel.Red, diffusePixel.Green, diffusePixel.Blue, normalPixel.Blue);
        }
        
        // Clear the blue channel out of the normal now that we're done with it.
        for (var x = 0; x < normal.Width; x++)
        for (int y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal[x, y];
            normal[x, y] = new SKColor(normalPixel.Red, normalPixel.Green, byte.MaxValue, normalPixel.Blue);
        }
        
        var resizedMask = new SKTexture(mask.Bitmap.Resize(new SKImageInfo(diffuse.Width, diffuse.Height), SKFilterQuality.High));
        if (customizeParameter != null)
        {
            for (var x = 0; x < diffuse.Width; x++)
            for (int y = 0; y < diffuse.Height; y++)
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
                    diffuseVec = FloatLerp(diffuseVec, customizeParameter.SkinColor, skinColorIntensity * 0.5f);
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
                    diffuseVec = FloatLerp(diffuseVec, customizeParameter.LipColor,
                                           lipIntensity * customizeParameter.LipColor.W);
                }
                
                // keep original alpha
                diffuse[x, y] = ToSkColor(diffuseVec).WithAlpha(diffusePixel.Alpha);
            }
        }
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(diffuse, name, "basecolor"))
               .WithNormal(BuildImage(normal,     name, "normal"))
               .WithOcclusion(BuildImage(mask, name, "mask"))
               .WithAlpha(isFace ? AlphaMode.MASK : AlphaMode.OPAQUE, 0.5f);
    }
        
    private static Vector4 FloatLerp(Vector4 a, Vector4 b, float t)
    {
        return new(
            Lerp(a.X, b.X, t),
            Lerp(a.Y, b.Y, t),
            Lerp(a.Z, b.Z, t),
            Lerp(a.W, b.W, t)
        );
    }
    
    private static float Lerp(float a, float b, float t)
    {
        return a * (1 - t) + b * t;
    }
    
    private static MaterialBuilder BuildFallback(Material material, string name)
    {
        var materialBuilder = BuildSharedBase(material, name)
                              .WithMetallicRoughnessShader()
                              .WithBaseColor(Vector4.One);

        if (material.TryGetTexture(TextureUsage.SamplerDiffuse, out var diffuse))
            materialBuilder.WithBaseColor(BuildImage(diffuse, name, "basecolor"));

        if (material.TryGetTexture(TextureUsage.SamplerNormal, out var normal))
            materialBuilder.WithNormal(BuildImage(normal, name, "normal"));

        return materialBuilder;
    }
    
    private static MaterialBuilder BuildSharedBase(Material material, string name)
    {
        const uint backfaceMask  = 0x1;
        var        showBackfaces = (material.ShaderFlags & backfaceMask) == 0;
        
        return new MaterialBuilder(name)
            .WithDoubleSide(showBackfaces);
    }
    
    private static SKColor ToSkColor(Vector4 color)
    {
        return new SKColor((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), (byte)(color.W * 255));
    }
    
    private static SKColor ToSkColor(Vector3 color)
    {
        return new SKColor((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), byte.MaxValue);
    }

    private static ImageBuilder BuildImage(Texture texture, string materialName, string suffix)
    {
        return BuildImage(texture.Resource.ToTexture(), materialName, suffix);
    }
    
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
                            * ((-input * 15) + MathF.Floor((input * 15) + 0.5f)))
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
            {
                ProcessRow(y, table);
            }
            
            return this;
        }
        
        private void ProcessRow(int y, ColorTable colorTable)
        {
            for (var x = 0; x < normal.Width; x++)
            {
                var normalPixel = Normal[x, y];
                
                // Table row data (.a)
                var tableRow = GetTableRowIndices(normalPixel.Alpha / 255f);
                var prevRow  = colorTable.Rows[tableRow.Previous];
                var nextRow  = colorTable.Rows[tableRow.Next];
                
                // Base colour (table, .b)
                var lerpedDiffuse = Vector3.Lerp(prevRow.Diffuse, nextRow.Diffuse, tableRow.Weight);
                var diff = new Vector4(lerpedDiffuse, 1);
                BaseColor[x, y] = ToSkColor(diff).WithAlpha(normalPixel.Blue);
                
                // Specular (table)
                var lerpedSpecularColor = Vector3.Lerp(prevRow.Specular, nextRow.Specular, tableRow.Weight);
                // float.Lerp is .NET8 ;-;
                //var lerpedSpecularFactor = (prevRow.SpecularStrength * (1.0f - tableRow.Weight)) + (nextRow.SpecularStrength * tableRow.Weight);
                var lerpedSpecularFactor = Lerp(prevRow.SpecularStrength, nextRow.SpecularStrength, tableRow.Weight);
                var spec = new Vector4(lerpedSpecularColor, lerpedSpecularFactor);
                Specular[x, y] = ToSkColor(spec);
                
                // Emissive (table)
                var lerpedEmissive = Vector3.Lerp(prevRow.Emissive, nextRow.Emissive, tableRow.Weight);
                var emis = new Vector4(lerpedEmissive, 1);
                Emissive[x, y] = ToSkColor(emis);
                
                // Normal (.rg)
                if (normalPixel is {Red: 0, Green: 0})
                {
                    Normal[x, y] = new SKColor(byte.MaxValue/2, byte.MaxValue/2, byte.MaxValue, byte.MinValue);
                }
                else
                {
                    Normal[x, y] = new SKColor(normalPixel.Red, normalPixel.Green, byte.MaxValue, normalPixel.Blue);
                }
            }
        }
    }

    public static SKTexture MultiplyBitmaps(SKTexture target, SKTexture multiplier)
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
                targetPixel.Alpha
            );
            result[x, y] = resultPixel;
        }

        return result;
    }
}
