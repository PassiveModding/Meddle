using System.Numerics;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using Meddle.Utils.Materials;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer.Materials;

public class CharacterMaterialBuilder : MeddleMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;
    private readonly IColorTableSet? colorTableSet;
    private readonly TextureMode textureMode;

    public CharacterMaterialBuilder(string name, MaterialSet set, DataProvider dataProvider, IColorTableSet? colorTableSet, TextureMode textureMode) : base(name)
    {
        this.set = set;
        this.dataProvider = dataProvider;
        this.colorTableSet = colorTableSet;
        this.textureMode = textureMode;
    }

    private void ApplyComputed()
    {
        var textureMode = set.GetShaderKeyOrDefault<Meddle.Utils.Constants.TextureMode>(ShaderCategory.GetValuesTextureType);
        
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerNormal, out var normalRes))
            throw new InvalidOperationException("Missing normal texture");
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerMask, out var maskRes))
            throw new InvalidOperationException("Missing mask texture");
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerIndex, out var indexRes))
            throw new InvalidOperationException("Missing index texture");

        var diffuseTexture = textureMode switch
        {
            Meddle.Utils.Constants.TextureMode.Compatibility => set.TryGetTexture(dataProvider, TextureUsage.g_SamplerDiffuse, out var tex) ? tex.ToTexture(normalRes.Size) : throw new InvalidOperationException("Missing diffuse texture"),
            _ => new SKTexture(normalRes.Width, normalRes.Height)
        };

        var normalTexture = normalRes.ToTexture();
        var maskTexture = maskRes.ToTexture(normalRes.Size);
        var indexTexture = indexRes.ToTexture(normalRes.Size);
        var occlusionTexture = new SKTexture(normalRes.Width, normalRes.Height);
        var metallicRoughness = new SKTexture(normalRes.Width, normalRes.Height);
        Partitioner.Iterate(normalTexture.Size, (x, y) =>
        {
            var normal = normalTexture[x, y].ToVector4();
            var mask = maskTexture[x, y].ToVector4();
            var indexColor = indexTexture[x, y];

            var blended = ((ColorTableSet?)colorTableSet)!.Value.ColorTable
                                .GetBlendedPair(indexColor.Red, indexColor.Green);
            if (textureMode == Meddle.Utils.Constants.TextureMode.Compatibility)
            {
                var diffuse = diffuseTexture![x, y].ToVector4();
                diffuse *= new Vector4(blended.Diffuse, normal.Z);
                diffuseTexture[x, y] = (diffuse with {W = normal.Z}).ToSkColor();
            }
            else if (textureMode == Meddle.Utils.Constants.TextureMode.Default)
            {
                var diffuse = new Vector4(blended.Diffuse, normal.Z);
                diffuseTexture[x, y] = diffuse.ToSkColor();
            }
            else
            {
                throw new InvalidOperationException($"Unknown texture mode {textureMode}");
            }

            /*var spec = blended.Specular;
            var specStrength = blended.SpecularStrength;
            if (specularMode == SpecularMode.Mask)
            {
                var diffuseMask = mask.X;
                var specMask = mask.Y;
                var roughMask = mask.Z;
                metallicRoughness[x, y] = new Vector4(specMask, roughMask, 0, 1).ToSkColor();
            }*/

            normalTexture[x, y] = (normal with {Z = 1.0f, W = 1.0f}).ToSkColor();
        });

        WithBaseColor(dataProvider.CacheTexture(diffuseTexture, $"Computed/{set.ComputedTextureName("diffuse")}"));
        WithNormal(dataProvider.CacheTexture(normalTexture, $"Computed/{set.ComputedTextureName("normal")}"));
        
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
        
        IndexOfRefraction = set.GetConstantOrThrow<float>(MaterialConstant.g_GlassIOR);
            
        var alphaThreshold = set.GetConstantOrDefault(MaterialConstant.g_AlphaThreshold, 0.0f);
        if (alphaThreshold > 0)
            WithAlpha(AlphaMode.MASK, alphaThreshold);
        
        WithDoubleSide(set.RenderBackfaces);
        
        
        Extras = set.ComposeExtrasNode();
        return this;
    }
}
