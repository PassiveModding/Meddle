using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildSkin(Material material, string name, CustomizeParameter parameters, CustomizeData data)
    {
        SkinType skinType = SkinType.Default;
        if (material.ShaderKeys.Any(x => x.Category == (uint)ShaderCategory.CategorySkinType))
        {
            var key = material.ShaderKeys.First(x => x.Category == (uint)ShaderCategory.CategorySkinType);
            skinType = (SkinType)key.Value;
        }
        
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        SKTexture mask = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture((normal.Width, normal.Height)); // spec, roughness, thickness
        SKTexture diffuse = material.GetTexture(TextureUsage.g_SamplerDiffuse).ToTexture((normal.Width, normal.Height));
        
        var diffuseMultiplier = material.GetConstantOrDefault(MaterialConstant.g_DiffuseColor, Vector3.One);
        
        // PART_BODY = no additional color
        // PART_FACE/default = lip color
        // PART_HRO = hairColor blend into hair highlight color
        
        var skinColor = parameters.SkinColor;
        var lipColor = parameters.LipColor;
        var hairColor = parameters.MainColor;
        var highlightColor = parameters.MeshColor;
        
        
        var diffuseTexture = new SKTexture(diffuse.Width, diffuse.Height);
        var normalTexture = new SKTexture(normal.Width, normal.Height);
        //var specularTexture = new SKTexture(normal.Width, normal.Height);
        for (int x = 0; x < normal.Width; x++)
        for (int y = 0; y < normal.Height; y++)
        {
            var normalPixel = normal[x, y].ToVector4();
            var maskPixel = mask[x, y].ToVector4();
            var diffusePixel = diffuse[x, y].ToVector4();

            var skinInfluence = normalPixel.Z;
            diffusePixel = Vector4.Lerp(diffusePixel, skinColor, skinInfluence);
            
            var specMask = maskPixel.X;
            //var specular = new Vector4(specMask, specMask, specMask, 1.0f);

            var secondaryInfluence = normalPixel.W;
            if (skinType == SkinType.Default || skinType == SkinType.Face)
            {
                if (data.LipStick)
                {
                    diffusePixel = Vector4.Lerp(diffusePixel, lipColor, secondaryInfluence * lipColor.W);
                    diffusePixel.W = 1.0f;
                }
            }
            else if (skinType == SkinType.Hrothgar)
            {
                var hair = hairColor;
                if (data.Highlights)
                {
                    hair = Vector3.Lerp(hairColor, highlightColor, maskPixel.W);
                }
                
                var hCol = new Vector4(hair, 1.0f);
                diffusePixel = Vector4.Lerp(diffusePixel, hCol, secondaryInfluence);
            }
            
            diffuseTexture[x, y] = diffusePixel.ToSkColor();
            normalTexture[x, y] = (normalPixel with { W = 1.0f }).ToSkColor();
           // specularTexture[x, y] = specular.ToSkColor();
        }

        var output = new MaterialBuilder(name);
        output.WithBaseColor(BuildImage(diffuseTexture, name, "diffuse"));
        output.WithNormal(BuildImage(normalTexture, name, "normal"));
        
        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);
        
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
        
        return output;
    }
}
