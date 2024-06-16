using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildHair(Material material, string name, MaterialParameters parameters)
    {
        const uint categoryHairType = 0x24826489;
        const uint valueFace        = 0x6E5B8F10;
        
        var isFace = material.ShaderKeys.Any(x => x is {Category: categoryHairType, Value: valueFace});
        
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture();
        SKTexture mask = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture();
        
        var outDiffuse = new SKTexture(normal.Width, normal.Height);
        var outSpecRough = new SKTexture(normal.Width, normal.Height);
        var outNormal = new SKTexture(normal.Width, normal.Height);
        
        var refMask = mask.Resize(normal.Width, normal.Height);
        var refNormal = normal.Resize(normal.Width, normal.Height);
        for (var x = 0; x < outDiffuse.Width; x++)
        for (var y = 0; y < outDiffuse.Height; y++)
        {
            var maskPixel = refMask[x, y].ToVector4();
            var normalPixel = refNormal[x, y].ToVector4();

            
            // Base color
            var hairColor = parameters.HairColor;
            if (isFace)
            {        
                var tattooColorIntensity = normalPixel.Z;        
                if (parameters.TattooColor.HasValue && tattooColorIntensity != 0)
                {
                    var highlightColor = parameters.TattooColor.Value;
                    hairColor = Vector3.Lerp(hairColor, highlightColor, tattooColorIntensity);
                }
            }
            else
            {
                var highlightColorIntensity = normalPixel.Z;
                if (parameters.HighlightColor.HasValue && highlightColorIntensity != 0)
                {
                    var highlightColor = parameters.HighlightColor.Value;
                    hairColor = Vector3.Lerp(hairColor, highlightColor, highlightColorIntensity);
                }
            }

            outDiffuse[x, y] = new Vector4(hairColor, normalPixel.W).ToSkColor();
            
            // Specular
            outSpecRough[x, y] = new Vector4(maskPixel.X, maskPixel.Y, maskPixel.Z, maskPixel.W).ToSkColor();
            
            // Normal
            outNormal[x, y] = normalPixel.ToSkColor();
        }

        var output = new MaterialBuilder(name);
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);

        output.WithBaseColor(BuildImage(outDiffuse, name, "diffuse"))
              .WithNormal(BuildImage(outNormal, name, "normal"))
              .WithAlpha(isFace ? AlphaMode.BLEND : AlphaMode.MASK, 0.5f);
        
        return output;
    }
}
