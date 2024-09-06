using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using SharpGLTF.Materials;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildIris(Material material, string name, TexFile cubemapArray, CustomizeParameter parameters, CustomizeData data)
    {
        SKTexture normal = material.GetTexture(TextureUsage.g_SamplerNormal).ToTexture(); 
        SKTexture diffuse = material.GetTexture(TextureUsage.g_SamplerDiffuse).ToTexture((normal.Width, normal.Height));
        SKTexture mask = material.GetTexture(TextureUsage.g_SamplerMask).ToTexture((normal.Width, normal.Height)); // emissive, reflection/cubemap, iris
        
        var sphereMapIndex = (int)material.GetConstantOrDefault(MaterialConstant.g_SphereMapIndex, 0);
        var texImage = ImageUtils.GetTexData(cubemapArray, sphereMapIndex, 0, 0).ToTexture().Resize(normal.Width, normal.Height);
        var whiteEyeColor = material.GetConstantOrDefault(MaterialConstant.g_WhiteEyeColor, new Vector3(1.0f));

        var leftIrisColor = parameters.LeftColor;
        //var rightIrisColor = parameters.RightColor; // based on vertex info, not texture
        
        var outDiffuse = new SKTexture(normal.Width, normal.Height);
        var outNormal = new SKTexture(normal.Width, normal.Height);
        var outEmissive = new SKTexture(normal.Width, normal.Height);
        var outSpecular = new SKTexture(normal.Width, normal.Height);
        
        for (var x = 0; x < outDiffuse.Width; x++)
        for (var y = 0; y < outDiffuse.Height; y++)
        {
            var maskPixel = mask[x, y].ToVector4();
            var normalPixel = normal[x, y].ToVector4();
            var diffusePixel = diffuse[x, y].ToVector4();
            var texPixel = texImage[x, y].ToVector4();
            
            // use mask blue as iris mask
            var irisMask = maskPixel.Z;
            var whites = diffusePixel * new Vector4(whiteEyeColor, 1.0f);
            var iris = diffusePixel * (leftIrisColor with {W = 1.0f });
            diffusePixel = Vector4.Lerp(whites, iris, irisMask);

            // most textures this channel is just 0
            // use mask red as emissive mask
            outEmissive[x, y] = new Vector4(maskPixel.X, maskPixel.X, maskPixel.X, 1.0f).ToSkColor();
            
            // use mask green as reflection mask/cubemap intensity
            var specular = new Vector4(texPixel.X * maskPixel.Y);
            outSpecular[x, y] = (specular with {W = 1.0f}).ToSkColor();
            
            outDiffuse[x, y] = diffusePixel.ToSkColor();
            outNormal[x, y] = normalPixel.ToSkColor();
        }
        
        
        var output = new MaterialBuilder(name);
        output.WithDoubleSide(material.RenderBackfaces);

        output.WithBaseColor(BuildImage(outDiffuse, name, "diffuse"))
              .WithNormal(BuildImage(outNormal, name, "normal"))
              .WithSpecularFactor(BuildImage(outSpecular, name, "specular"), 0.2f)
              .WithSpecularColor(BuildImage(outSpecular, name, "specular"))
              .WithEmissive(BuildImage(outEmissive, name, "emissive"));
        
        var alphaThreshold = material.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            output.WithAlpha(AlphaMode.MASK, alphaThreshold);
        
        return output;
    }
}
