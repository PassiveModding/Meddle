using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildHair(
        Material material, string name, CustomizeParameter parameters, CustomizeData data)
    {
        HairType? hairType = null;
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
        var mask = material.GetTexture(TextureUsage.g_SamplerMask)
                           .ToTexture((normal.Width, normal.Height)); // spec, roughness, sss thickness, ao

        var outDiffuse = new SKTexture(normal.Width, normal.Height);
        var roughMet =
            new SKTexture(normal.Width, normal.Height); // R = SSS, G = roughness, B = metallic, A = specular strength
        var outNormal = new SKTexture(normal.Width, normal.Height);
        var occ = new SKTexture(normal.Width, normal.Height);

        for (var x = 0; x < outDiffuse.Width; x++)
        for (var y = 0; y < outDiffuse.Height; y++)
        {
            var normalPixel = normal[x, y].ToVector4();
            var maskPixel = mask[x, y].ToVector4();

            roughMet[x, y] = new Vector4(maskPixel.Z, maskPixel.Y, 0, maskPixel.X).ToSkColor();
            occ[x, y] = new Vector4(0, 0, 0, maskPixel.W).ToSkColor();


            // Base color
            var hairColor = parameters.MainColor;

            if (hairType == HairType.Face)
            {
                var tattooColorIntensity = normalPixel.Z;
                hairColor = Vector3.Lerp(hairColor, parameters.OptionColor, tattooColorIntensity);
            }
            else if (hairType == HairType.Hair)
            {
                var highlightColorIntensity = normalPixel.Z;
                if (highlightColorIntensity != 0 && data.Highlights)
                {
                    hairColor = Vector3.Lerp(hairColor, parameters.MeshColor, highlightColorIntensity);
                }
                else
                {
                    hairColor = parameters.MainColor;
                }
            }
            else
            {
                hairColor = parameters.MainColor;
            }

            outDiffuse[x, y] = new Vector4(hairColor, normalPixel.W).ToSkColor();
            outNormal[x, y] = (normalPixel with {Z = 1.0f, W = 1.0f}).ToSkColor();
        }

        var output = new MaterialBuilder(name);
        //var doubleSided = (material.ShaderFlags & 0x1) == 0;
        //output.WithDoubleSide(doubleSided);


        // using this kinda just makes everything look jank
        //var normalScale = material.GetConstantOrDefault(MaterialConstant.g_NormalScale, 1.0f);

        output.WithBaseColor(BuildImage(outDiffuse, name, "diffuse"));
        output.WithNormal(BuildImage(outNormal, name, "normal"));
        //var roughMetImg = BuildImage(roughMet, name, "roughness_metallic_specular");
        //output.WithSpecularFactor(roughMetImg, 1.0f);
        //output.WithMetallicRoughness(roughMetImg, 0.0f, 1.0f);
        //output.WithMetallicRoughnessShader();
        //output.WithOcclusion(BuildImage(occ, name, "occlusion"));

        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);

        return output;
    }
}
