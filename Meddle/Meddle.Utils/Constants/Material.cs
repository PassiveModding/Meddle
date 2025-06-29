using System.Diagnostics.CodeAnalysis;
// ReSharper disable InconsistentNaming

namespace Meddle.Utils.Constants;

public enum ShaderCategory : uint
{
    CategorySkinType = 0x380CAED0,
    CategoryHairType = 0x24826489,
    GetValuesTextureType = 0xB616DC5A,  // DEFAULT, COMPATIBILITY, SIMPLE
    CategorySpecularType = 0xC8BD1DEF, // MASK, DEFAULT
    CategoryFlowMapType = 0x40D1481E,  // STANDARD, FLOW
    CategoryDiffuseAlpha = 0xA9A3EE25, // Alpha channel on diffuse texture is used
    CategoryBgVertexPaint = 0x4F4F0636, // Enable vertex paint
}

public enum BgVertexPaint : uint
{
    Off = 0x7C6FA05B, // Default off
    On = 0xBD94649A // R/G faux wind influence/multiplier
}

public enum DiffuseAlpha : uint
{
    Default = 0x0,
    UseDiffuseAlphaAsOpacity = 0x72AAA9AE
}

public enum SkinType : uint
{
    Body = 0x2BDB45F1,
    Face = 0xF5673524,
    Hrothgar = 0x57FF3B64,
    Default = 0x0
}

public enum HairType : uint
{
    Face = 0x6E5B8F10,
    Hair = 0xF7B8956E
}

public enum FlowType : uint
{
    Standard = 0x337C6BC4, // No flow?
    Flow = 0x71ADA939
}

public enum TextureMode : uint
{
    Default = 0x5CC605B5,       // Default mask texture
    Compatibility = 0x600EF9DF, // Used to enable diffuse texture
    Simple = 0x22A4AABF,        // meh
    
    // BG.shpk
    BG_UNK0 = 0x669A451B, // no map1 usage
    BG_UNK1 = 0x1DF2985C, 
    BG_UNK2 = 0x941820BE, // Mix both textures? 
}

public enum SpecularMode : uint
{
    Mask = 0xA02F4828,   // Use mask sampler for specular
    Default = 0x198D11CD // Use spec sampler for specular
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum MaterialConstant : uint
{
    g_AlphaThreshold = 0x29AC0223,
    g_ShaderID = 0x59BDA0B1, // ??? Set to 0: disable SSS, add a metallic effect. Set to 1: SSS enabled. Set to 6 (hroth) disable SSS, fur parallax enabled
    g_DiffuseColor = 0x2C2A34DD,
    g_SpecularColor = 0x141722D5,
    g_SpecularColorMask = 0xCB0338DC,
    g_LipRoughnessScale = 0x3632401A,
    g_WhiteEyeColor = 0x11C90091,
    g_SphereMapIndex = 0x074953E9, // array index for chara/common/texture/sphere_d_array.tex
    g_EmissiveColor = 0x38A64362,
    g_SSAOMask = 0xB7FA33E2,
    g_TileIndex = 0x4255F2F4,
    g_TileScale = 0x2E60B071,
    g_TileAlpha = 0x12C6AC9F,
    g_NormalScale = 0xB5545FBB,
    g_SheenRate = 0x800EE35F,
    g_SheenTintRate = 0x1F264897,
    g_SheenAperture = 0xF490F76E,
    g_IrisRingColor = 0x50E36D56, // doesn't appear to do anything
    g_IrisRingEmissiveIntensity = 0x7DABA471,
    g_IrisThickness = 0x66C93D3E, // SSS Thickness on eyes?
    g_IrisOptionColorRate = 0x29253809,
    g_AlphaAperture = 0xD62BF368,
    g_AlphaOffset = 0xD07A6A65,
    g_GlassIOR = 0x7801E004,
    g_GlassThicknessMax = 0xC4647F37,
    g_TextureMipBias = 0x39551220,
    g_OutlineColor = 0x623CC4FE,
    g_OutlineWidth = 0x8870C938,
    g_Ray = 0x827BDD09,
    g_TexU = 0x5926A043,
    g_TexV = 0xC02FF1F9,
    g_TexAnim = 0x14D8E13D,
    g_Color = 0xD27C58B9,
    g_ShadowAlphaThreshold = 0xD925FF32,
    g_NearClip = 0x17A52926,
    g_AngleClip = 0x71DBDA81,
    g_CausticsReflectionPowerBright = 0x0CC09E67, 
    g_CausticsReflectionPowerDark = 0xC295EA6C, 
    
    g_HeightScale = 0x8F8B0070, 
    g_HeightMapScale = 0xA320B199, 
    g_HeightMapUVScale = 0x5B99505D, 
    g_MultiWaveScale = 0x37363FDD, 
    g_WaveSpeed = 0xE4C68FF3, 
    g_WaveTime = 0x8EB9D2A6, 
    g_AlphaMultiParam = 0x07EDA444, 
    g_AmbientOcclusionMask = 0x575ABFB2, 
    g_ColorUVScale = 0xA5D02C52, 
    
    g_DetailID = 0x8981D4D9, // Index into bgcommon/nature/detail/texture/detail_d_array.tex and bgcommon/nature/detail/texture/detail_n_array.tex
    g_DetailNormalScale = 0x9F42EDA2, 
    g_DetailColorUvScale = 0xC63D9716,
    g_DetailColor = 0xDD93D839, 
    g_DetailNormalUvScale = 0x025A9BEE, 
    
    g_EnvMapPower = 0xEEF5665F, 
    g_FresnelValue0 = 0x62E44A4F, 
    g_InclusionAperture = 0xBCA22FD4, 
    g_IrisRingForceColor = 0x58DE06E2, // seems to adjust the colour or specular of the iris ring
    g_LayerDepth = 0xA9295FEF, 
    g_LayerIrregularity = 0x0A00B0A1, 
    g_LayerScale = 0xBFCC6602, 
    g_LayerVelocity = 0x72181E22, 
    g_LipFresnelValue0 = 0x174BB64E, 
    g_LipShininess = 0x878B272C, 
    g_MultiDetailColor = 0x11FD4221, 
    g_MultiDiffuseColor = 0x3F8AC211, 
    g_MultiEmissiveColor = 0xAA676D0F, 
    g_MultiHeightScale = 0x43E59A68, 
    g_MultiNormalScale = 0x793AC5A3, 
    g_MultiSpecularColor = 0x86D60CB8, 
    g_MultiSSAOMask = 0x926E860D, 
    g_MultiWhitecapDistortion = 0x93504F3B, 
    g_MultiWhitecapScale = 0x312B69C1, 
    g_NormalScale1 = 0x0DD83E61, 
    g_NormalUVScale = 0xBB99CF76, 
    g_PrefersFailure = 0x5394405B, 
    g_ReflectionPower = 0x223A3329, 
    g_ScatteringLevel = 0xB500BB24, 
    g_ShadowOffset = 0x96D2B53D, 
    g_ShadowPosOffset = 0x5351646E, 
    g_SpecularMask = 0x36080AD0, 
    g_SpecularPower = 0xD9CB6B9C, 
    g_SpecularUVScale = 0x8D03A782, 
    g_ToonIndex = 0xDF15112D, 
    g_ToonLightScale = 0x3CCE9E4C, 
    g_ToonReflectionScale = 0xD96FAF7A, 
    g_ToonSpecIndex = 0x00A680BC, 
    g_TransparencyDistance = 0x1624F841, 
    g_WaveletDistortion = 0x3439B378, 
    g_WaveletNoiseParam = 0x1279815C, 
    g_WaveletOffset = 0x9BE8354A, 
    g_WaveletScale = 0xD62C681E, 
    g_WaveTime1 = 0x6EE5BF35, 
    g_WhitecapDistance = 0x5D26B262, 
    g_WhitecapDistortion = 0x61053025, 
    g_WhitecapNoiseScale = 0x0FF95B0C, 
    g_WhitecapScale = 0xA3EA47AC, 
    g_WhitecapSpeed = 0x408A9CDE, 
    g_Fresnel = 0xE3AA427A, 
    g_Gradation = 0x94B40EEE, 
    g_Intensity = 0xBCBA70E1, 
    g_Shininess = 0x992869AB, 
    g_LayerColor = 0x35DC0B6F, 
    g_RefractionColor = 0xBA163700, 
    g_WhitecapColor = 0x29FA2AC1, 
    
    // The following are have unknown names but their usage is generally known
    unk_LimbalRingRange = 0xE18398AE, // from centre of iris, the start and end point of the limbal ring
    unk_LimbalRingFade = 0x5B608CFE, // inner and outer fade of limbal ring
    unk_IrisParallaxDepth = 0x37DEA328, // Iris parallax depth (needs to be >0) 
    unk_IrisEmissiveOverride = 0x8EA14846, // Override iris emissive color with feature color
    unk_IrisEmissiveOverrideOpacity = 0x7918D232, // Opacity of the Feature Color Emissive Override.
    unk_TileSharpening = 0x6421DD30, // Positive value "smoothes" the texture, negative value "sharpens" the texture
    unk_WaterColor = 0xD315E728, // Water color
}
