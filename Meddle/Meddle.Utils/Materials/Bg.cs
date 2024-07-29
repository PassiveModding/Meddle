using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Utils.Materials;

public static partial class MaterialUtility
{
    public static MaterialBuilder BuildBg(Material material, string name)
    {
        SKTexture? specularMap0 = null;
        SKTexture? specularMap1 = null;
        SKTexture? colorMap0 = null;
        SKTexture? colorMap1 = null;
        SKTexture? normalMap0 = null;
        SKTexture? normalMap1 = null;
        
        if (material.TryGetTexture(TextureUsage.g_SamplerSpecularMap0, out var specular0))
        {
            specularMap0 = specular0.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerSpecularMap1, out var specular1))
        {
            specularMap1 = specular1.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerColorMap0, out var color0))
        {
            colorMap0 = color0.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerColorMap1, out var color1))
        {
            colorMap1 = color1.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerNormalMap0, out var normal0))
        {
            normalMap0 = normal0.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerNormalMap1, out var normal1))
        {
            normalMap1 = normal1.ToTexture();
        }
        
        var output = new MaterialBuilder(name)
            .WithDoubleSide(material.RenderBackfaces)
            .WithAlpha(AlphaMode.MASK, 0.5f);

        if (colorMap1 != null)
        {
            output.WithBaseColor(BuildImage(colorMap1, name, "colorMap1"));
        }
        else if (colorMap0 != null)
        {
            output.WithBaseColor(BuildImage(colorMap0, name, "colorMap0"));
        }
        else
        {
            output.WithBaseColor(Vector4.One);
        }
        
        if (normalMap1 != null) output.WithNormal(BuildImage(normalMap1, name, "normalMap1"));
        if (normalMap0 != null) output.WithNormal(BuildImage(normalMap0, name, "normalMap0"));
        if (specularMap1 != null) output.WithSpecularColor(BuildImage(specularMap1, name, "specularMap1"));
        if (specularMap0 != null) output.WithSpecularColor(BuildImage(specularMap0, name, "specularMap0"));
        
        return output;
    }

    public static MaterialBuilder BuildBgProp(Material material, string name)
    {
        SKTexture? colorMap0 = null;
        SKTexture? normalMap0 = null;
        SKTexture? specularMap0 = null;
        
        if (material.TryGetTexture(TextureUsage.g_SamplerColorMap0, out var color0))
        {
            colorMap0 = color0.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerNormalMap0, out var normal0))
        {
            normalMap0 = normal0.ToTexture();
        }
        
        if (material.TryGetTexture(TextureUsage.g_SamplerSpecularMap0, out var specular0))
        {
            specularMap0 = specular0.ToTexture();
        }
        
        var output = new MaterialBuilder(name)
            .WithDoubleSide(material.RenderBackfaces)
            .WithAlpha(AlphaMode.MASK, 0.5f);

        if (colorMap0 != null)
        {
            output.WithBaseColor(BuildImage(colorMap0, name, "colorMap0"));;
        }
        else
        {
            output.WithBaseColor(Vector4.One);
        }
        
        
        if (normalMap0 != null) output.WithNormal(BuildImage(normalMap0, name, "normalMap0"));
        if (specularMap0 != null) output.WithSpecularColor(BuildImage(specularMap0, name, "specularMap0"));
        
        return output;
    }
}
