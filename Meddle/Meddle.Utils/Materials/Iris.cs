using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildIris(Material material, string name)
    {
        SKTexture? normal = null;
        SKTexture? mask = null;
        SKTexture? catchLight = null;
        
        if (material.TryGetTexture(TextureUsage.g_SamplerNormal, out var normalTexture))
        {
            normal = normalTexture.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerMask, out var maskTexture))
        {
            mask = maskTexture.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerCatchlight, out var catchLightTexture))
        {
            catchLight = catchLightTexture.ToTexture();
        }
        
        var output = new MaterialBuilder(name)
                     .WithDoubleSide(true)
                     .WithMetallicRoughnessShader()
                     .WithBaseColor(Vector4.One);
        
        if (normal != null) output.WithNormal(BuildImage(normal, name, "normal"));
        if (mask != null) output.WithSpecularFactor(BuildImage(mask, name, "mask"), 1);
        if (catchLight != null) output.WithEmissive(BuildImage(catchLight, name, "catchlight"));
        
        return output;
    }
}
