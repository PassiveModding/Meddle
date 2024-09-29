using Meddle.Utils;
using Meddle.Utils.Materials;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer.Materials;

public abstract class MeddleMaterialBuilder : MaterialBuilder, IVertexPaintMaterialBuilder
{
    public MeddleMaterialBuilder(string name) : base(name)
    {
        IndexOfRefraction = 1.0f;
    }
    
    public abstract MeddleMaterialBuilder Apply();
    
    internal void ApplyRaw(MaterialSet set, DataProvider dataProvider)
    {
        VertexPaint = true;
        
        var hashStr = set.HashStr();
        foreach (var texture in set.TextureUsageDict)
        {
            if (set.TryGetTexture(dataProvider, texture.Key, out var tex))
            {
                var texName = $"{hashStr}/{set.ComputedTextureName(texture.Value.GamePath)}";
                var builder = dataProvider.CacheTexture(tex.ToTexture(), texName);
                var mapped = GenericMaterialBuilder.MapTextureUsageToChannel(texture.Key);
                if (mapped != null)
                {
                    WithChannelImage(mapped.Value, builder);
                }
            }
        }
    }

    public bool VertexPaint { get; internal set; }
}
