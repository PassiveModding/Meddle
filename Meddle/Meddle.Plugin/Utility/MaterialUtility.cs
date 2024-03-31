using System.Diagnostics;
using System.Numerics;
using Dalamud.Utility.Numerics;
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

    public static MaterialBuilder BuildCharacter(Material material, string name)
    {
        var parameters = material.Parameters!.Value;
        
        var srcNormal = material.GetTexture(TextureUsage.SamplerNormal);
        var operation = new ProcessCharacterNormalOperation(srcNormal.Resource.ToTexture(), material.ColorTable).Run();
        var normal = operation.Normal;
        var diffuseColor = operation.BaseColor;
        var specularMask = operation.Specular;
        
        if (material.TryGetTexture(TextureUsage.SamplerDiffuse, out var diff))
        {
            var diffuse = diff.Resource.ToTexture((diffuseColor.Width, diffuseColor.Height));
            diffuseColor = MultiplyBitmaps(diffuseColor, diffuse);
        }

        if (material.TryGetTexture(TextureUsage.SamplerSpecular, out var spec))
        {
            var specular = spec.Resource.ToTexture((specularMask.Width, specularMask.Height));
            specularMask = MultiplyBitmaps(specularMask, specular);
        }

        var fresnelValue0 = new SKTexture(normal.Width, normal.Height);
        var shininess = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            fresnelValue0[x, y] = ToSkColor(parameters.FresnelValue0);
            shininess[x, y] = ToSkColor(new Vector4(parameters.Shininess));
        }

        if (material.TryGetTexture(TextureUsage.SamplerMask, out var mask))
        {
            var samplerMask = mask.Resource.ToTexture((normal.Width, normal.Height));
            for (var x = 0; x < normal.Width; x++)
            for (var y = 0; y < normal.Height; y++)
            {
                var maskPixel = ToVector4(samplerMask[x, y]);
                var maskSSq = maskPixel * maskPixel;
                var diffuseVec = ToVector4(diffuseColor[x, y]);
                var fresnelVec = ToVector4(fresnelValue0[x, y]);
                var specularVec = ToVector4(specularMask[x, y]);

                diffuseColor[x, y] = ToSkColor((diffuseVec * maskSSq.X).WithW(diffuseVec.W));
                fresnelValue0[x, y] = ToSkColor((fresnelVec * maskSSq.Y).WithW(fresnelVec.W));
                specularMask[x, y] = ToSkColor((specularVec * maskSSq.Z).WithW(specularVec.W));
            }
        }
        
        var emissiveColor = operation.Emissive;
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var emissivePixel = ToVector4(emissiveColor[x, y]);
            emissivePixel = ToVector4(emissiveColor[x, y]) * new Vector4(parameters.EmissiveColor, emissivePixel.W);
            emissiveColor[x, y] = ToSkColor(emissivePixel);
        }
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(diffuseColor,      name, "basecolor"))
               .WithNormal(BuildImage(normal,     name, "normal"))
               .WithEmissive(BuildImage(emissiveColor, name, "emissive"), Vector3.One, 1)
               .WithSpecularFactor(BuildImage(specularMask, name, "specular"), 1)
               .WithSpecularColor(BuildImage(fresnelValue0, name, "fresnel"));
    }
    
    public static MaterialBuilder BuildCharacter2(Material material, string name)
    {
        var normal = material.GetTexture(TextureUsage.SamplerNormal);
        
        var operation = new ProcessCharacterNormalOperation(normal.Resource.ToTexture(), material.ColorTable!).Run();
        
        var baseColor = operation.BaseColor;
        if (material.TryGetTexture(TextureUsage.SamplerDiffuse, out var diffuseTexture))
        {
            var diffuse = diffuseTexture.Resource.ToTexture();
            // ensure same size
            if (diffuse.Width < baseColor.Width || diffuse.Height < baseColor.Height)
                diffuse = new SKTexture(diffuse.Bitmap.Resize(new SKImageInfo(baseColor.Width, baseColor.Height), SKFilterQuality.High));
            if (diffuse.Width > baseColor.Width || diffuse.Height > baseColor.Height)
                baseColor = new SKTexture(baseColor.Bitmap.Resize(new SKImageInfo(diffuse.Width, diffuse.Height), SKFilterQuality.High));
            
            baseColor = MultiplyBitmaps(baseColor, diffuse);
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

    public static MaterialBuilder BuildHair(Material material, string name, HairShaderParameters? customizeParameters)
    {
        var parameters = material.Parameters!.Value;
        // Trust me bro.
        const uint categoryHairType = 0x24826489;
        const uint valueFace        = 0x6E5B8F10;
        const uint valueHair        = 0xF7B8956E;
        var isFace = material.ShaderKeys
                             .Any(key => key is { Category: categoryHairType, Value: valueFace });
        var isHair = material.ShaderKeys
                             .Any(key => key is { Category: categoryHairType, Value: valueHair });
        
        var hairCol      = customizeParameters?.MainColor ?? DefaultHairColor;
        var highlightCol = customizeParameters?.MeshColor ?? DefaultHighlightColor;
        var optionCol    = customizeParameters?.OptionColor ?? Vector3.Zero;
        var hairFresnel  = customizeParameters?.HairFresnelValue0 ?? Vector3.Zero;
        
        
        var normal = material.GetTexture(TextureUsage.SamplerNormal).Resource.ToTexture();
        var mask   = material.GetTexture(TextureUsage.SamplerMask).Resource.ToTexture((normal.Width, normal.Height));
        var baseColor = new SKTexture(normal.Width, normal.Height);
        var specular = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            // PS_Input = ps
            // this represents the pixel in the shader
            // since we are not using a shader we need to replicate the logic here
            
            var normalVec = ToVector4(normal[x, y]);
            var maskVec = ToVector4(mask[x, y]);
            if (isHair)
            {
                var diffuseColor = Vector3.Lerp(hairCol, highlightCol, maskVec.W);
                baseColor[x, y] = ToSkColor(new Vector4(diffuseColor, normalVec.W));
            }
            
            if (isFace)
            {
                var diffuseColor = Vector3.Lerp(hairCol, optionCol, maskVec.W);
                baseColor[x, y] = ToSkColor(new Vector4(diffuseColor, normalVec.W));
            }
            
            specular[x, y] = ToSkColor(new Vector4(maskVec.Y * parameters.SpecularMask).WithW(normalVec.W));
        }

        var specularImg = BuildImage(specular, name, "specular");
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(baseColor, name, "basecolor"), new Vector4(parameters.DiffuseColor, 1))
               .WithNormal(BuildImage(normal,       name, "normal"), parameters.NormalScale)
               .WithSpecularFactor(specularImg, 1)
               .WithSpecularColor(specularImg, hairFresnel)
               .WithAlpha(isFace ? AlphaMode.BLEND : AlphaMode.MASK, parameters.AlphaThreshold);
    }
    
    
    /// <summary> Build a material following the semantics of hair.shpk. </summary>
    public static MaterialBuilder BuildHair2(Material material, string name, HairShaderParameters? customizeParameters)
    {
        // Trust me bro.
        const uint categoryHairType = 0x24826489;
        const uint valueFace        = 0x6E5B8F10;
        
        var hairCol      = customizeParameters?.MainColor ?? DefaultHairColor;
        var highlightCol = customizeParameters?.MeshColor ?? DefaultHighlightColor;
        
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
            if (isFace)
            {
                // Alpha = Tattoo/Limbal/Ear Clasp Color (OptionColor)
                var alpha = maskPixel.Alpha / 255f;

                var color = customizeParameters?.OptionColor != null ? 
                                Vector3.Lerp(hairCol, customizeParameters.OptionColor, alpha) : 
                                hairCol;
                
                // Eyelashes shouldn't be mutated here but logic is too dependent on shader to fix
                baseColor[x, y] = ToSkColor(color).WithAlpha(normalPixel.Alpha);
            }
            else
            {
                var color = Vector3.Lerp(hairCol,
                                         highlightCol,
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
        const uint valueFace        = 0xF5673524;

        // Face is the default for the skin shader, so a lack of skin type category is also correct.
        var isFace = !material.ShaderKeys
               .Any(key => key.Category == categorySkinType && key.Value != valueFace);
        var parameters = material.Parameters!.Value;
        
        var diffuse = material.GetTexture(TextureUsage.SamplerDiffuse).Resource.ToTexture();
        var normal  = material.GetTexture(TextureUsage.SamplerNormal).Resource.ToTexture((diffuse.Width, diffuse.Height));
        var mask    = material.GetTexture(TextureUsage.SamplerMask).Resource.ToTexture((diffuse.Width, diffuse.Height));
        
        var skinColor = customizeParameter?.SkinColor ?? new Vector4(255f, 223f, 220f, 255f) / 255f;
        var skinFresnel = customizeParameter?.SkinFresnelValue0 ?? new Vector3(0.25f, 0.25f, 0.25f);
        var skinFresnelW = customizeParameter?.SkinFresnelValue0W ?? 32f;
        var hairFresnel = customizeParameter?.HairFresnelValue0 ?? new Vector3(0.86f, 0.86f, 0.86f);
        var hairColor = customizeParameter?.MainColor ?? DefaultHairColor;
        var hairHighlight = customizeParameter?.MeshColor ?? DefaultHighlightColor;
        var lipColor = customizeParameter?.LipColor ?? new Vector4(120f, 69f, 104f, 153f) / 255f;
        var isHrothgar = customizeParameter?.IsHrothgar ?? false;
        
        
        var fresnelMap = new SKTexture(diffuse.Width, diffuse.Height);
        var shininessMap = new SKTexture(diffuse.Width, diffuse.Height);
        for (var x = 0; x < diffuse.Width; x++)
        for (var y = 0; y < diffuse.Height; y++)
        {
            var normalVec = ToVector4(normal[x, y]);
            var mtrlDiffuse = ToVector4(diffuse[x, y]).WithW(normalVec.W);
            normalVec = normalVec.WithW(byte.MaxValue);
            
            if (mtrlDiffuse.W == 0f)
            {
                normal[x, y] = ToSkColor(normalVec);
                diffuse[x, y] = ToSkColor(mtrlDiffuse);
                continue;
            }
            
            var maskS = ToVector4(mask[x, y]);
            // comp.diffuseColor = lerp(1, g_CustomizeParameter.m_SkinColor.xyz, maskS.x);
            // comp.fresnelValue0 = lerp(mtrlFresnelValue0Sq, g_CustomizeParameter.m_SkinFresnelValue0.xyz, maskS.x);
            var diffuseColor = Vector4.Lerp(Vector4.One, skinColor, maskS.X);
            var fresnelValue0 = Vector3.Lerp(parameters.FresnelValue0, skinFresnel, maskS.X);
            
            float specularSq;
            if (isHrothgar)
            {
                specularSq = 0.04f;
                var hc = Vector3.Lerp(
                    hairColor, 
                    hairHighlight, 
                    maskS.Z);
                var diffuseCol = Vector3.Lerp(
                    new Vector3(diffuseColor.X, diffuseColor.Y, diffuseColor.Z), 
                    hc, 
                    maskS.Y);
                
                diffuseColor = new Vector4(diffuseCol, diffuseColor.W);
            }
            else
            {
                specularSq = maskS.Y * maskS.Y;
            }
            
            // comp.diffuseColor *= diffuseSSq;
            // comp.shininess = g_Shininess;
            var shininess = parameters.Shininess;
            if (isFace)
            {
                var lipInfluence = maskS.Z * (lipColor.W > 0.1f ? 1.0f : 0f);
                fresnelValue0 = Vector3.Lerp(fresnelValue0, parameters.LipFresnelValue0, lipInfluence);
                shininess = Lerp(shininess, parameters.LipShininess, lipInfluence);
            }

            if (isHrothgar)
            {
                fresnelValue0 = Vector3.Lerp(fresnelValue0, hairFresnel, maskS.Y);
            }

            if (isFace)
            {
                // comp.diffuseColor = lerp(comp.diffuseColor, g_CustomizeParameter.m_LipColor.xyz, g_CustomizeParameter.m_LipColor.w * maskS.z);
                diffuseColor = Vector4.Lerp(
                    diffuseColor, 
                    lipColor, 
                    maskS.Z * lipColor.W);
                
            }
            
            // keep original alpha
            diffuse[x, y] = ToSkColor((diffuseColor * mtrlDiffuse).WithW(mtrlDiffuse.W));
            fresnelMap[x, y] = ToSkColor(new Vector4(fresnelValue0 * specularSq, skinFresnelW));
            shininessMap[x, y] = ToSkColor(new Vector4(0, 0, 0, shininess));
        }
        
        return BuildSharedBase(material, name)
               .WithBaseColor(BuildImage(diffuse, name, "basecolor"), new Vector4(parameters.DiffuseColor, 1))
               .WithNormal(BuildImage(normal,     name, "normal"), parameters.NormalScale)
               .WithOcclusion(BuildImage(mask, name, "mask"))
               .WithSpecularFactor(BuildImage(mask, name, "mask"), 1)
               .WithSpecularColor(BuildImage(fresnelMap, name, "fresnel"))
               .WithEmissive(BuildImage(shininessMap, name, "shininess"), parameters.EmissiveColor)
               .WithAlpha(isFace ? AlphaMode.MASK : AlphaMode.OPAQUE, parameters.AlphaThreshold);
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
    
    public static MaterialBuilder BuildFallback(Material material, string name)
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
    
    private static Vector4 ToVector4(SKColor color)
    {
        return new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);
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
                var prevRow  = table[tableRow.Previous];
                var nextRow  = table[tableRow.Next];
            
                // Base colour (table, .b)
                var lerpedDiffuse = Vector3.Lerp(prevRow.Diffuse, nextRow.Diffuse, tableRow.Weight);
                BaseColor[x, y] = ToSkColor(new Vector4(lerpedDiffuse, 1)).WithAlpha(normalPixel.Blue);
            
                // Specular (table)
                var lerpedSpecularColor = Vector3.Lerp(prevRow.Specular, nextRow.Specular, tableRow.Weight);
                // float.Lerp is .NET8 ;-;
                //var lerpedSpecularFactor = (prevRow.SpecularStrength * (1.0f - tableRow.Weight)) + (nextRow.SpecularStrength * tableRow.Weight);
                var lerpedSpecularFactor = Lerp(prevRow.SpecularStrength, nextRow.SpecularStrength, tableRow.Weight);
                Specular[x, y] = ToSkColor(new Vector4(lerpedSpecularColor, lerpedSpecularFactor));
            
                // Emissive (table)
                var lerpedEmissive = Vector3.Lerp(prevRow.Emissive, nextRow.Emissive, tableRow.Weight);
                Emissive[x, y] = ToSkColor(new Vector4(lerpedEmissive, 1));
            
                // Normal (.rg)
                Normal[x, y] = new SKColor(normalPixel.Red, normalPixel.Green, byte.MaxValue, normalPixel.Blue);

                /*
                if (normalPixel is {Red: 0, Green: 0})
                {
                    Normal[x, y] = new SKColor(byte.MaxValue/2, byte.MaxValue/2, byte.MaxValue, byte.MinValue);
                }
                else
                {
                    Normal[x, y] = new SKColor(normalPixel.Red, normalPixel.Green, byte.MaxValue, normalPixel.Blue);
                }*/
            }
            
            return this;
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
