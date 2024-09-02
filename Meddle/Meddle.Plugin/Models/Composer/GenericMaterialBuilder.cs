using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;

public class GenericMaterialBuilder : InstanceMaterialBuilder
{
    private readonly MaterialSet set;
    
    public GenericMaterialBuilder(string name, MaterialSet set, Func<string, byte[]?> lookupFunc, Func<SKTexture, string, ImageBuilder> cacheFunc) : base(name, set.ShpkName, lookupFunc, cacheFunc)
    {
        this.set = set;
    }
    
    public GenericMaterialBuilder WithGeneric()
    {
        var alphaThreshold = set.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            WithAlpha(AlphaMode.MASK, alphaThreshold);
        
        var texturePaths = set.File.GetTexturePaths();
        var setTypes = new HashSet<TextureUsage>();
        foreach (var sampler in set.File.Samplers)
        {
            if (sampler.TextureIndex == byte.MaxValue) continue;
            var textureInfo = set.File.TextureOffsets[sampler.TextureIndex];
            var texturePath = texturePaths[textureInfo.Offset];
            // bg textures can have additional textures, which may be dummy textures, ignore them
            if (texturePath.Contains("dummy_")) continue;
            if (!set.Package.TextureLookup.TryGetValue(sampler.SamplerId, out var usage))
            {
                continue;
            }
            var texData = LookupFunc(texturePath);
            if (texData == null) continue;
            var texture = new TexFile(texData).ToResource().ToTexture();
            var tex = CacheFunc(texture, texturePath);
            var channel = MaterialUtility.MapTextureUsageToChannel(usage);
            if (channel != null && setTypes.Add(usage))
            {
                WithChannelImage(channel.Value, tex);
            }
        }

        Extras = set.ComposeExtrasNode();
        
        return this;
    }
}
