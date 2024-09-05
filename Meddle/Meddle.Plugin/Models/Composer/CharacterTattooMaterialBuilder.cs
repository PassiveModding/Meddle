using System.Numerics;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;

public class CharacterTattooMaterialBuilder : MeddleMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;
    private readonly CustomizeParameter customizeParameter;

    public CharacterTattooMaterialBuilder(string name, MaterialSet set, DataProvider dataProvider, CustomizeParameter customizeParameter) : base(name)
    {
        this.set = set;
        this.dataProvider = dataProvider;
        this.customizeParameter = customizeParameter;
    }

    public override MeddleMaterialBuilder Apply()
    {
        var hairType = set.GetShaderKeyOrDefault(ShaderCategory.CategoryHairType, (HairType)0);
        
        Vector3 color = hairType switch
        {
            HairType.Face => customizeParameter.OptionColor,
            HairType.Hair => customizeParameter.MeshColor,
            _ => Vector3.Zero
        };
        
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerNormal, out var normalRes))
            throw new InvalidOperationException("Missing normal texture");
        
        var normalTexture = normalRes.ToTexture();
        var diffuseTexture = new SKTexture(normalTexture.Width, normalTexture.Height);
        for (var x = 0; x < normalTexture.Width; x++)
        for (var y = 0; y < normalTexture.Height; y++)
        {
            var normal = normalTexture[x, y].ToVector4();
            var influence = normal.Z;
            
            if (influence > 0)
            {
                diffuseTexture[x, y] = new Vector4(color, normal.W).ToSkColor();
            }
            else
            {
                diffuseTexture[x, y] = new Vector4(0, 0, 0, normal.W).ToSkColor();
            }
            
            normalTexture[x, y] = (normal with { Z = 1.0f, W = 1.0f }).ToSkColor();
        }

        WithBaseColor(dataProvider.CacheTexture(diffuseTexture, $"Computed/{set.ComputedTextureName("diffuse")}"));
        WithNormal(dataProvider.CacheTexture(normalTexture, $"Computed/{set.ComputedTextureName("normal")}"));
        
        WithDoubleSide(set.RenderBackfaces);
        
        IndexOfRefraction = set.GetConstantOrThrow<float>(MaterialConstant.g_GlassIOR);
        var alphaThreshold = set.GetConstantOrThrow<float>(MaterialConstant.g_AlphaThreshold);
        WithAlpha(AlphaMode.BLEND, alphaThreshold);
        
        Extras = set.ComposeExtrasNode();
        return this;
    }
}
