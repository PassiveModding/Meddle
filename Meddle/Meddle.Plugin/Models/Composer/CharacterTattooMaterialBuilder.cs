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
        
        var normalTexture = set.GetTexture(dataProvider, TextureUsage.g_SamplerNormal).ToResource().ToTexture();
        var diffuseTexture = new SKTexture(normalTexture.Width, normalTexture.Height);
        for (var x = 0; x < normalTexture.Width; x++)
        for (var y = 0; y < normalTexture.Height; y++)
        {
            var normal = normalTexture[x, y].ToVector4();
            if (normal.Z != 0)
            {
                diffuseTexture[x, y] = new Vector4(color, normal.W).ToSkColor();
            }
            else
            {
                diffuseTexture[x, y] = new Vector4(0, 0, 0, normal.W).ToSkColor();
            }
        }

        WithBaseColor(dataProvider.CacheTexture(diffuseTexture, $"Computed/{set.ComputedTextureName("diffuse")}"));
        WithNormal(dataProvider.CacheTexture(normalTexture, $"Computed/{set.ComputedTextureName("normal")}"));
        
        WithDoubleSide(set.RenderBackfaces);
        
        var alpha = set.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alpha > 0)
        {
            WithAlpha(AlphaMode.BLEND, alpha);
        }
        else
        {
            WithAlpha(AlphaMode.BLEND);
        }
        
        Extras = set.ComposeExtrasNode();
        return this;
    }
}
