using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    private enum SkinType
    {
        Face,
        Hrothgar,
        Other
    }
    
    public static MaterialBuilder BuildSkin(Material material, string name, MaterialParameters parameters)
    {
        const uint categorySkinType = 0x380CAED0;
        const uint valueFace = 0xF5673524;
        const uint valueHrothgar = 0x57FF3B64;

        SkinType skinType = SkinType.Face;
        foreach (var shaderKey in material.ShaderKeys)
        {
            if (shaderKey.Category == categorySkinType)
            {
                skinType = shaderKey.Value switch
                {
                    valueFace => SkinType.Face,
                    valueHrothgar => SkinType.Hrothgar,
                    _ => SkinType.Other
                };
            }
        }
        
        SKTexture mask = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture(); // spec, roughness, thickness
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        SKTexture diffuse = material.GetTexture(TextureUsage.g_SamplerDiffuse).ToTexture();
        
        
        var output = new MaterialBuilder(name)
                     .WithDoubleSide(true);

        var outDiffuse = new SKTexture(diffuse.Width, diffuse.Height);
        var outSpecRough = new SKTexture(diffuse.Width, diffuse.Height);
        var outNormal = new SKTexture(diffuse.Width, diffuse.Height);
        
        var refMask = mask.Resize(diffuse.Width, diffuse.Height);
        var refNormal = normal.Resize(diffuse.Width, diffuse.Height);
        for (var x = 0; x < outDiffuse.Width; x++)
        for (var y = 0; y < outDiffuse.Height; y++)
        {
            var normalPixel = refNormal[x, y].ToVector4();
            var diffusePixel = diffuse[x, y].ToVector4();
            var maskPixel = refMask[x, y].ToVector4();
            
            var skinColorInfluence = normalPixel.Z;
            if (skinColorInfluence != 0)
            {
                var skin = new Vector4(parameters.SkinColor, diffusePixel.W);
                diffusePixel = Vector4.Lerp(diffusePixel, skin, skinColorInfluence);
            }
            
            if (skinType == SkinType.Face)
            {
                var lipColorInfluence = normalPixel.W;
                var lip = new Vector4(parameters.LipColor, diffusePixel.W);
                diffusePixel = Vector4.Lerp(diffusePixel, lip, lipColorInfluence);
            }
            else if (skinType == SkinType.Hrothgar)
            {
                var furColorInfluence = normalPixel.W;
                if (skinColorInfluence == 0)
                {
                    var fur = new Vector4(parameters.HairColor, diffusePixel.W);
                    diffusePixel = Vector4.Lerp(diffusePixel, fur, furColorInfluence);
                }

                if (parameters.HighlightColor != null)
                {
                    var furPatternColorInfluence = maskPixel.W;
                    var furPattern = new Vector4(parameters.HighlightColor.Value, diffusePixel.W);
                    diffusePixel = Vector4.Lerp(diffusePixel, furPattern, furPatternColorInfluence);
                }
            }
            else
            {
                throw new NotImplementedException($"Unknown skin type {skinType} (0x{skinType:X})");
            }

            /*if (parameters is HrothgarSkinParameters hrothgarSkinParameters)
            {
                var furColorInfluence = normalPixel.W;

                if (skinColorInfluence == 0)
                {
                    var fur = new Vector4(hrothgarSkinParameters.FurColor, diffusePixel.W);
                    diffusePixel = Vector4.Lerp(diffusePixel, fur, furColorInfluence);
                }
                else
                {
                    var skin = new Vector4(hrothgarSkinParameters.SkinColor, diffusePixel.W);
                    diffusePixel = Vector4.Lerp(diffusePixel, skin, skinColorInfluence);
                }
            }*/

            outDiffuse[x, y] = diffusePixel.ToSkColor();
            outSpecRough[x, y] = refMask[x, y];
            outNormal[x, y] = normalPixel.ToSkColor();
        }
        
        output.WithBaseColor(BuildImage(outDiffuse, name, "diffuse"));
        output.WithMetallicRoughness(BuildImage(outSpecRough, name, "mask"));
        output.WithMetallicRoughnessShader();
        output.WithNormal(BuildImage(outNormal, name, "normal"));
        
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
        
        return output;
    }
}
