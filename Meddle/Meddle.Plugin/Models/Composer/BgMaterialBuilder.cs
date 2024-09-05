using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;


public interface IBgMaterialBuilderParams;
    
public record BgColorChangeParams : IBgMaterialBuilderParams
{
    public Vector4? StainColor { get; init; }
    
    public BgColorChangeParams(Vector4? stainColor)
    {
        StainColor = stainColor;
    }
}

public record BgParams : IBgMaterialBuilderParams;


public class BgMaterialBuilder : MeddleMaterialBuilder, IVertexPaintMaterialBuilder
{
    private readonly IBgMaterialBuilderParams bgParams;
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;
    private const uint BgVertexPaintKey = 0x4F4F0636;
    private const uint BgVertexPaintValue = 0xBD94649A;
    private const uint DiffuseAlphaKey = 0xA9A3EE25;
    private const uint DiffuseAlphaValue = 0x72AAA9AE; // if present, alpha channel on diffuse texture is used?? and should respect g_AlphaThreshold
    public BgMaterialBuilder(string name, IBgMaterialBuilderParams bgParams, MaterialSet set, DataProvider dataProvider) : base(name)
    {
        this.bgParams = bgParams;
        this.set = set;
        this.dataProvider = dataProvider;
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
                var detailDArray = dataProvider.LookupData("bgcommon/nature/detail/texture/detail_d_array.tex");
                if (detailDArray == null)
                {
                    throw new Exception("Detail D array texture not found");
                }

                DetailDArray = new TexFile(detailDArray);
            }

            if (DetailNArray == null)
            {
                var detailNArray = dataProvider.LookupData("bgcommon/nature/detail/texture/detail_n_array.tex");
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
    
    private TextureSet GetTextureSet()
    {
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerColorMap0, out var colorMap0Texture))
        {
            throw new Exception("ColorMap0 texture not found");
        }
        
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerSpecularMap0, out var specularMap0Texture))
        {
            throw new Exception("SpecularMap0 texture not found");
        }
        
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerNormalMap0, out var normalMap0Texture))
        {
            throw new Exception("NormalMap0 texture not found");
        }
        
        var sizes = new List<Vector2> {colorMap0Texture.Size, specularMap0Texture.Size, normalMap0Texture.Size};
        var colorMap1 = set.TryGetTexture(dataProvider, TextureUsage.g_SamplerColorMap1, out var colorMap1Texture);
        var specularMap1 = set.TryGetTexture(dataProvider, TextureUsage.g_SamplerSpecularMap1, out var specularMap1Texture);
        var normalMap1 = set.TryGetTexture(dataProvider, TextureUsage.g_SamplerNormalMap1, out var normalMap1Texture);
        
        var size = sizes.MaxBy(x => x.X * x.Y);
        var colorTex0 = colorMap0Texture.ToTexture(size);
        var specularTex0 = specularMap0Texture.ToTexture(size);
        var normalTex0 = normalMap0Texture.ToTexture(size);
        var colorTex1 = colorMap1 ? colorMap1Texture.ToTexture(size) : null;
        var specularTex1 = specularMap1 ? specularMap1Texture.ToTexture(size) : null;
        var normalTex1 = normalMap1 ? normalMap1Texture.ToTexture(size) : null;
        
        return new TextureSet(colorTex0, specularTex0, normalTex0, colorTex1, specularTex1, normalTex1);
    }

    public bool VertexPaint { get; private set; }

    
    private bool GetDiffuseColor(out Vector4 diffuseColor)
    {
        if (!set.TryGetConstant(MaterialConstant.g_DiffuseColor, out Vector3 diffuseColor3))
        {
            diffuseColor = Vector4.One;
            return false;
        }

        diffuseColor = new Vector4(diffuseColor3, 1);
        return true;
    }
    
    public override MeddleMaterialBuilder Apply()
    {
        var extras = new List<(string, object)>();
        var textureSet = GetTextureSet();
        var alphaType = set.GetShaderKeyOrDefault(DiffuseAlphaKey, 0);
        if (alphaType == DiffuseAlphaValue)
        {
            var alphaThreshold = set.GetConstantOrThrow<float>(MaterialConstant.g_AlphaThreshold);
            WithAlpha(AlphaMode.MASK, alphaThreshold); // TODO: which mode?
            extras.Add(("AlphaThreshold", alphaThreshold));
        }
        
        if (bgParams is BgColorChangeParams bgColorChangeParams && GetDiffuseColor(out var bgColorChangeDiffuseColor))
        {
            var diffuseColor = bgColorChangeParams.StainColor ?? bgColorChangeDiffuseColor;
            extras.Add(("DiffuseColor", diffuseColor.AsFloatArray()));
        }

        VertexPaint = set.ShaderKeys.Any(x => x is {Category: BgVertexPaintKey, Value: BgVertexPaintValue});
        extras.Add(("VertexPaint", VertexPaint));
        
        
        // Stub
        WithNormal(dataProvider.CacheTexture(textureSet.Normal0, $"Computed/{set.ComputedTextureName("normal")}"));
        WithBaseColor(dataProvider.CacheTexture(textureSet.Color0, $"Computed/{set.ComputedTextureName("diffuse")}"));
        //WithSpecularColor(dataProvider.CacheTexture(textureSet.Specular0, $"Computed/{set.ComputedTextureName("specular")}"));
        
        Extras = set.ComposeExtrasNode(extras.ToArray());
        return this;
    }
}
