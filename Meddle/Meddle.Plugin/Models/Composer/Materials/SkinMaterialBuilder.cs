using System.Numerics;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Materials;
using AlphaMode = SharpGLTF.Materials.AlphaMode;

namespace Meddle.Plugin.Models.Composer.Materials;

public class SkinMaterialBuilder : MeddleMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;
    private readonly CustomizeParameter parameters;
    private readonly CustomizeData data;
    private readonly TextureMode textureMode;

    public SkinMaterialBuilder(
        string name, MaterialSet set, DataProvider dataProvider, CustomizeParameter parameters, CustomizeData data,
        TextureMode textureMode) : base(name)
    {
        this.set = set;
        this.dataProvider = dataProvider;
        this.parameters = parameters;
        this.data = data;
        this.textureMode = textureMode;
    }

    private void ApplyComputed()
    {
         var skinType = set.GetShaderKeyOrDefault(ShaderCategory.CategorySkinType, SkinType.Face);
        
        // var normalTexture = set.GetTexture(dataProvider, TextureUsage.g_SamplerNormal).ToResource().ToTexture();
        // var maskTexture = set.GetTexture(dataProvider, TextureUsage.g_SamplerMask).ToResource().ToTexture(normalTexture.Size);
        // var diffuseTexture = set.GetTexture(dataProvider, TextureUsage.g_SamplerDiffuse).ToResource().ToTexture(normalTexture.Size);
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerNormal, out var normalRes))
            throw new InvalidOperationException("Missing normal texture");
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerMask, out var maskRes))
            throw new InvalidOperationException("Missing mask texture");
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerDiffuse, out var diffuseRes))
            throw new InvalidOperationException("Missing diffuse texture");
        
        var normalTexture = normalRes.ToTexture();
        var maskTexture = maskRes.ToTexture(normalTexture.Size);
        var diffuseTexture = diffuseRes.ToTexture(normalTexture.Size);
        
        
        // PART_BODY = no additional color
        // PART_FACE/default = lip color
        // PART_HRO = hairColor blend into hair highlight color

        var skinColor = parameters.SkinColor;
        var lipColor = parameters.LipColor;
        var hairColor = parameters.MainColor;
        var highlightColor = parameters.MeshColor;
        var diffuseColor = set.GetConstantOrDefault(MaterialConstant.g_DiffuseColor, Vector3.One);
        var alphaThreshold = set.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        var lipRoughnessScale = set.GetConstantOrDefault(MaterialConstant.g_LipRoughnessScale, 0.7f);
        var alphaMultiplier = alphaThreshold != 0 ? 1.0f / alphaThreshold : 1.0f;
        
        var metallicRoughnessTexture = new SKTexture(normalTexture.Width, normalTexture.Height);
        var sssTexture = new SKTexture(normalTexture.Width, normalTexture.Height);
        Partitioner.Iterate(normalTexture.Size, (x, y) =>
        {
            var normal = normalTexture[x, y].ToVector4();
            var mask = maskTexture[x, y].ToVector4();
            var diffuse = diffuseTexture[x, y].ToVector4();

            var diffuseAlpha = diffuse.W;
            var skinInfluence = normal.Z;
            var specularPower = mask.X;
            var roughness = mask.Y;
            var sssThickness = mask.Z;
            var metallic = 0.0f;
            var hairHighlightInfluence = mask.W;

            var sColor = Vector3.Lerp(diffuseColor, skinColor, skinInfluence);
            diffuse *= new Vector4(sColor, 1.0f);

            if (skinType == SkinType.Hrothgar)
            {
                var hair = hairColor;
                if (data.Highlights)
                {
                    hair = Vector3.Lerp(hairColor, highlightColor, hairHighlightInfluence);
                }

                // tt arbitrary darkening instead of using flow map
                hair *= 0.4f;

                var delta = Math.Min(Math.Max(normal.W - skinInfluence, 0), 1.0f);
                diffuse = Vector4.Lerp(diffuse, new Vector4(hair, 1.0f), delta);
                diffuseAlpha = 1.0f;
            }

            diffuseAlpha = set.IsTransparent
                               ? diffuseAlpha * alphaMultiplier
                               : (diffuseAlpha * alphaMultiplier < 1.0f ? 0.0f : 1.0f);


            if (skinType == SkinType.Face)
            {
                if (data.LipStick)
                {
                    diffuse = Vector4.Lerp(diffuse, lipColor, normal.W * lipColor.W);
                }

                roughness *= lipRoughnessScale;
            }

            diffuseTexture[x, y] = (diffuse with {W = diffuseAlpha}).ToSkColor();
            normalTexture[x, y] = (normal with {Z = 1.0f, W = 1.0f}).ToSkColor();
            metallicRoughnessTexture[x, y] = new Vector4(1.0f, roughness, metallic, 1.0f).ToSkColor();
            sssTexture[x, y] = new Vector4(sssThickness, sssThickness, sssThickness, 1).ToSkColor();
        });
        
        WithBaseColor(dataProvider.CacheTexture(diffuseTexture, $"Computed/{set.ComputedTextureName("diffuse")}"));
        WithNormal(dataProvider.CacheTexture(normalTexture, $"Computed/{set.ComputedTextureName("normal")}"));
        WithMetallicRoughness(dataProvider.CacheTexture(metallicRoughnessTexture, $"Computed/{set.ComputedTextureName("metallicRoughness")}"));
        WithVolumeThickness(dataProvider.CacheTexture(sssTexture, $"Computed/{set.ComputedTextureName("sss")}"), 1.0f);
    }
    
    public override MeddleMaterialBuilder Apply()
    {
       if (textureMode == TextureMode.Bake)
       {
           ApplyComputed();
       }
       else
       {
           ApplyRaw(set, dataProvider);
       }
       
       IndexOfRefraction = set.GetConstantOrDefault(MaterialConstant.g_GlassIOR, 1.0f);
       var alphaThreshold = set.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
       WithAlpha(AlphaMode.MASK, alphaThreshold);
       WithMetallicRoughnessShader();
       WithDoubleSide(set.RenderBackfaces);
       Extras = set.ComposeExtrasNode();
       
       return this;
    }
}
