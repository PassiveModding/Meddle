﻿using Meddle.Utils.Export;

namespace Meddle.Utils.Constants;

// Based on:
// https://github.com/Ottermandias/Penumbra.GameData/blob/33fea10e18ec9f8a5b309890de557fcb25780086/Files/ShaderStructs/Names.cs
public static class Names
{
    public interface ICrcPair
    {
        public string Value { get; }
        public uint Crc { get; }
    }
    
    // Used in the case we know the purpose of the crc but not the actual name, or already have the actual name
    public readonly struct StubName(string value, uint crc) : ICrcPair
    {
        public string Value { get; } = value;
        public uint Crc { get; } = crc;
    }
    
    public readonly struct Name(string value) : ICrcPair
    {
        public string Value { get; } = value;
        public uint Crc { get; } = Crc32.GetHash(value);

        public static implicit operator Name(string value) => new(value);
    }
    
    private static Dictionary<uint, ICrcPair>? Constants;
    public static Dictionary<uint, ICrcPair> GetConstants()
    {
        if (Constants == null)
        {
            var buffer = new Dictionary<uint, ICrcPair>();
            
            AddEnumConstants<MaterialConstant>();
            AddEnumConstants<ShaderCategory>();
            AddEnumConstants<BgVertexPaint>();
            AddEnumConstants<DiffuseAlpha>();
            AddEnumConstants<SkinType>();
            AddEnumConstants<HairType>();
            AddEnumConstants<FlowType>();
            AddEnumConstants<TextureMode>();
            AddEnumConstants<SpecularMode>();
            
            foreach (var constant in KnownConstants)
            {
                var name = new Name(constant);
                buffer[name.Crc] = name;
                foreach (var suffix in KnownSuffixes)
                {
                    var suffixedName = new Name($"{constant}{suffix}");
                    buffer[suffixedName.Crc] = suffixedName;
                }
            }

            Constants = buffer;
            
            void AddEnumConstants<T>() where T : struct, Enum
            {
                var values = Enum.GetValues<T>();
                foreach (var value in values)
                {
                    ICrcPair name = new Name(value.ToString());
                    if (name.Crc != (uint)(object)value)
                    {
                        name = new StubName(value.ToString(), (uint)(object)value);
                    }
                    
                    buffer[name.Crc] = name;
                }
            }
        }
        
        return Constants;
    }
    
    private static readonly IReadOnlyList<string> KnownConstants =
        [
            // Material constants
            "g_AlphaAperture",
            "g_AlphaMultiParam",
            "g_AlphaOffset",
            "g_AlphaThreshold",
            "g_AmbientOcclusionMask",
            "g_AngleClip",
            "g_BackScatterPower",
            "g_Color",
            "g_ColorUVScale",
            "g_DetailColor",
            "g_DetailColorUvScale",
            "g_DetailID",
            "g_DetailNormalScale",
            "g_DiffuseColor",
            "g_EmissiveColor",
            "g_EnvMapPower",
            "g_FarClip",
            "g_Fresnel",
            "g_FresnelValue0",
            "g_FurLength",
            "g_GlassIOR",
            "g_Gradation",
            "g_HairBackScatterRoughnessOffsetRate",
            "g_HairScatterColorShift",
            "g_HairSecondaryRoughnessOffsetRate",
            "g_HairSpecularBackScatterShift",
            "g_HairSpecularPrimaryShift",
            "g_HairSpecularSecondaryShift",
            "g_HeightMapScale",
            "g_HeightScale",
            "g_InclusionAperture",
            "g_Intensity",
            "g_IrisOptionColorRate",
            "g_IrisRingColor",
            "g_IrisRingEmissiveIntensity",
            "g_IrisRingForceColor",
            "g_IrisThickness",
            "g_LayerColor",
            "g_LayerDepth",
            "g_LayerIrregularity",
            "g_LayerScale",
            "g_LayerVelocity",
            "g_LightingType",
            "g_LipFresnelValue0",
            "g_LipRoughnessScale",
            "g_LipShininess",
            "g_MultiDetailColor",
            "g_MultiDiffuseColor",
            "g_MultiEmissiveColor",
            "g_MultiHeightScale",
            "g_MultiNormalScale",
            "g_MultiSpecularColor",
            "g_MultiSSAOMask",
            "g_MultiWaveScale",
            "g_MultiWhitecapDistortion",
            "g_MultiWhitecapScale",
            "g_NearClip",
            "g_NormalScale",
            "g_NormalScale1",
            "g_NormalUVScale",
            "g_OutlineColor",
            "g_OutlineWidth",
            "g_PrefersFailure",
            "g_Ray",
            "g_ReflectionPower",
            "g_RefractionColor",
            "g_ScatteringLevel",
            "g_ShaderID",
            "g_ShadowAlphaThreshold",
            "g_ShadowOffset",
            "g_ShadowPosOffset",
            "g_SheenAperture",
            "g_SheenRate",
            "g_SheenTintRate",
            "g_Shininess",
            "g_SpecularColor",
            "g_SpecularColorMask",
            "g_SpecularMask",
            "g_SpecularPower",
            "g_SpecularUVScale",
            "g_SSAOMask",
            "g_SubSurfacePower",
            "g_SubSurfaceProfileID",
            "g_SubSurfaceWidth",
            "g_TexAnim",
            "g_TextureMipBias",
            "g_TexU",
            "g_TexV",
            "g_TileAlpha",
            "g_TileScale",
            "g_ToonIndex",
            "g_ToonLightScale",
            "g_ToonReflectionScale",
            "g_ToonSpecIndex",
            "g_Transparency",
            "g_TransparencyDistance",
            "g_UseSubSurfaceRate",
            "g_WaveletDistortion",
            "g_WaveletNoiseParam",
            "g_WaveletOffset",
            "g_WaveletScale",
            "g_WaveSpeed",
            "g_WaveTime",
            "g_WaveTime1",
            "g_WhitecapColor",
            "g_WhitecapDistance",
            "g_WhitecapDistortion",
            "g_WhitecapNoiseScale",
            "g_WhitecapScale",
            "g_WhitecapSpeed",
            "g_WhiteEyeColor",

            // Shader functions
            "AddLayer",
            "ApplyAlphaClip",
            "ApplyAttenuation",
            "ApplyConeAttenuation",
            "ApplyDissolveColor",
            "ApplyDitherClip",
            "ApplyMaskTexture",
            "ApplyOmniShadow",
            "ApplyUnderWater",
            "ApplyVertexMovement",
            "ApplyWavelet",
            "ApplyWavingAnim",
            "ApplyWavingAnimation",
            "CalculateInstancingPosition",
            "ComputeSoftParticleAlpha",
            "DecodeDepthBuffer",
            "DrawOffscreen",
            "GeometryInstancing",
            "GetAmbientLight",
            "GetAmbientOcclusion",
            "GetColor",
            "GetCustumizeColorAura",
            "GetDecalColor",
            "GetDirectionalLight",
            "GetFakeSpecular",
            "GetHairFlow",
            "GetInstanceData",
            "GetLocalPosition",
            "GetMaterialValue",
            "GetNormalMap",
            "GetReflectColor",
            "GetRLR",
            "GetShadow",
            "GetSubColor",
            "GetUnderWaterLighting",
            "GetValues",
            "LightClip",
            "SelectOutput",
            "ShadowDistanceFadeType",
            "ShadowSoftShadowType",
            "SpecularLighting",
            "TransformProj",
            "TransformType",
            "TransformView",
            "Type",

            // Shader tables
            "ApplyFog_Table",
            "ApplyLightBufferType_Table",
            "ComputeFinalColorType_Table",
            "ComputeSoftParticleType_Table",
            "DepthOffsetType_Table",
            "DirectionalLight_Table",
            "DirectionalLightType_Table",
            "ForceFarZ_Table",
            "OutputType_Table",
            "PointLightCount_Table",
            "PointLightPositionType_Table",
            "PointLightType_Table",
            "TextureColor1_CalculateAlpha_Table",
            "TextureColor1_CalculateColor_Table",
            "TextureColor1_ColorToAlpha_Table",
            "TextureColor1_Decode_Table",
            "TextureColor1_Table",
            "TextureColor1_UvNo_Table",
            "TextureColor2_CalculateAlpha_Table",
            "TextureColor2_CalculateColor_Table",
            "TextureColor2_ColorToAlpha_Table",
            "TextureColor2_Decode_Table",
            "TextureColor2_Table",
            "TextureColor2_UvNo_Table",
            "TextureColor3_CalculateAlpha_Table",
            "TextureColor3_CalculateColor_Table",
            "TextureColor3_ColorToAlpha_Table",
            "TextureColor3_Decode_Table",
            "TextureColor3_Table",
            "TextureColor3_UvNo_Table",
            "TextureColor4_CalculateAlpha_Table",
            "TextureColor4_CalculateColor_Table",
            "TextureColor4_ColorToAlpha_Table",
            "TextureColor4_Decode_Table",
            "TextureColor4_Table",
            "TextureColor4_UvNo_Table",
            "TextureDistortion",
            "TextureDistortion_UvNo_Table",
            "TextureDistortion_UvSet0_Table",
            "TextureDistortion_UvSet1_Table",
            "TextureDistortion_UvSet2_Table",
            "TextureDistortion_UvSet3_Table",
            "TextureNormal_Table",
            "TextureNormal_UvNo_Table",
            "TexturePalette_Table",
            "TextureReflection_CalculateColor_Table",
            "TextureReflection_Table",
            "UvCompute0_Table",
            "UvCompute1_Table",
            "UvCompute2_Table",
            "UvCompute3_Table",
            "UvPrecisionType_Table",
            "UvSetCount_Table",

            // Other shader keys
            "Color",
            "Default",
            "DefaultTechnique",
            "Depth",
            "GeometryInstancingOff",
            "GeometryInstancingOn",
            "GetInstancingData_Bush",
            "GetNoInstancingData_Bush",
            "SUB_VIEW_CUBE_0",
            "SUB_VIEW_MAIN",
            "SUB_VIEW_ROOF",
            "SUB_VIEW_SHADOW_0",
            "SUB_VIEW_SHADOW_1",

            // Rendering passes
            "PASS_0",
            "PASS_7",
            "PASS_10",
            "PASS_12",
            "PASS_14",
            "PASS_COMPOSITE_OPAQUE",
            "PASS_COMPOSITE_SEMITRANSPARENCY",
            "PASS_COMPOSITE_SEMITRANSPARENCY_UNDER_WATER",
            "PASS_G_OPAQUE",
            "PASS_G_SEMITRANSPARENCY",
            "PASS_ID",
            "PASS_LIGHTING_OPAQUE",
            "PASS_LIGHTING_SEMITRANSPARENCY",
            "PASS_SEMITRANSPARENCY",
            "PASS_WATER",
            "PASS_WATER_Z",
            "PASS_WIREFRAME",
            "PASS_Z_OPAQUE",
        ];

    private static readonly IReadOnlyList<string> KnownSuffixes =
        [
            // Shader functions
            "_On",
            "_Off",
            "0",
            "1",
            "2",
            "Add",
            "Alpha",
            "Body",
            "BodyJJM",
            "Box",
            "Cascade",
            "CascadeWith",
            "CloudOnly",
            "Color",
            "Compatibility",
            "Depth",
            "Distance",
            "Face",
            "Face2",
            "FaceEmissive",
            "Hair",
            "Low",
            "Mask",
            "Mul",
            "None",
            "Normal",
            "Off",
            "On",
            "ParallaxOcclusion",
            "Plane",
            "PlaneFar",
            "PlaneNear",
            "ReflectivityRGB",
            "RGBA",
            "Rigid",
            "Simple",
            "Skin",
            "TerrainEadg",
            "WaterDepth",

            // Shader tables
            "_0",
            "_0_0",
            "_1",
            "_1_0",
            "_1_1",
            "_1x1",
            "_2",
            "_3",
            "_3x3",
            "_4",
            "_Add",
            "_Alpha",
            "_Apply",
            "_AutoPlacement",
            "_ByParameter",
            "_ByPixelPosition",
            "_Chara",
            "_Color",
            "_Cubic",
            "_Debug",
            "_Disable",
            "_Enable",
            "_Ex",
            "_FixedIntervalNDC",
            "_HalfLambert",
            "_High",
            "_INTZ_FETCH4",
            "_Lambert",
            "_Legacy",
            "_LerpWhite",
            "_Linear",
            "_Low",
            "_Map",
            "_MapChara",
            "_Max",
            "_Medium",
            "_Min",
            "_ModulateAlpha",
            "_Mul",
            "_None",
            "_NoneControl",
            "_Nothing",
            "_PerModel",
            "_PerPixel",
            "_Quadratic",
            "_RAWZ",
            "_Release",
            "_RGB",
            "_SH",
            "_Shadow",
            "_Shigemi",
            "_Sub",
            "_Table",
            "_Texture",
        ];
}
