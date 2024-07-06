using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public enum FlowType : uint
    {
        Standard = 0x337C6BC4, // No flow?
        Flow = 0x71ADA939
    }

    public enum TextureMode : uint
    {
        Default = 0x5CC605B5, // Default mask texture
        Compatibility = 0x600EF9DF, // Used to enable diffuse texture
        Simple = 0x22A4AABF // meh
    }

    public enum SpecularMode : uint
    {
        Mask = 0xA02F4828, // Use mask sampler for specular
        Default = 0x198D11CD // Use spec sampler for specular
    }
    
    public static MaterialBuilder BuildCharacter(Material material, string name)
    {
        const uint textureCategory = 0xB616DC5A; // DEFAULT, COMPATIBILITY, SIMPLE
        const uint specularCategory = 0xC8BD1DEF; // MASK, DEFAULT
        const uint flowMapCategory = 0x40D1481E; // STANDARD, FLOW
        
        // diffuse optional
        TextureMode texMode;
        if (material.ShaderKeys.Any(x => x.Category == textureCategory))
        {
            var key = material.ShaderKeys.First(x => x.Category == textureCategory);
            texMode = (TextureMode)key.Value;
        }
        else
        {
            texMode = TextureMode.Default;
        }
        
        SpecularMode specMode;
        if (material.ShaderKeys.Any(x => x.Category == specularCategory))
        {
            var key = material.ShaderKeys.First(x => x.Category == specularCategory);
            specMode = (SpecularMode)key.Value;
        }
        else
        {
            specMode = 0x0;
        }
        
        FlowType flowType;
        if (material.ShaderKeys.Any(x => x.Category == flowMapCategory))
        {
            var key = material.ShaderKeys.First(x => x.Category == flowMapCategory);
            flowType = (FlowType)key.Value;
        }
        else
        {
            flowType = FlowType.Standard;
        }
        
        
        var normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        var maskTexture = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture((normal.Width, normal.Height));
        var indexTexture = material.GetTexture(TextureUsage.g_SamplerIndex).ToTexture((normal.Width, normal.Height));
        
        var diffuseTexture = texMode switch
        {
            TextureMode.Compatibility => material.GetTexture(TextureUsage.g_SamplerDiffuse).ToTexture((normal.Width, normal.Height)),
            _ => null
        };
        var specTexture = specMode switch
        {
            SpecularMode.Mask => new SKTexture(normal.Width, normal.Height),
            SpecularMode.Default => material.GetTexture(TextureUsage.g_SamplerSpecular).ToTexture((normal.Width, normal.Height)),
            _ => null
        };
        var flowTexture = flowType switch
        {
            FlowType.Flow => material.GetTexture(TextureUsage.g_SamplerFlow).ToTexture((normal.Width, normal.Height)),
            _ => null
        };
        
        var outNormal = new SKTexture(normal.Width, normal.Height);
        var outDiffuse = new SKTexture(normal.Width, normal.Height);
        var outSpecular = new SKTexture(normal.Width, normal.Height);
        var outOcclusion = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal[x, y].ToVector4();
            var maskPixel = maskTexture[x, y].ToVector4();
            var indexPixel = indexTexture[x, y];

            var blended = material.ColorTable.GetBlendedPair(indexPixel.Red, indexPixel.Green);
            if (texMode == TextureMode.Compatibility)
            {
                var diffusePixel = diffuseTexture![x, y].ToVector4();
                diffusePixel *= new Vector4(blended.Diffuse, normalPixel.Z);
                outDiffuse[x, y] = (diffusePixel with {W = normalPixel.Z}).ToSkColor();
            }
            else if (texMode == TextureMode.Default)
            {
                var diffusePixel = new Vector4(blended.Diffuse, normalPixel.Z);
                outDiffuse[x, y] = diffusePixel.ToSkColor();
            }
            else
            {
                throw new NotImplementedException();
            }

            var specular = new Vector4(1.0f);
            var roughness = 0.0f;
            var occlusion = 1.0f;
            
            if (specMode == SpecularMode.Mask)
            {
                var diffuseMask = maskPixel.X;
                specular *= maskPixel.Y;
                roughness = maskPixel.Z;
                outOcclusion[x, y] = new Vector4(diffuseMask).ToSkColor();
                outSpecular[x, y] = specular.ToSkColor();
            }
            else if (specMode == SpecularMode.Default)
            {
                var specPixel = specTexture![x, y].ToVector4();
                outSpecular[x, y] = specPixel.ToSkColor();
            }
            else
            {
                outSpecular[x, y] = new Vector4(0.1f, 0.1f, 0.1f, 1.0f).ToSkColor();
            }
            
            outNormal[x, y] = (normalPixel with {W = 1.0f}).ToSkColor();
        }

        var output = new MaterialBuilder(name);
        output.WithBaseColor(BuildImage(outDiffuse, name, "diffuse"));
        output.WithNormal(BuildImage(outNormal, name, "normal"));
        
        if (specMode == SpecularMode.Mask)
        {
            output.WithMetallicRoughnessShader();
            output.WithMetallicRoughness(BuildImage(outSpecular, name, "specular"), 1);
            output.WithOcclusion(BuildImage(outOcclusion, name, "occlusion"));
        }
        else
        {
            output.WithSpecularFactor(BuildImage(outSpecular, name, "specular"), 1);
        }
        
        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);
        else
            output.WithAlpha();
        
        output.WithDoubleSide((material.ShaderFlags & 0x1) == 0);
        
        return output;
    }
    
    public static MaterialBuilder BuildCharacterOcclusion(Material material, string name)
    {
        // this is purely guesswork
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        
        // fully transparent
        var baseTexture = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < baseTexture.Width; x++)
        for (var y = 0; y < baseTexture.Height; y++)
        {
            // black, use transparency from normal
            var normalPixel = normal[x, y].ToVector4();
        }

        var output = new MaterialBuilder(name);
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
        output.WithBaseColor(new Vector4(1, 1, 1, 0f));
        output.WithAlpha(AlphaMode.BLEND, 0.5f);
        
        return output;
    }
    
    public static MaterialBuilder BuildCharacterTattoo(
        Material material, string name, CustomizeParameter parameters, CustomizeData data)
    {
        // face or hair
        const uint categoryHairType = 0x24826489;
        HairType? hairType = null;
        if (material.ShaderKeys.Any(x => x.Category == categoryHairType))
        {
            var key = material.ShaderKeys.First(x => x.Category == categoryHairType);
            hairType = (HairType)key.Value;
        }
        
        // face = tattoo color
        // hair = highlight color
        Vector3 color = hairType switch
        {
            HairType.Face => parameters.OptionColor,
            HairType.Hair => parameters.MeshColor,
            _ => Vector3.Zero
        };
        
        var normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        var baseTexture = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < baseTexture.Width; x++)
        for (var y = 0; y < baseTexture.Height; y++)
        {
            var normalSample = normal[x, y].ToVector4();
            
            // apply color to normal
            if (normalSample.Z != 0)
            {
                baseTexture[x, y] = new Vector4(color, normalSample.W).ToSkColor();
            }
            else
            {
                baseTexture[x, y] = new Vector4(0, 0, 0, normalSample.W).ToSkColor();
            }
        }
        
        var output = new MaterialBuilder(name)
                     .WithBaseColor(BuildImage(baseTexture, name, "diffuse"))
                     .WithNormal(BuildImage(normal, name, "normal"));
        
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
        
        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.BLEND, alphaThreshold);
        else
            output.WithAlpha(AlphaMode.BLEND);
        
        return output;
    }
    
    public static MaterialBuilder BuildCharacterLegacy(Material material, string name)
    {
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        SKTexture? diffuse = null;
        bool hasDiffuse = false;
        
        if (material.TryGetTexture(TextureUsage.g_SamplerDiffuse, out var diffuseTexture))
        {
            hasDiffuse = true;
            diffuse = diffuseTexture.ToTexture((normal.Width, normal.Height));
        }

        var baseNormal = normal;
        SKTexture baseTexture = diffuse ?? new SKTexture(normal.Width, normal.Height);
        SKTexture? baseGloss = null;
        SKTexture? baseSpecular = null;
        if (material.TryGetTexture(TextureUsage.g_SamplerIndex, out var indexTexture))
        {
            var index = indexTexture.ToTexture((normal.Width, normal.Height));
            baseSpecular = new SKTexture(normal.Width, normal.Height);
            baseGloss = new SKTexture(normal.Width, normal.Height);
            var table = material.ColorTable;

            for (var x = 0; x < index.Width; x++)
            for (var y = 0; y < index.Height; y++)
            {
                var indexPixel = index[x, y];
                var blended = table.GetBlendedPair(indexPixel.Red, indexPixel.Green);
                var normalPixel = baseNormal[x, y].ToVector4();
                
                baseGloss[x, y] = new Vector4(blended.Emissive, blended.GlossStrength).ToSkColor();
                baseSpecular[x, y] = new Vector4(blended.Specular, blended.SpecularStrength).ToSkColor();
                
                // apply to diffuse
                if (hasDiffuse)
                {
                    var diffusePixel = baseTexture[x, y].ToVector4();
                    // multiply diffuse by pairDiffuse
                    diffusePixel *= new Vector4(blended.Diffuse, normalPixel.Z);
                    baseTexture[x, y] = (diffusePixel with {W = normalPixel.Z}).ToSkColor();
                }
                else
                {
                    baseTexture[x, y] = new Vector4(blended.Diffuse, normalPixel.Z).ToSkColor();
                }
            }
        }
        
        
        if (material.TryGetTexture(TextureUsage.g_SamplerMask, out var maskTexture))
        {
            var mask = maskTexture.ToTexture((normal.Width, normal.Height));
            // red -> diffuse mask
            // green -> specular mask
            // blue -> gloss mask
            
            for (var x = 0; x < mask.Width; x++)
            for (var y = 0; y < mask.Height; y++)
            {
                var maskPixel = mask[x, y].ToVector4();
                var normalPixel = baseNormal[x, y].ToVector4();
                var diffusePixel = baseTexture[x, y].ToVector4();
                
                // apply diffuse mask
                diffusePixel *= new Vector4(maskPixel.X);
                baseTexture[x, y] = (diffusePixel with {W = normalPixel.Z}).ToSkColor();
                
                // apply specular mask
                if (baseSpecular != null)
                {
                    var specPixel = baseSpecular[x, y].ToVector4();
                    var specAlpha = specPixel.W;
                    specPixel *= new Vector4(maskPixel.Y);
                    baseSpecular[x, y] = (specPixel with {W = specAlpha}).ToSkColor();
                }

                // apply gloss mask
                if (baseGloss != null)
                {
                    var glossPixel = baseGloss[x, y].ToVector4();
                    var glossAlpha = glossPixel.W;
                    glossPixel *= new Vector4(maskPixel.Z);
                    baseGloss[x, y] = (glossPixel with {W = glossAlpha}).ToSkColor();
                }
            }
        }
        
        var output = new MaterialBuilder(name)
                     .WithDoubleSide(true);
        
        output.WithBaseColor(BuildImage(baseTexture, name, "diffuse"));
        output.WithNormal(BuildImage(normal, name, "normal"));
        
        if (baseGloss != null) output.WithSpecularColor(BuildImage(baseGloss, name, "gloss"));
        if (baseSpecular != null) output.WithSpecularFactor(BuildImage(baseSpecular, name, "specular"), 1);
        
        return output;
    }
}
