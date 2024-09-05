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

        if (!set.TryGetImageBuilderStrict(dataProvider, TextureUsage.g_SamplerColorMap0, out var colorMap0Texture))
        {
            throw new Exception("ColorMap0 texture not found");
        }
        
        if (!set.TryGetImageBuilderStrict(dataProvider, TextureUsage.g_SamplerSpecularMap0, out var specularMap0Texture))
        {
            throw new Exception("SpecularMap0 texture not found");
        }
        
        if (!set.TryGetImageBuilderStrict(dataProvider, TextureUsage.g_SamplerNormalMap0, out var normalMap0Texture))
        {
            throw new Exception("NormalMap0 texture not found");
        }
        
        set.TryGetImageBuilder(dataProvider, TextureUsage.g_SamplerColorMap1, out var colorMap1Texture);
        set.TryGetImageBuilder(dataProvider, TextureUsage.g_SamplerSpecularMap1, out var specularMap1Texture);
        set.TryGetImageBuilder(dataProvider, TextureUsage.g_SamplerNormalMap1, out var normalMap1Texture);
        
        
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

        WithNormal(normalMap0Texture);
        WithBaseColor(colorMap0Texture);
        // only the green/y channel is used here for roughness
        WithMetallicRoughness(specularMap0Texture, 0.0f, 1.0f);
        
        IndexOfRefraction = 1.0f;
        
        Extras = set.ComposeExtrasNode(extras.ToArray());
        return this;
    }
}
