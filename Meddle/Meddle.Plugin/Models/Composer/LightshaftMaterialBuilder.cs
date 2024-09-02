using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Memory;

namespace Meddle.Plugin.Models.Composer;

public class LightshaftMaterialBuilder : InstanceMaterialBuilder
{
    private readonly MaterialSet set;

    public LightshaftMaterialBuilder(string name, MaterialSet set, Func<string, byte[]?> lookupFunc, Func<SKTexture, string, ImageBuilder> cacheFunc) : base(name, "lightshaft.shpk", lookupFunc, cacheFunc)
    {
        this.set = set;
    }

    public LightshaftMaterialBuilder WithLightShaft()
    {
        var sampler0 = set.TextureUsageDict[TextureUsage.g_Sampler0];
        var sampler1 = set.TextureUsageDict[TextureUsage.g_Sampler1];
        var texture0 = LookupFunc(sampler0);
        var texture1 = LookupFunc(sampler1);
        if (texture0 == null || texture1 == null)
        {
            return this;
        }

        var tex0 = new TexFile(texture0).ToResource();
        var tex1 = new TexFile(texture1).ToResource();
        
        var size = Vector2.Max(tex0.Size, tex1.Size);
        var res0 = tex0.ToTexture(size);
        var res1 = tex1.ToTexture(size);
        
        
        var outTexture = new SKTexture((int)size.X, (int)size.Y);
        set.TryGetConstant(MaterialConstant.g_Color, out Vector3 color);
        for (var x = 0; x < outTexture.Width; x++)
        for (var y = 0; y < outTexture.Height; y++)
        {
            var tex0Color = res0[x, y].ToVector4();
            var tex1Color = res1[x, y].ToVector4();
            var outColor = new Vector4(color, 1);
            
            outTexture[x, y] = (outColor * tex0Color * tex1Color).ToSkColor();
        }
        
        var fileName = $"Computed/{Path.GetFileNameWithoutExtension(set.MtrlPath)}_lightshaft_diffuse";
        var diffuseImage = CacheFunc(outTexture, fileName);
        //this.WithBaseColor(diffuseImage);
        this.WithBaseColor(new Vector4(1, 1, 1, 0));
        this.WithEmissive(diffuseImage);
        
        
        
        if (!set.TryGetConstant(MaterialConstant.g_AlphaThreshold, out float alphaThreshold))
        {
            alphaThreshold = 0.5f;
        }
        
        this.WithAlpha(AlphaMode.MASK, alphaThreshold);

        Extras = set.ComposeExtrasNode();
        
        return this;
    }
}
