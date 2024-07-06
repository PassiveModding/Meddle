using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Shader;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildIris(Material material, string name, CustomizeParameter parameters, CustomizeData data)
    {
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture(); 
        SKTexture diffuse = material.GetTexture(TextureUsage.g_SamplerDiffuse).ToTexture((normal.Width, normal.Height));
        SKTexture mask = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture((normal.Width, normal.Height)); // emissive, reflection/cubemap, iris

        var leftIrisColor = parameters.LeftColor;
        //var rightIrisColor = parameters.RightColor; // based on vertex info, not texture
        
        var outDiffuse = new SKTexture(normal.Width, normal.Height);
        var outNormal = new SKTexture(normal.Width, normal.Height);
        for (var x = 0; x < outDiffuse.Width; x++)
        for (var y = 0; y < outDiffuse.Height; y++)
        {
            var maskPixel = mask[x, y].ToVector4();
            var normalPixel = normal[x, y].ToVector4();
            var diffusePixel = diffuse[x, y].ToVector4();

            outDiffuse[x, y] = diffusePixel.ToSkColor();
            outNormal[x, y] = normalPixel.ToSkColor();
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
