using System.Numerics;
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
            else
            {
                throw new NotImplementedException("Unknown mask texture");
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
        SKTexture? baseEmissive = null;
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
            baseEmissive = new SKTexture(diffuse.Width, diffuse.Height);
            var weightArr = new byte[] { 
                0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 
                0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF 
            };
            for (var x = 0; x < index.Width; x++)
            for (var y = 0; y < index.Height; y++)
            {
                var indexPixel = index[x, y];

                var nearestPair = weightArr.MinBy(v => Math.Abs(v - indexPixel.Red));
                var pairIdx = Array.IndexOf(weightArr, nearestPair) * 2;
                
                var pair0 = table.GetRow(pairIdx);
                var pair1 = table.GetRow(pairIdx + 1);
                
                // 0xFF = pair0
                // 0x00 = pair1
                var blend = indexPixel.Green / 255f;
                var pairDiffuse = Vector3.Lerp(pair1.Diffuse, pair0.Diffuse, blend);
                var normalPixel = baseNormal[x, y].ToVector4();
                
                var pairEmis = Vector3.Lerp(pair1.Emissive, pair0.Emissive, blend);
                baseEmissive[x, y] = pairEmis.ToSkColor();
                
                // apply to diffuse
                if (hasDiffuse)
                {
                    var diffusePixel = baseTexture[x, y].ToVector4();
                    // multiply diffuse by pairDiffuse
                    diffusePixel *= new Vector4(pairDiffuse, normalPixel.Z);
                    baseTexture[x, y] = (diffusePixel with {W = normalPixel.Z}).ToSkColor();
                }
                else
                {
                    baseTexture[x, y] = new Vector4(pairDiffuse, normalPixel.Z).ToSkColor();
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
        if (baseEmissive != null)
        {
            output.WithEmissive(BuildImage(baseEmissive, name, "emissive"));
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
            baseTexture[x, y] = new Vector4(0, 0, 0, normalPixel.Z).ToSkColor();
        }
        
        var output = new MaterialBuilder(name)
                     .WithDoubleSide(true)
                     .WithBaseColor(BuildImage(baseTexture, name, "diffuse"));
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
        
        return output;
    }
    
    public static MaterialBuilder BuildCharacterTattoo(
        Material material, string name, MaterialParameters parameters)
    {
        var normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        var baseTexture = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < baseTexture.Width; x++)
        for (var y = 0; y < baseTexture.Height; y++)
        {
            var normalSample = normal[x, y].ToVector4();
            var meshColor = new Vector4(parameters.SkinColor, normalSample.W);
            var decalColor = parameters.DecalColor ?? new Vector4(1,1,1, normalSample.W);
            
            var finalColor = meshColor * decalColor;
            baseTexture[x, y] = (finalColor with {W = normalSample.W }).ToSkColor();
        }
        
        var output = new MaterialBuilder(name)
                     .WithBaseColor(BuildImage(baseTexture, name, "diffuse"))
                     .WithNormal(BuildImage(normal, name, "normal"));
        
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
        
        return output;
    }
    
    public static MaterialBuilder BuildCharacterLegacy(Material material, string name)
    {
        SKTexture? normal = null;
        SKTexture? mask = null;
        SKTexture? index = null;
        SKTexture? diffuse = null;
        
        if (material.TryGetTexture(TextureUsage.g_SamplerNormal, out var normalTexture))
        {
            normal = normalTexture.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerMask, out var maskTexture))
        {
            mask = maskTexture.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerIndex, out var indexTexture))
        {
            index = indexTexture.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerDiffuse, out var diffuseTexture))
        {
            diffuse = diffuseTexture.ToTexture();
        }
        
        var output = new MaterialBuilder(name)
                     .WithDoubleSide(true)
                     .WithMetallicRoughnessShader()
                     .WithBaseColor(Vector4.One);
        
        if (diffuse != null) output.WithBaseColor(BuildImage(diffuse, name, "diffuse"));
        if (normal != null) output.WithNormal(BuildImage(normal, name, "normal"));
        if (mask != null) output.WithSpecularFactor(BuildImage(mask, name, "mask"), 1);
        if (index != null) output.WithSpecularColor(BuildImage(index, name, "index"));
        
        return output;
    }
}
