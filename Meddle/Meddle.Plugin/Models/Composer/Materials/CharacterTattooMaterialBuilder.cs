﻿using System.Numerics;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Helpers;
using Meddle.Utils.Materials;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer.Materials;

public class CharacterTattooMaterialBuilder : MeddleMaterialBuilder
{
    private readonly MaterialSet set;
    private readonly DataProvider dataProvider;
    private readonly CustomizeParameter customizeParameter;
    private readonly TextureMode textureMode;

    public CharacterTattooMaterialBuilder(
        string name, MaterialSet set, DataProvider dataProvider, CustomizeParameter customizeParameter,
        TextureMode textureMode) : base(name)
    {
        this.set = set;
        this.dataProvider = dataProvider;
        this.customizeParameter = customizeParameter;
        this.textureMode = textureMode;
    }

    private void ApplyComputed()
    {
        var influenceColor = customizeParameter.OptionColor;
        if (!set.TryGetTextureStrict(dataProvider, TextureUsage.g_SamplerNormal, out var normalRes))
            throw new InvalidOperationException("Missing normal texture");
        
        var normalTexture = normalRes.ToTexture();
        var diffuseTexture = new SKTexture(normalTexture.Width, normalTexture.Height);
        Partitioner.Iterate(normalTexture.Size, (x, y) =>
        {
            var normal = normalTexture[x, y].ToVector4();
            var influence = normal.Z;

            if (influence > 0)
            {
                diffuseTexture[x, y] = new Vector4(influenceColor, normal.W).ToSkColor();
            }
            else
            {
                diffuseTexture[x, y] = new Vector4(0, 0, 0, normal.W).ToSkColor();
            }

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
        
        WithDoubleSide(set.RenderBackfaces);
        WithAlpha(AlphaMode.BLEND, set.GetConstantOrThrow<float>(MaterialConstant.g_AlphaThreshold));
        IndexOfRefraction = set.GetConstantOrThrow<float>(MaterialConstant.g_GlassIOR);
        WithDoubleSide(set.RenderBackfaces);
        Extras = set.ComposeExtrasNode();
        
        return this;
    }
}
