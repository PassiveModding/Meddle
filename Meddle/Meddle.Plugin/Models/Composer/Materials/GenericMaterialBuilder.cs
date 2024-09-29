using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
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
        var alphaThreshold = set.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            WithAlpha(AlphaMode.MASK, alphaThreshold);
        
        var setTypes = new HashSet<TextureUsage>();
        foreach (var textureUsage in set.TextureUsageDict)
        {
            var texData = dataProvider.LookupData(textureUsage.Value.FullPath);
            if (texData == null) continue;
            // caching the texture regardless of usage, but only applying it to the material if it's a known channel
            var texture = new TexFile(texData).ToResource().ToTexture();
            var tex = dataProvider.CacheTexture(texture, textureUsage.Value.FullPath);
            
            var channel = MapTextureUsageToChannel(textureUsage.Key);
            if (channel != null && setTypes.Add(textureUsage.Key))
            {
                WithChannelImage(channel.Value, tex);
            }
        }
        
        IndexOfRefraction = set.GetConstantOrDefault(MaterialConstant.g_GlassIOR, 1.0f);
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
