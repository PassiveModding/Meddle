using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly string shpkSuffix;
    public BgMaterialBuilder(string name, string shpkName, MaterialSet set, Func<string, byte[]?> lookupFunc, Func<SKTexture, string, ImageBuilder> cacheFunc) : base(name, shpkName, lookupFunc, cacheFunc)
    {
        this.set = set;
        shpkSuffix = Path.GetExtension(shpkName);
    }
    
    private record TextureSet(SKTexture Color0, SKTexture Specular0, SKTexture Normal0, SKTexture? Color1, SKTexture? Specular1, SKTexture? Normal1);

    // DetailID =
    // bgcommon/nature/detail/texture/detail_d_array.tex
    // bgcommon/nature/detail/texture/detail_n_array.tex
    private static TexFile? DetailDArray;
    private static TexFile? DetailNArray;
    private static readonly object DetailLock = new();
    private record DetailSet(SKTexture Diffuse, SKTexture Normal, Vector3 DetailColor, float DetailNormalScale, Vector4 DetailColorUvScale, Vector4 DetailNormalUvScale);
    
    private DetailSet GetDetail(int detailId, Vector2 size)
    {
        lock (DetailLock)
        {
            const int maxDetailId = 32;
            if (detailId < 0 || detailId >= maxDetailId)
            {
                throw new ArgumentOutOfRangeException(nameof(detailId),
                                                      $"Detail ID must be between 0 and {maxDetailId - 1}");
            }

            if (DetailDArray == null)
            {
                var detailDArray = LookupFunc("bgcommon/nature/detail/texture/detail_d_array.tex");
                if (detailDArray == null)
                {
                    throw new Exception("Detail D array texture not found");
                }

                DetailDArray = new TexFile(detailDArray);
            }

            if (DetailNArray == null)
            {
                var detailNArray = LookupFunc("bgcommon/nature/detail/texture/detail_n_array.tex");
                if (detailNArray == null)
                {
                    throw new Exception("Detail N array texture not found");
                }

                DetailNArray = new TexFile(detailNArray);
            }

            var detailD = ImageUtils.GetTexData(DetailDArray, detailId, 0, 0).ToTexture(size);
            var detailN = ImageUtils.GetTexData(DetailNArray, detailId, 0, 0).ToTexture(size);
            
            if (!set.TryGetConstant(MaterialConstant.g_DetailColor, out Vector3 detailColor))
            {
                detailColor = Vector3.One;
            }
            
            if (!set.TryGetConstant(MaterialConstant.g_DetailNormalScale, out float detailNormalScale))
            {
                detailNormalScale = 1;
            }
            
            if (!set.TryGetConstant(MaterialConstant.g_DetailColorUvScale, out Vector4 detailColorUvScale))
            {
                detailColorUvScale = new Vector4(4);
            }
            
            if (!set.TryGetConstant(MaterialConstant.g_DetailNormalUvScale, out Vector4 detailNormalUvScale))
            {
                detailNormalUvScale = new Vector4(4);
            }
            
            return new DetailSet(detailD, detailN, detailColor, detailNormalScale, detailColorUvScale, detailNormalUvScale);
        }
    }
    
    private TextureResource? GetTextureResourceOrNull(TextureUsage usage, ref List<Vector2> sizes)
    {
        if (set.TextureUsageDict.TryGetValue(usage, out var texture))
        {
            if (texture.Contains("_dummy"))
            {
                return null;
            }
            var textureResource = LookupFunc(texture);
            if (textureResource != null)
            {
                var resource = new TexFile(textureResource).ToResource();
                sizes.Add(resource.Size);
                return resource;
            }
        }
        
        return null;
    }
    
    private TextureSet GetTextureSet()
    {
        var colorMap0 = set.TextureUsageDict[TextureUsage.g_SamplerColorMap0];
        var specularMap0 = set.TextureUsageDict[TextureUsage.g_SamplerSpecularMap0];
        var normalMap0 = set.TextureUsageDict[TextureUsage.g_SamplerNormalMap0];
        var colorMap0Texture = LookupFunc(colorMap0) ?? throw new Exception("ColorMap0 texture not found");
        var specularMap0Texture = LookupFunc(specularMap0) ?? throw new Exception("SpecularMap0 texture not found");
        var normalMap0Texture = LookupFunc(normalMap0) ?? throw new Exception("NormalMap0 texture not found");

        var colorRes0 = new TexFile(colorMap0Texture).ToResource();
        var specularRes0 = new TexFile(specularMap0Texture).ToResource();
        var normalRes0 = new TexFile(normalMap0Texture).ToResource();
        var sizes = new List<Vector2>
        {
            colorRes0.Size,
            specularRes0.Size,
            normalRes0.Size
        };
        
        var colorRes1 = GetTextureResourceOrNull(TextureUsage.g_SamplerColorMap1, ref sizes);
        var specularRes1 = GetTextureResourceOrNull(TextureUsage.g_SamplerSpecularMap1, ref sizes);
        var normalRes1 = GetTextureResourceOrNull(TextureUsage.g_SamplerNormalMap1, ref sizes);
        
        var size = Max(sizes);
        var colorTex0 = colorRes0.ToTexture(size);
        var specularTex0 = specularRes0.ToTexture(size);
        var normalTex0 = normalRes0.ToTexture(size);
        var colorTex1 = colorRes1?.ToTexture(size);
        var specularTex1 = specularRes1?.ToTexture(size);
        var normalTex1 = normalRes1?.ToTexture(size);
        
        return new TextureSet(colorTex0, specularTex0, normalTex0, colorTex1, specularTex1, normalTex1);
    }
    
    
    public BgMaterialBuilder WithBgColorChange(Vector4? stainColor)
    {
        Apply(stainColor);
        return this;
    }

    private void SaveAllTextures()
    {
        foreach (var (usage, path) in set.TextureUsageDict)
        {
            var texture = LookupFunc(path);
            if (texture == null)
            {
                continue;
            }
            var tex = new TexFile(texture).ToResource().ToTexture();
            CacheFunc(tex, $"Debug/{Path.GetFileNameWithoutExtension(path)}");
        }
    }

   
    public BgMaterialBuilder WithBg()
    {
        Apply();
        return this;
    }

    public bool VertexPaint { get; private set; }
    public Vector4 TangentMultiplier { get; private set; }
    
    private Vector2 Max(IEnumerable<Vector2> vectors)
    {
        var max = new Vector2(float.MinValue);
        foreach (var vector in vectors)
        {
            max = Vector2.Max(max, vector);
        }

        return max;
    }
    
    private void Apply(Vector4? stainColor = null)
    {
        var textureSet = GetTextureSet();
        var useAlpha = set.ShaderKeys.Any(x => x is {Category: DiffuseAlphaKey, Value: DiffuseAlphaValue});
        if (useAlpha && set.TryGetConstant(MaterialConstant.g_AlphaThreshold, out float alphaThreshold))
        {
            WithAlpha(AlphaMode.MASK, alphaThreshold);
        }

        if (!set.TryGetConstant(MaterialConstant.g_DiffuseColor, out Vector3 diffuseColor))
        {
            diffuseColor = Vector3.One;
        }

        // if (!set.TryGetConstant(MaterialConstant.g_SpecularColor, out Vector3 specularColor))
        // {
        //     specularColor = Vector3.One;
        // }

        // Vector3 emmissiveColor;
        // if (!set.TryGetConstant(MaterialConstant.g_EmissiveColor, out emmissiveColor))
        // {
        //     emmissiveColor = Vector3.Zero;
        // }

        Vector4 diffuseColor0;
        if (stainColor != null && stainColor != Vector4.Zero)
        {
            diffuseColor0 = stainColor.Value;
        }
        else
        {
            diffuseColor0 = new Vector4(diffuseColor, 1);
        }

        if (!set.TryGetConstant(MaterialConstant.g_DetailID, out float detailId))
        {
            detailId = 0;
        }

        if (!set.TryGetConstant(MaterialConstant.g_NormalScale, out float normalScale))
        {
            normalScale = 1;
        }
        
        if (!set.TryGetConstant(MaterialConstant.g_ColorUVScale, out Vector4 colorUvScale))
        {
            colorUvScale = new Vector4(1);
        }
        
        //var detail = GetDetail((int)detailId, textureSet.Color0.Size);
        //var detailColor0 = new Vector4(detail.DetailColor, 1);
        
        //var specularColor0 = new Vector4(specularColor, 1);
        SKTexture diffuse = new SKTexture(textureSet.Color0.Width, textureSet.Color0.Height);
        SKTexture normal = new SKTexture(textureSet.Color0.Width, textureSet.Color0.Height);
        //for (int x = 0; x < diffuse.Width; x++)
        //for (int y = 0; y < diffuse.Height; y++)
        Parallel.For(0, diffuse.Width, x =>
        {
            for (int y = 0; y < diffuse.Height; y++)
            {
                var color0 = textureSet.Color0[x, y].ToVector4();
                var normal0 = textureSet.Normal0[x, y].ToVector4();
                var color1 = textureSet.Color1?[x, y].ToVector4() ?? Vector4.One;
                var normal1 = textureSet.Normal1?[x, y].ToVector4() ?? Vector4.One;

                // idk about this one tbh
                //var specular0 = textureSet.Specular0[x, y].ToVector4();
                //var specular1 = textureSet.Specular1?[x, y].ToVector4() ?? Vector4.One;
                //var specularData = (specularColor0 * specular0 * specular1); 

                // var detailColorMask = detail.Diffuse[x, y].ToVector4();
                // var detailNormalMap = detail.Normal[x, y].ToVector4();

                var outDiffuse = color0 * color1;
                //var color = Vector4.Lerp(diffuseColor0, detailColor0, detailColorMask.Z);
                // diffuse alpha is a mask for stain color
                if (Shpk == "bgcolorchange.shpk")
                {
                    outDiffuse = Vector4.Lerp(outDiffuse, diffuseColor0, outDiffuse.W);
                    outDiffuse.W = 1;
                }

                // normal blue is weight for the detail normal map?
                var outNormal = (normal0 * normal1 * normalScale);
                //outNormal = Vector4.Lerp(outNormal, detailNormalMap * detail.DetailNormalScale, detailColorMask.Z);
                // maybe?
                outNormal *= outNormal.Z;
                outNormal.W = 1;


                diffuse[x, y] = outDiffuse.ToSkColor();
                //emmissive[x, y] = Vector4.Lerp(Vector4.Zero, new Vector4(emissiveColor, 1), specularData.X).ToSkColor();
                //specular[x, y] = specularData.ToSkColor();
                normal[x, y] = outNormal.ToSkColor();
            }
        });

        VertexPaint = set.ShaderKeys.Any(x => x is {Category: BGVertexPaintKey, Value: BGVertexPaintValue});
        Extras = set.ComposeExtrasNode();

        var extrasDict = set.ComposeExtras();
        var stainString = "";
        if (Shpk == "bgcolorchange.shpk")
        {
            stainString = $"_{ToHex(diffuseColor0)}";
            extrasDict["stainColor"] = ToHex(diffuseColor0);
        }
        Extras = JsonNode.Parse(JsonSerializer.Serialize(extrasDict, MaterialSet.JsonOptions))!;
        
        WithNormal(CacheFunc(normal, $"Computed/{Path.GetFileNameWithoutExtension(set.MtrlPath)}_{shpkSuffix}_normal"));
        WithBaseColor(CacheFunc(diffuse, $"Computed/{Path.GetFileNameWithoutExtension(set.MtrlPath)}_{shpkSuffix}{stainString}_diffuse"));
        
        // should not be applied uniformly. strength is probably dictated using one of the spec channels
        // WithEmissive(CacheFunc(emmissive, $"{Path.GetFileNameWithoutExtension(set.MtrlPath)}_bg_emissive"));
        //CacheFunc(detail.Diffuse, $"Debug/{Path.GetFileNameWithoutExtension(set.MtrlPath)}_{shpkSuffix}_detail{(int)detailId}_diffuse");
        //CacheFunc(detail.Normal, $"Debug/{Path.GetFileNameWithoutExtension(set.MtrlPath)}_{shpkSuffix}_detail{(int)detailId:D}_normal");
        SaveAllTextures();
    }
    
    private string ToHex(Vector4 color)
    {
        var r = (byte)(color.X * 255);
        var g = (byte)(color.Y * 255);
        var b = (byte)(color.Z * 255);
        var a = (byte)(color.W * 255);
        return $"{r:X2}{g:X2}{b:X2}{a:X2}";
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
