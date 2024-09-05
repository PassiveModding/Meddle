using System.Numerics;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;

public class HairMaterialBuilder : MeddleMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;
    private readonly CustomizeParameter parameters;

    public HairMaterialBuilder(string name, MaterialSet set, DataProvider dataProvider, CustomizeParameter parameters) : base(name)
    {
        this.set = set;
        this.dataProvider = dataProvider;
        this.parameters = parameters;
    }

    public override MeddleMaterialBuilder Apply()
    {
        var hairType = set.GetShaderKeyOrDefault(ShaderCategory.CategoryHairType, HairType.Hair);
        
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerNormal, out var normalRes))
            throw new InvalidOperationException("Missing normal texture");
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerMask, out var maskRes))
            throw new InvalidOperationException("Missing mask texture");

        var normalTexture = normalRes.ToTexture();
        var maskTexture = maskRes.ToTexture(normalTexture.Size);
        
        var hairColor = parameters.MainColor;
        var tattooColor = parameters.OptionColor;
        var highlightColor = parameters.MeshColor;
        
        var diffuseTexture = new SKTexture(normalTexture.Width, normalTexture.Height);
        var occ_x_x_x_Texture = new SKTexture(normalTexture.Width, normalTexture.Height);
        var vol_thick_x_x_Texture = new SKTexture(normalTexture.Width, normalTexture.Height);
        for (int x = 0; x < normalTexture.Width; x++)
        for (int y = 0; y < normalTexture.Height; y++)
        {
            var normal = normalTexture[x, y].ToVector4();
            var mask = maskTexture[x, y].ToVector4();

            var bonusColor = hairType switch
            {
                HairType.Face => tattooColor,
                HairType.Hair => highlightColor,
                _ => hairColor
            };
            
            var bonusIntensity = normal.Z;
            var diffusePixel = Vector3.Lerp(hairColor, bonusColor, bonusIntensity);
            var occlusion = mask.W * mask.W;
            
            diffuseTexture[x, y] = new Vector4(diffusePixel, normal.W).ToSkColor();
            occ_x_x_x_Texture[x, y] = new Vector4(occlusion, 0f, 0f, 1.0f).ToSkColor();
            normalTexture[x, y] = (normal with { Z = 1.0f, W = 1.0f }).ToSkColor();
            vol_thick_x_x_Texture[x, y] = new Vector4(mask.Z, mask.Z, mask.Z, 1.0f).ToSkColor();
        }

        WithDoubleSide(set.RenderBackfaces);
        WithBaseColor(dataProvider.CacheTexture(diffuseTexture, $"Computed/{set.ComputedTextureName("diffuse")}"));
        WithNormal(dataProvider.CacheTexture(normalTexture, $"Computed/{set.ComputedTextureName("normal")}"));
        WithOcclusion(dataProvider.CacheTexture(occ_x_x_x_Texture, $"Computed/{set.ComputedTextureName("occlusion")}"));
        WithMetallicRoughness(0, 1);
        WithAlpha(AlphaMode.BLEND, set.GetConstantOrThrow<float>(MaterialConstant.g_AlphaThreshold));
        IndexOfRefraction = set.GetConstantOrThrow<float>(MaterialConstant.g_GlassIOR);
        
        Extras = set.ComposeExtrasNode(
            ("hairColor", hairColor.AsFloatArray()),
            ("tattooColor", tattooColor.AsFloatArray()),
            ("highlightColor", highlightColor.AsFloatArray())
        );
        return this;
    }
}
