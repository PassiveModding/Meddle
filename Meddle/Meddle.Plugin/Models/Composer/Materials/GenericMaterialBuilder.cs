using Meddle.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Helpers;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer.Materials;

public class GenericMaterialBuilder : MeddleMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;

    public GenericMaterialBuilder(string name, MaterialSet set, DataProvider dataProvider) : base(name)
    {
        this.set = set;
        this.dataProvider = dataProvider;
    }
    
    public override MeddleMaterialBuilder Apply()
    {
        ApplyRaw(set, dataProvider);
        Extras = set.ComposeExtrasNode();
        return this;
    }
        
    public static KnownChannel? MapTextureUsageToChannel(TextureUsage usage)
    {
        return usage switch
        {
            TextureUsage.g_SamplerDiffuse => KnownChannel.BaseColor,
            TextureUsage.g_SamplerNormal => KnownChannel.Normal,
            TextureUsage.g_SamplerMask => KnownChannel.SpecularFactor,
            TextureUsage.g_SamplerSpecular => KnownChannel.SpecularColor,
            TextureUsage.g_SamplerCatchlight => KnownChannel.Emissive,
            TextureUsage.g_SamplerColorMap0 => KnownChannel.BaseColor,
            TextureUsage.g_SamplerNormalMap0 => KnownChannel.Normal,
            TextureUsage.g_SamplerSpecularMap0 => KnownChannel.SpecularColor,
            TextureUsage.g_SamplerColorMap1 => KnownChannel.BaseColor,
            TextureUsage.g_SamplerNormalMap1 => KnownChannel.Normal,
            TextureUsage.g_SamplerSpecularMap1 => KnownChannel.SpecularColor,
            TextureUsage.g_SamplerColorMap => KnownChannel.BaseColor,
            TextureUsage.g_SamplerNormalMap => KnownChannel.Normal,
            TextureUsage.g_SamplerSpecularMap => KnownChannel.SpecularColor,
            TextureUsage.g_SamplerNormal2 => KnownChannel.Normal,
            _ => null
        };
    }
}
