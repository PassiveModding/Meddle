using System.Numerics;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Memory;

namespace Meddle.Plugin.Models.Composer;

public class BgMaterialBuilder : InstanceMaterialBuilder, IVertexPaintMaterialBuilder
{
    private readonly MaterialSet set;
    private const uint BGVertexPaintKey = 0x4F4F0636;
    private const uint BGVertexPaintValue = 0xBD94649A;
    private const uint DiffuseAlphaKey = 0xA9A3EE25;
    private const uint DiffuseAlphaValue = 0x72AAA9AE; // if present, alpha channel on diffuse texture is used?? and should respect g_AlphaThreshold
    
    public BgMaterialBuilder(string name, MaterialSet set, Func<string, byte[]?> lookupFunc, Func<SKTexture, string, MemoryImage> cacheFunc) : base(name, "bg.shpk", lookupFunc, cacheFunc)
    {
        this.set = set;
    }
    

    public BgMaterialBuilder WithBg()
    {
        // samplers
        var colorMap0 = set.TextureUsageDict[TextureUsage.g_SamplerColorMap0];
        var specularMap0 = set.TextureUsageDict[TextureUsage.g_SamplerSpecularMap0];
        var normalMap0 = set.TextureUsageDict[TextureUsage.g_SamplerNormalMap0];
        var colorMap0Texture = LookupFunc(colorMap0);
        var specularMap0Texture = LookupFunc(specularMap0);
        var normalMap0Texture = LookupFunc(normalMap0);
        if (colorMap0Texture == null || specularMap0Texture == null || normalMap0Texture == null)
        {
            return this;
        }

        var colorRes0 = new TexFile(colorMap0Texture).ToResource();
        var specularRes0 = new TexFile(specularMap0Texture).ToResource();
        var normalRes0 = new TexFile(normalMap0Texture).ToResource();
        var sizes = new List<Vector2>
        {
            colorRes0.Size,
            specularRes0.Size,
            normalRes0.Size
        };
        
        TextureResource? colorRes1 = null;
        if (set.TextureUsageDict.TryGetValue(TextureUsage.g_SamplerColorMap1, out var colorMap1))
        {
            var colorMap1Texture = LookupFunc(colorMap1);
            if (colorMap1Texture != null)
            {
                colorRes1 = new TexFile(colorMap1Texture).ToResource();
                sizes.Add(colorRes1.Value.Size);
            }
        }
        
        TextureResource? specularRes1 = null;
        if (set.TextureUsageDict.TryGetValue(TextureUsage.g_SamplerSpecularMap1, out var specularMap1))
        {
            var specularMap1Texture = LookupFunc(specularMap1);
            if (specularMap1Texture != null)
            {
                specularRes1 = new TexFile(specularMap1Texture).ToResource();
                sizes.Add(specularRes1.Value.Size);
            }
        }
        
        TextureResource? normalRes1 = null;
        if (set.TextureUsageDict.TryGetValue(TextureUsage.g_SamplerNormalMap1, out var normalMap1))
        {
            var normalMap1Texture = LookupFunc(normalMap1);
            if (normalMap1Texture != null)
            {
                normalRes1 = new TexFile(normalMap1Texture).ToResource();
                sizes.Add(normalRes1.Value.Size);
            }
        }
        
        var size = Max(sizes);
        var colorTex0 = colorRes0.ToTexture(size);
        var specularTex0 = specularRes0.ToTexture(size);
        var normalTex0 = normalRes0.ToTexture(size);
        var colorTex1 = colorRes1?.ToTexture(size);
        var specularTex1 = specularRes1?.ToTexture(size);
        var normalTex1 = normalRes1?.ToTexture(size);
        
        var useAlpha = set.ShaderKeys.Any(x => x is {Category: DiffuseAlphaKey, Value: DiffuseAlphaValue});
        if (useAlpha && set.TryGetConstant(MaterialConstant.g_AlphaThreshold, out float alphaThreshold))
        {
            WithAlpha(AlphaMode.MASK, alphaThreshold);
        }

        Vector3 tmp;
        Vector3 diffuseColor = Vector3.One;
        if (set.TryGetConstant(MaterialConstant.g_DiffuseColor, out tmp))
        {
            diffuseColor = tmp;
        }
        Vector3 specularColor = Vector3.One;
        if (set.TryGetConstant(MaterialConstant.g_SpecularColor, out tmp))
        {
            specularColor = tmp;
        }
        Vector3 emissiveColor = Vector3.Zero;
        if (set.TryGetConstant(MaterialConstant.g_EmissiveColor, out tmp))
        {
            emissiveColor = tmp;
        }
        
        var diffuseColor0 = new Vector4(diffuseColor, 1);
        var specularColor0 = new Vector4(specularColor, 1);
        SKTexture diffuse = new SKTexture((int)size.X, (int)size.Y);
        //SKTexture specular = new SKTexture((int)size.X, (int)size.Y);
        //SKTexture emmissive = new SKTexture((int)size.X, (int)size.Y);
        SKTexture normal = new SKTexture((int)size.X, (int)size.Y);
        for (int x = 0; x < diffuse.Width; x++)
        for (int y = 0; y < diffuse.Height; y++)
        {
            var color0 = colorTex0[x, y].ToVector4();
            var specular0 = specularTex0[x, y].ToVector4();
            var normal0 = normalTex0[x, y].ToVector4();
            var color1 = colorTex1?[x, y].ToVector4() ?? Vector4.One;
            var specular1 = specularTex1?[x, y].ToVector4() ?? Vector4.One;
            var normal1 = normalTex1?[x, y].ToVector4() ?? Vector4.One;
            
            
            var outDiffuse = (diffuseColor0 * color0 * color1).ToSkColor();
            
            var specularData = (specularColor0 * specular0 * specular1);
            var outNormal = (normal0 * normal1).ToSkColor();
            
            diffuse[x, y] = outDiffuse;
            //emmissive[x, y] = Vector4.Lerp(Vector4.Zero, new Vector4(emissiveColor, 1), specularData.X).ToSkColor();
            //specular[x, y] = specularData.ToSkColor();
            normal[x, y] = outNormal;
        }
        
        VertexPaint = set.ShaderKeys.Any(x => x is {Category: BGVertexPaintKey, Value: BGVertexPaintValue});
        Extras = set.ComposeExtrasNode();
        
        if (set.TryGetConstant(MaterialConstant.g_NormalScale, out float normalScale))
        {
            WithNormal(CacheFunc(normal, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_computed_bg_normal"), normalScale);
        }
        else
        {
            WithNormal(CacheFunc(normal, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_computed_bg_normal"));
        }
        
        WithBaseColor(CacheFunc(diffuse, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_computed_bg_diffuse"));
        
        // should not be applied uniformly. strength is probably dictated using one of the spec channels
        // WithEmissive(CacheFunc(emmissive, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_bg_emissive"));
        
        CacheFunc(colorTex0, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_bg_color0");
        CacheFunc(specularTex0, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_bg_specular0");
        CacheFunc(normalTex0, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_bg_normal0");
        if (colorTex1 != null)
        {
            CacheFunc(colorTex1, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_bg_color1");
        }
        if (specularTex1 != null)
        {
            CacheFunc(specularTex1, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_bg_specular1");
        }
        if (normalTex1 != null)
        {
            CacheFunc(normalTex1, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_bg_normal1");
        }
        
        return this;
    }

    public bool VertexPaint { get; private set; }
    
    private Vector2 Max(IEnumerable<Vector2> vectors)
    {
        var max = new Vector2(float.MinValue);
        foreach (var vector in vectors)
        {
            max = Vector2.Max(max, vector);
        }

        return max;
    }
    
    /*
g_AlphaThreshold = [0]
g_ShadowAlphaThreshold = [0.5]
g_ShaderID = [0]
g_DiffuseColor = [1, 1, 1]
g_MultiDiffuseColor = [1, 1, 1]
g_SpecularColor = [1, 1, 1]
g_MultiSpecularColor = [1, 1, 1]
g_EmissiveColor = [0, 0, 0]
g_MultiEmissiveColor = [0, 0, 0]
g_NormalScale = [1]
g_MultiNormalScale = [1]
g_HeightScale = [0.015]
g_MultiHeightScale = [0.015]
g_SSAOMask = [1]
g_MultiSSAOMask = [1]
0xBFE9D12D = [1]
0x093084AD = [1]
g_InclusionAperture = [1]
0x5106E045 = [0]
g_ColorUVScale = [1, 1, 1, 1]
g_SpecularUVScale = [1, 1, 1, 1]
g_NormalUVScale = [1, 1, 1, 1]
g_AlphaMultiParam = [0, 0, 0, 0]
g_DetailID = [0]
0xAC156136 = [0]
g_DetailColor = [0.5, 0.5, 0.5]
g_MultiDetailColor = [0.5, 0.5, 0.5]
0xF769298E = [0.3, 0.3, 0.3, 0.3]
g_DetailNormalScale = [1]
0xA83DBDF1 = [1]
g_DetailNormalUvScale = [4, 4, 4, 4]
g_DetailColorUvScale = [4, 4, 4, 4]
0xB8ACCE58 = [50, 100, 50, 100]
0xD67F62C8 = [1]
0x12F6AB51 = [3]
0x236EE793 = [0, 0]
0xF3F28C58 = [0, 0]
0x756DFE22 = [0, 0]
0xB10AF2DA = [0, 0]
0x9A696A17 = [10, 10, 10, 10]
g_EnvMapPower = [0.85]
     */
}
