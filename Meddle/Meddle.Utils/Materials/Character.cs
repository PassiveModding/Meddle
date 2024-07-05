using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Shader;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildCharacter(Material material, string name)
    {        
        var output = new MaterialBuilder(name)
                     .WithDoubleSide(true)
                     .WithBaseColor(Vector4.One);
        
        // r -> X+ tangent space
        // g -> Y+ tangent space
        // b -> opacity
        // a -> Z+ tangent space?
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        
        // rgb -> color data
        SKTexture diffuse;
        bool hasDiffuse = false;
        if (material.TryGetTexture(TextureUsage.g_SamplerDiffuse, out var diffuseTexture))
        {
            diffuse = diffuseTexture.ToTexture();
            hasDiffuse = true;
        }
        else
        {
            // will be populated from colorTable anyways
            diffuse = new SKTexture(normal.Width, normal.Height);
        }
        
        SKTexture? orm = null;
        SKTexture? mask = null;
        SKTexture? specular = null;

        if (material.TryGetTexture(TextureUsage.g_SamplerMask, out var maskTexture))
        {
            // TODO: This is temp until I do the material params to get it
            if (maskTexture.HandlePath.EndsWith("_orm.tex"))
            {
                orm = maskTexture.ToTexture();
            }
            else if (maskTexture.HandlePath.EndsWith("_mask.tex"))
            {
                mask = maskTexture.ToTexture();
            }
            else if (maskTexture.HandlePath.EndsWith("_s.tex"))
            {
                mask = maskTexture.ToTexture();
            }
            else
            {
                throw new NotImplementedException($"Unknown mask texture, {maskTexture.HandlePath}");
            }
        }
        

        if (material.TryGetTexture(TextureUsage.g_SamplerSpecular, out var specularTexture))
        {
            throw new NotImplementedException("Why is this here?");
        }

        if (material.TryGetTexture(TextureUsage.g_SamplerFlow, out var flowTexture))
        {
            // used for fur, optional
        }

        var baseTexture = diffuse;
        var baseNormal = normal.Resize(diffuse.Width, diffuse.Height);
        SKTexture? baseGloss = null;
        SKTexture? baseSpecular = null;
        if (material.TryGetTexture(TextureUsage.g_SamplerIndex, out var indexTexture))
        {
            // used to apply colortable
            // r -> selects the pair, nearest pair will be selected
            // g -> blending,
            //  ex. red is 0, so pair 1-2 will be selected
            //      if green is 0, then row 2 will be used
            //      if green is 255, row 1 will be used
            //      intermediate value will create a blend between row 1 and 2
            var index = indexTexture.ToTexture((diffuse.Width, diffuse.Height));
            var table = material.ColorTable;
            baseGloss = new SKTexture(diffuse.Width, diffuse.Height);
            baseSpecular = new SKTexture(diffuse.Width, diffuse.Height);
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
        
        // apply normal opacity to diffuse
        for (var x = 0; x < baseTexture.Width; x++)
        for (var y = 0; y < baseTexture.Height; y++)
        {
            var normalPixel = baseNormal[x, y].ToVector4();
            var diffusePixel = baseTexture[x, y].ToVector4();
            baseTexture[x, y] = (diffusePixel with {W = normalPixel.Z}).ToSkColor();
        }

        output.WithBaseColor(BuildImage(baseTexture, name, "diffuse"));
        if (baseGloss != null)
        {
            output.WithEmissive(BuildImage(baseGloss, name, "emissive"));
        }
        
        if (baseSpecular != null)
        {
            output.WithSpecularFactor(BuildImage(baseSpecular, name, "specular"), 1);
        }

        output.WithNormal(BuildImage(baseNormal, name, "normal"));
        if (orm != null)
        {
            // r -> metallicity
            // g -> roughness
            // b -> ambient occlusion
            output.WithMetallicRoughness(BuildImage(orm, name, "orm"));
            output.WithMetallicRoughnessShader();
        }

        if (mask != null)
        {
            // r -> cavity
            // g -> 
            /*
             * A nightmare texture. This channel is essentially used to configure what material it is.
             * The channel is bitpacked, or segmented, into fourths.
             * The bit ranges are thus: 0 - 63 is blank and does nothing; 64 - 127 is metallicity; 128 - 191 is leather; 192 - 255 is cloth.
             * Per Ny, the colorset has a row in the fifth column (column 4 in devspeak) that uses the Photoshop blend mode "Overlay" to pull a value closer to 1, or 255, and closer to 0.
             * When doing this, it can essentially be made closer to one of these four material types. It looks lots worse than it really is.
             */
            // b -> ambient occulsion
            
            // TODO: yawn
            output.WithSpecularFactor(BuildImage(mask, name, "mask"), 1);
        }
        
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
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
