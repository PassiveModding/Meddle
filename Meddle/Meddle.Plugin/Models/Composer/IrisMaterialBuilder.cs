using System.Numerics;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;

public class IrisMaterialBuilder : MeddleMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;
    private readonly CustomizeParameter parameters;

    public IrisMaterialBuilder(string name, MaterialSet set, DataProvider dataProvider, CustomizeParameter parameters) : base(name)
    {
        this.set = set;
        this.dataProvider = dataProvider;
        this.parameters = parameters;
    }

    private SKTexture GetCubeMap(int index)
    {
        var cubeMapData = dataProvider.LookupData("chara/common/texture/sphere_d_array.tex");
        if (cubeMapData == null)
            throw new InvalidOperationException("Missing cube map");
        var cubeMap = new TexFile(cubeMapData);
        return ImageUtils.GetTexData(cubeMap, index, 0, 0).ToTexture();
    }

    public override MeddleMaterialBuilder Apply()
    {
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerNormal, out var normalRes))
            throw new InvalidOperationException("Missing normal texture");
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerMask, out var maskRes))
            throw new InvalidOperationException("Missing mask texture");
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerDiffuse, out var diffuseRes))
            throw new InvalidOperationException("Missing diffuse texture");
        
        var normalTexture = normalRes.ToTexture();
        var maskTexture = maskRes.ToTexture(normalTexture.Size);
        var diffuseTexture = diffuseRes.ToTexture(normalTexture.Size);

        var sphereMapIndex = set.GetConstantOrThrow<float>(MaterialConstant.g_SphereMapIndex);
        var cubeMapTexture = GetCubeMap((int)sphereMapIndex).Resize(normalTexture.Width, normalTexture.Height);
        var whiteEyeColor = set.GetConstantOrThrow<Vector3>(MaterialConstant.g_WhiteEyeColor);
        var leftIrisColor = parameters.LeftColor;
        var emissiveTexture = new SKTexture(normalTexture.Width, normalTexture.Height);
        var specularTexture = new SKTexture(normalTexture.Width, normalTexture.Height);

        Partitioner.Iterate(normalTexture.Size, (x, y) =>
        {
            var normal = normalTexture[x, y].ToVector4();
            var mask = maskTexture[x, y].ToVector4();
            var diffuse = diffuseTexture[x, y].ToVector4();
            var cubeMap = cubeMapTexture[x, y].ToVector4();

            var irisMask = mask.Z;
            var whites = diffuse * new Vector4(whiteEyeColor, 1.0f);
            var iris = diffuse * (leftIrisColor with {W = 1.0f});
            diffuse = Vector4.Lerp(whites, iris, irisMask);

            // most textures this channel is just 0
            // use mask red as emissive mask
            emissiveTexture[x, y] = new Vector4(mask.X, mask.X, mask.X, 1.0f).ToSkColor();

            // use mask green as reflection mask/cubemap intensity
            var specular = new Vector4(cubeMap.X * mask.Y);
            specularTexture[x, y] = (specular with {W = 1.0f}).ToSkColor();

            diffuseTexture[x, y] = diffuse.ToSkColor();
            normalTexture[x, y] = (normal with {Z = 1.0f, W = 1.0f}).ToSkColor();
        });
        
        WithDoubleSide(set.RenderBackfaces);
        WithBaseColor(dataProvider.CacheTexture(diffuseTexture, $"Computed/{set.ComputedTextureName("diffuse")}"));
        WithNormal(dataProvider.CacheTexture(normalTexture, $"Computed/{set.ComputedTextureName("normal")}"));
        WithSpecularFactor(dataProvider.CacheTexture(specularTexture, $"Computed/{set.ComputedTextureName("specular")}"), 0.2f);
        WithSpecularColor(dataProvider.CacheTexture(specularTexture, $"Computed/{set.ComputedTextureName("specular")}"));
        WithEmissive(dataProvider.CacheTexture(emissiveTexture, $"Computed/{set.ComputedTextureName("emissive")}"));
        
        IndexOfRefraction = set.GetConstantOrThrow<float>(MaterialConstant.g_GlassIOR);
        var alphaThreshold = set.GetConstantOrThrow<float>(MaterialConstant.g_AlphaThreshold);
        if (alphaThreshold > 0)
            WithAlpha(AlphaMode.MASK, alphaThreshold);
        
        Extras = set.ComposeExtrasNode(
            ("leftIrisColor", leftIrisColor.AsFloatArray()), 
            ("rightIrisColor", parameters.RightColor.AsFloatArray())
        );
        return this;
    }
}
