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
        var metallicRoughness = new SKTexture(normal.Width, normal.Height);
        //for (int x = 0; x < normal.Width; x++)
        Parallel.For(0, normal.Width, x =>
        {
            for (int y = 0; y < normal.Height; y++)
            {
                var normalPixel = normal[x, y].ToVector4();
                var maskPixel = mask[x, y].ToVector4();
                var diffusePixel = diffuse[x, y].ToVector4();

                var skinInfluence = normalPixel.Z;
                var diffuseAlpha = diffusePixel.W;
                diffusePixel = Vector4.Lerp(diffusePixel, skinColor, skinInfluence);

                var specular = maskPixel.X;
                var roughness = maskPixel.Y;
                var subsurface = maskPixel.Z;
                var metallic = 0.0f;
                var roughnessPixel = new Vector4(subsurface, roughness, metallic, specular);

                if (skinType == SkinType.Face)
                {
                    if (data.LipStick)
                    {
                        diffusePixel = Vector4.Lerp(diffusePixel, lipColor, normalPixel.W * lipColor.W);
                    }
                }
                // only apply hair for hrothgar if skin is not applied
                else if (skinType == SkinType.Hrothgar && skinInfluence == 0f)
                {
                    var hair = hairColor;
                    if (data.Highlights)
                    {
                        hair = Vector3.Lerp(hairColor, highlightColor, maskPixel.W);
                    }

                    diffusePixel = Vector4.Lerp(diffusePixel, new Vector4(hair, 1.0f), normalPixel.W);
                }

                diffuseTexture[x, y] = (diffusePixel with {W = diffuseAlpha}).ToSkColor();
                normalTexture[x, y] = (normalPixel with {W = 1.0f}).ToSkColor();
                metallicRoughness[x, y] = roughnessPixel.ToSkColor();
            }
        });

        var output = new MaterialBuilder(name);
        output.WithBaseColor(BuildImage(diffuseTexture, name, "diffuse"));
        
        // just seems right at 0.75f, at least for au-ra scales?
        output.WithNormal(BuildImage(normalTexture, name, "normal"), 0.75f);
        
        var mr = BuildImage(metallicRoughness, name, "sss_roughness_metallic_specular");
        output.WithMetallicRoughness(mr);
        output.WithSpecularFactor(mr, 1.0f);
        output.WithMetallicRoughnessShader();
        
        
        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);
        
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);
        
        return output;
    }
}
