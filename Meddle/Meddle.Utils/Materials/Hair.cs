using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Shader;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public enum HairType : uint
    {
        Face = 0x6E5B8F10,
        Hair = 0xF7B8956E
    }
    
    public static MaterialBuilder BuildHair(Material material, string name, CustomizeParameter parameters, CustomizeData data)
    {
        const uint categoryHairType = 0x24826489;
        
        HairType? hairType = null;
        if (material.ShaderKeys.Any(x => x.Category == categoryHairType))
        {
            var key = material.ShaderKeys.First(x => x.Category == categoryHairType);
            hairType = (HairType)key.Value;
        }
        
        var diffuseMultiplier = material.GetConstantOrDefault(MaterialConstant.g_DiffuseColor, Vector3.One);
        
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
            var hairColor = parameters.MainColor.ToVector3();

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
            
            // Specular
            outSpecRough[x, y] = new Vector4(maskPixel.X, maskPixel.Y, maskPixel.Z, maskPixel.W).ToSkColor();
            
            // Normal
            outNormal[x, y] = (normalPixel with {W = 1.0f}).ToSkColor();
        }

        var output = new MaterialBuilder(name);
        var doubleSided = (material.ShaderFlags & 0x1) == 0;
        output.WithDoubleSide(doubleSided);

        output.WithBaseColor(BuildImage(outDiffuse, name, "diffuse"))
              .WithNormal(BuildImage(outNormal, name, "normal"));
        
        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);
        
        return output;
    }
}
