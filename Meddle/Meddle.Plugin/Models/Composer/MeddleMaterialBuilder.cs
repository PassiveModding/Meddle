using Meddle.Utils;
using Meddle.Utils.Files;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;

public abstract class MeddleMaterialBuilder : MaterialBuilder
{
    public MeddleMaterialBuilder(string name) : base(name)
    {
    }
    
    public abstract MeddleMaterialBuilder Apply();

    protected void SaveAllTextures(MaterialSet set, DataProvider provider)
    {
        foreach (var textureUsage in set.TextureUsageDict)
        {
            var texData = provider.LookupData(textureUsage.Value);
            if (texData == null) continue;
            var texture = new TexFile(texData).ToResource().ToTexture();
            provider.CacheTexture(texture, textureUsage.Value);
        }
    }
}
