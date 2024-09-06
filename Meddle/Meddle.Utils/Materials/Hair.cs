using System.Numerics;
using Meddle.Utils.Export;
using SharpGLTF.Materials;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildHair(
        Material material, string name, CustomizeParameter parameters, CustomizeData data)
    {
        HairType? hairType;
        if (material.ShaderKeys.Any(x => x.Category == (uint)ShaderCategory.CategoryHairType))
        {
            var key = material.ShaderKeys.First(x => x.Category == (uint)ShaderCategory.CategoryHairType);
            hairType = (HairType)key.Value;
        }
        else
        {
            hairType = HairType.Hair;
        }

        var normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        var mask = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture((normal.Width, normal.Height)); 

        var diffuseMultiplier = material.GetConstantOrDefault(MaterialConstant.g_DiffuseColor, Vector3.One);
        
        var outNormal = new SKTexture(normal.Width, normal.Height);
        var outDiffuse = new SKTexture(normal.Width, normal.Height);
        var outOcclusion = new SKTexture(normal.Width, normal.Height);
        var outSpecular = new SKTexture(normal.Width, normal.Height);

        var hairColor = parameters.MainColor;
        var tattooColor = parameters.OptionColor;
        var highlightColor = parameters.MeshColor;
        
        Parallel.For(0, outDiffuse.Width, x =>
        {
            for (var y = 0; y < outDiffuse.Height; y++)
            {
                var normalPixel = normal[x, y].ToVector4();
                var maskPixel = mask[x, y].ToVector4();

                var bonusColor = hairType switch
                {
                    HairType.Face => tattooColor,
                    HairType.Hair => highlightColor,
                    _ => hairColor
                };
                
                var bonusIntensity = normalPixel.Z;
                var diffusePixel = Vector3.Lerp(hairColor, bonusColor, bonusIntensity);
                diffusePixel *= diffuseMultiplier;

                var occlusion = maskPixel.W * maskPixel.W;
                var roughness = maskPixel.Y;
                var specular = new Vector4(maskPixel.X, maskPixel.X, maskPixel.X, 1.0f);

                outDiffuse[x, y] = new Vector4(diffusePixel, normalPixel.W).ToSkColor();
                outNormal[x, y] = (normalPixel with {Z = 1.0f, W = 1.0f}).ToSkColor();
                outOcclusion[x, y] = new Vector4(occlusion, roughness, 0.0f, 1.0f).ToSkColor();
            }
        });

        var output = new MaterialBuilder(name)
            .WithDoubleSide(material.RenderBackfaces);
        
        output.WithBaseColor(BuildImage(outDiffuse, name, "diffuse"));
        output.WithNormal(BuildImage(outNormal, name, "normal"));

        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);

        return output;
    }
}
