using System.Numerics;
using Meddle.Utils.Export;
using SharpGLTF.Materials;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildWater(Material material, string name)
    {
        // TODO: Wavemap stuff maybe? not sure if I want to compute that since its dynamic
        var output = new MaterialBuilder(name)
                     .WithDoubleSide(material.RenderBackfaces)
                     .WithAlpha(AlphaMode.BLEND, 0.5f)
                     .WithBaseColor(new Vector4(1, 1, 1, 0f));

        return output;
    }

    public static MaterialBuilder BuildLightShaft(Material material, string name)
    {
        var output = new MaterialBuilder(name)
            .WithDoubleSide(material.RenderBackfaces);
        
        var sampler0 = material.GetTexture(TextureUsage.g_Sampler0);
        var sampler1 = material.GetTexture(TextureUsage.g_Sampler1);
        
        var texture0 = sampler0.ToTexture();
        var texture1 = sampler1.ToTexture();
        
        var outTexture = new SKTexture(texture0.Width, texture0.Height);
        
        for (var x = 0; x < outTexture.Width; x++)
        for (var y = 0; y < outTexture.Height; y++)
        {
            var tex0 = texture0[x, y].ToVector4();
            var tex1 = texture1[x, y].ToVector4();
            
            outTexture[x, y] = (tex0 * tex1).ToSkColor();
        }
        
        output.WithBaseColor(BuildImage(outTexture, name, "base"));

        return output;
    }
}
