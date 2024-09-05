﻿using System.Numerics;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;

public class HairMaterialBuilder : MeddleMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;
    private readonly CustomizeParameter parameters;

    public HairMaterialBuilder(string name, MaterialSet set, DataProvider dataProvider, CustomizeParameter parameters) : base(name)
    {
        this.set = set;
        this.dataProvider = dataProvider;
        this.parameters = parameters;
    }

    public override MeddleMaterialBuilder Apply()
    {
        var hairType = set.GetShaderKeyOrDefault(ShaderCategory.CategoryHairType, HairType.Hair);
        
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerNormal, out var normalRes))
            throw new InvalidOperationException("Missing normal texture");
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerMask, out var maskRes))
            throw new InvalidOperationException("Missing mask texture");

        var normalTexture = normalRes.ToTexture();
        var maskTexture = maskRes.ToTexture(normalTexture.Size);
        
        var hairColor = parameters.MainColor;
        var bonusColor = hairType switch
        {
            HairType.Face => parameters.OptionColor, // tattoo
            HairType.Hair => parameters.MeshColor, // hair highlight
            _ => hairColor
        };
        
        // TODO: Eyelashes should be black, possibly to do with vertex colors
        var diffuseTexture = new SKTexture(normalTexture.Width, normalTexture.Height);
        var metallicRoughnessTexture = new SKTexture(normalTexture.Width, normalTexture.Height);
        Partitioner.Iterate(normalTexture.Size, (x, y) =>
        {
            var normal = normalTexture[x, y].ToVector4();
            var mask = maskTexture[x, y].ToVector4();

            var bonusIntensity = normal.Z;
            var specular = mask.X;
            var roughness = mask.Y;
            var sssThickness = mask.Z;
            var metallic = 0.0f;
            var diffuseMaskOrAmbientOcclusion = mask.W;

            var diffusePixel = Vector3.Lerp(hairColor, bonusColor, bonusIntensity);

            metallicRoughnessTexture[x, y] = new Vector4(1.0f, roughness, metallic, 1.0f).ToSkColor();
            diffuseTexture[x, y] = new Vector4(diffusePixel, normal.W).ToSkColor();
            normalTexture[x, y] = (normal with {Z = 1.0f, W = 1.0f}).ToSkColor();
        });

        WithDoubleSide(set.RenderBackfaces);
        WithBaseColor(dataProvider.CacheTexture(diffuseTexture, $"Computed/{set.ComputedTextureName("diffuse")}"));
        WithNormal(dataProvider.CacheTexture(normalTexture, $"Computed/{set.ComputedTextureName("normal")}"));
        WithMetallicRoughness(dataProvider.CacheTexture(metallicRoughnessTexture, $"Computed/{set.ComputedTextureName("metallicRoughness")}"));
        WithAlpha(AlphaMode.MASK, set.GetConstantOrThrow<float>(MaterialConstant.g_AlphaThreshold));
        IndexOfRefraction = set.GetConstantOrThrow<float>(MaterialConstant.g_GlassIOR);
        
        Extras = set.ComposeExtrasNode(
            ("hairColor", hairColor.AsFloatArray()),
            ("bonusColor", bonusColor.AsFloatArray())
        );
        return this;
    }
}
