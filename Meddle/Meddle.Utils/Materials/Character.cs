using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildCharacter(Material material, string name)
    {
        TextureMode texMode;
        if (material.ShaderKeys.Any(x => x.Category == (uint)ShaderCategory.CategoryTextureType))
        {
            var key = material.ShaderKeys.First(x => x.Category == (uint)ShaderCategory.CategoryTextureType);
            texMode = (TextureMode)key.Value;
        }
        else
        {
            texMode = TextureMode.Default;
        }
        
        SpecularMode specMode;
        if (material.ShaderKeys.Any(x => x.Category == (uint)ShaderCategory.CategorySpecularType))
        {
            var key = material.ShaderKeys.First(x => x.Category == (uint)ShaderCategory.CategorySpecularType);
            specMode = (SpecularMode)key.Value;
        }
        else
        {
            specMode = 0x0;
        }
        
        FlowType flowType;
        if (material.ShaderKeys.Any(x => x.Category == (uint)ShaderCategory.CategoryFlowMapType))
        {
            var key = material.ShaderKeys.First(x => x.Category == (uint)ShaderCategory.CategoryFlowMapType);
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
        //for (var x = 0; x < normal.Width; x++)
        Parallel.For(0, normal.Width, x =>
        {
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

                var spec = blended.Specular;
                var specStrength = blended.SpecularStrength;

                if (specMode == SpecularMode.Mask)
                {
                    var diffuseMask = maskPixel.X;
                    var maspSpec = maskPixel.Y;
                    var maskRoughness = maskPixel.Z;
                    outOcclusion[x, y] = new Vector4(diffuseMask).ToSkColor();
                    outSpecular[x, y] = new Vector4(spec, specStrength).ToSkColor();
                }
                else if (specMode == SpecularMode.Default)
                {
                    var specPixel = specTexture![x, y].ToVector4();
                    outSpecular[x, y] = specPixel.ToSkColor();
                }
                else
                {
                    outSpecular[x, y] = new Vector4(spec, specStrength).ToSkColor();
                }

                outNormal[x, y] = (normalPixel with {W = 1.0f}).ToSkColor();
            }
        });

        var output = new MaterialBuilder(name);
        output.WithBaseColor(BuildImage(outDiffuse, name, "diffuse"));
        output.WithNormal(BuildImage(outNormal, name, "normal"));
        
        if (specMode == SpecularMode.Mask)
        {
            output.WithMetallicRoughnessShader();
            output.WithMetallicRoughness(BuildImage(outSpecular, name, "specular"));
            output.WithOcclusion(BuildImage(outOcclusion, name, "occlusion"));
        }
        else
        {
            var spec = BuildImage(outSpecular, name, "specular");
            output.WithSpecularFactor(spec, 1);
            output.WithSpecularColor(spec);
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
        return BuildCharacter(material, name);
        TextureMode texMode;
        if (material.ShaderKeys.Any(x => x.Category == (uint)ShaderCategory.CategoryTextureType))
        {
            var key = material.ShaderKeys.First(x => x.Category == (uint)ShaderCategory.CategoryTextureType);
            texMode = (TextureMode)key.Value;
        }
        else
        {
            texMode = TextureMode.Default;
        }
        
        SpecularMode specMode;
        if (material.ShaderKeys.Any(x => x.Category == (uint)ShaderCategory.CategorySpecularType))
        {
            var key = material.ShaderKeys.First(x => x.Category == (uint)ShaderCategory.CategorySpecularType);
            specMode = (SpecularMode)key.Value;
        }
        else
        {
            specMode = 0x0;
        }
        
        var normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        var indexTexture = material.GetTexture(TextureUsage.g_SamplerIndex).ToTexture((normal.Width, normal.Height));
        
        var diffuseTexture = texMode switch
        {
            TextureMode.Compatibility => material.GetTexture(TextureUsage.g_SamplerDiffuse).ToTexture((normal.Width, normal.Height)),
            _ => null
        };
        
        var specularTexture = specMode switch
        {
            SpecularMode.Default => material.GetTexture(TextureUsage.g_SamplerSpecular).ToTexture((normal.Width, normal.Height)),
            _ => null
        };
        
        var outNormal = new SKTexture(normal.Width, normal.Height);
        var outDiffuse = new SKTexture(normal.Width, normal.Height);
        var outSpecular = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < normal.Width; x++)
        for (var y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal[x, y].ToVector4();
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

            if (specMode == SpecularMode.Default)
            {
                var specPixel = specularTexture![x, y].ToVector4();
                outSpecular[x, y] = specPixel.ToSkColor();
            }
            else if (specMode == SpecularMode.Mask)
            {
                outSpecular[x, y] = new Vector4(0.1f, 0.1f, 0.1f, 0.1f).ToSkColor();
            }
            else
            {
                outSpecular[x, y] = new Vector4(0.1f, 0.1f, 0.1f, 0.1f).ToSkColor();
            }
            
            outNormal[x, y] = (normalPixel with {W = 1.0f}).ToSkColor();
        }

        var output = new MaterialBuilder(name);
        output.WithBaseColor(BuildImage(outDiffuse, name, "diffuse"));
        output.WithNormal(BuildImage(outNormal, name, "normal"));
        var spec = BuildImage(outSpecular, name, "specular");
        output.WithSpecularFactor(spec, 1);
        output.WithSpecularColor(spec);
        
        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);
        else
            output.WithAlpha();
        
        output.WithDoubleSide((material.ShaderFlags & 0x1) == 0);
        
        return output;
    }
}
