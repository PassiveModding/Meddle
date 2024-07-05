using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Shader;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SkiaSharp;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    private enum SkinType : uint
    {
        Body = 0x2BDB45F1,
        Face = 0xF5673524,
        Hrothgar = 0x57FF3B64,
        Default = 0
    }
    
    public static MaterialBuilder BuildSkin(Material material, string name, CustomizeParameter parameters, CustomizeData data)
    {
        const uint categorySkinType = 0x380CAED0;

        SkinType skinType = SkinType.Default;
        if (material.ShaderKeys.Any(x => x.Category == categorySkinType))
        {
            var key = material.ShaderKeys.First(x => x.Category == categorySkinType);
            skinType = (SkinType)key.Value;
        }

        var alphaMultiplier = 1.0f;
        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0.0f)
        {
            alphaMultiplier = 1.0f / alphaThreshold;
        }
        var diffuseMultiplier = material.GetConstantOrDefault(MaterialConstant.g_DiffuseColor, Vector3.One);
        var specularMultiplier = material.GetConstantOrDefault(MaterialConstant.g_SpecularColorMask, Vector3.One);
        var emissiveColorMul = material.GetConstantOrDefault(MaterialConstant.g_EmissiveColor, Vector3.Zero);

        diffuseMultiplier *= diffuseMultiplier;
        specularMultiplier *= specularMultiplier;
        emissiveColorMul *= emissiveColorMul;
        
        
        
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        SKTexture mask = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture((normal.Width, normal.Height)); // spec, roughness, thickness
        SKTexture diffuse = material.GetTexture(TextureUsage.g_SamplerDiffuse).ToTexture((normal.Width, normal.Height));
        
        // second color
        // PART_BODY = no additional color
        // PART_FACE = lip color
        // PART_HRO = hairColor
        // default = lip color
        
        // third color
        // PART_HRO = hair highlight color
        
        var skinColor = parameters.SkinColor;
        var lipColor = parameters.LipColor;
        var hairColor = parameters.MainColor;
        var highlightColor = parameters.MeshColor;
        
        
        var diffuseTexture = new SKTexture(diffuse.Width, diffuse.Height);
        var normalTexture = new SKTexture(normal.Width, normal.Height);
        var specularTexture = new SKTexture(normal.Width, normal.Height);
        for (int x = 0; x < normal.Width; x++)
        for (int y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal[x, y].ToVector4();
            var maskPixel = mask[x, y].ToVector4();
            var diffusePixel = diffuse[x, y].ToVector4();

            var skinInfluence = normalPixel.Z;
            var sColor = Vector4.Lerp(new Vector4(1.0f), skinColor, skinInfluence);
            diffusePixel *= sColor;
            
            var specMask = maskPixel.X;
            var specular = new Vector4(specMask, specMask, specMask, 1.0f);

            var secondaryInfluence = normalPixel.W;
            if (skinType == SkinType.Default || skinType == SkinType.Face)
            {
                if (data.Lipstick)
                {
                    diffusePixel = Vector4.Lerp(diffusePixel, lipColor, secondaryInfluence * lipColor.W);
                    diffusePixel.W = 1.0f;
                }
            }
            else if (skinType == SkinType.Hrothgar)
            {
                if (data.Highlights)
                {
                    hairColor = Vector3.Lerp(hairColor, highlightColor, maskPixel.W);
                }
                
                var hCol = new Vector4(hairColor, 1.0f);
                
                diffusePixel = Vector4.Lerp(diffusePixel, hCol, secondaryInfluence);
            }
            
            //diffusePixel *= new Vector4(diffuseMultiplier, 1.0f);
            
            diffuseTexture[x, y] = diffusePixel.ToSkColor();
            normalTexture[x, y] = (normalPixel with { W = 1.0f }).ToSkColor();
            specularTexture[x, y] = specular.ToSkColor();
        }

        var output = new MaterialBuilder(name);
        output.WithBaseColor(BuildImage(diffuseTexture, name, "diffuse"));
        //output.WithMetallicRoughness(BuildImage(specularTexture, name, "mask"));
        output.WithNormal(BuildImage(normalTexture, name, "normal"));
        output.WithAlpha(AlphaMode.OPAQUE, alphaThreshold);
        
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
        
        return output;
    }
}
