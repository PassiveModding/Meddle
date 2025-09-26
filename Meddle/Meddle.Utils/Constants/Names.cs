using Meddle.Utils.Export;

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
    
    public readonly struct SuffixedName(string value) : ICrcPair
    {
        public string Value { get; } = value;
        public uint Crc { get; } = Crc32.GetHash(value);

        public static implicit operator SuffixedName(string value) => new(value);
    }
    
    public readonly struct Name(string value) : ICrcPair
    {
        public string Value { get; } = value;
        public uint Crc { get; } = Crc32.GetHash(value);

        public static implicit operator Name(string value) => new(value);
    }
    
    private static Dictionary<uint, ICrcPair>? Constants;
    public static string TryResolveName(uint crc)
    {
        var constants = GetConstants();
        if (constants.TryGetValue(crc, out var name))
        {
            return $"{name.Value} ({name.Crc:X8})";
        }
        
        return $"Unknown constant ({crc:X8})";
    }
    
    public static Dictionary<uint, ICrcPair> GetConstants()
    {
        if (Constants == null)
        {
            var buffer = new Dictionary<uint, ICrcPair>();
            
            foreach (var constant in FoundConstants)
            {
                var name = new Name(constant);
                buffer[name.Crc] = name;
            }

            foreach (var constant in FoundConstants)
            {
                foreach (var suffix in KnownSuffixes)
                {
                    var suffixedName = new SuffixedName($"{constant}{suffix}");
                    buffer.TryAdd(suffixedName.Crc, suffixedName);
                }
            }
            
            AddEnumConstants<MaterialConstant>();
            AddEnumConstants<ShaderCategory>();
            AddEnumConstants<BgVertexPaint>();
            AddEnumConstants<DiffuseAlpha>();
            AddEnumConstants<SkinType>();
            AddEnumConstants<HairType>();
            AddEnumConstants<FlowType>();
            AddEnumConstants<TextureMode>();
            AddEnumConstants<SpecularMode>();

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
                    
                    // Using tryadd because we dont want to override the name if it already exists
                    buffer.TryAdd(name.Crc, name);
                }
            }
        }
        
        return Constants;
    }

    private static readonly IReadOnlyList<string> FoundConstants =
    [
        "AddLayer",                                    // 0xEA931ECA
        "ApplyAlpha",                                  // 0x9AB3A530
        "ApplyAlphaClip",                              // 0xDCFC844E
        "ApplyAttenuation",                            // 0x53AF00ED
        "ApplyCone",                                   // 0x0EA9B350
        "ApplyConeAttenuation",                        // 0x52D21D34
        "ApplyDepth",                                  // 0xB0F08033
        "ApplyDepthWhitecap",                          // 0x28981633
        "ApplyDepthWhitecapOn",                        // 0xDD54E76C
        "ApplyDetail",                                 // 0x77F07B68
        "ApplyDetailMap",                              // 0x6313FD87
        "ApplyDissolve",                               // 0xCBA0B4BE
        "ApplyDissolveColor",                          // 0xAD24ACAD
        "ApplyDither",                                 // 0xEB6BDF89
        "ApplyDitherClip",                             // 0x8B036665
        "ApplyDynamicWave",                            // 0xE5D84BEF
        "ApplyFog_Table",                              // 0xF4DA16D6
        "ApplyLightBufferType_Table",                  // 0x82730F4C
        "ApplyMaskTexture",                            // 0xF66E4589
        "ApplyOmniShadow",                             // 0x51668572
        "ApplyUnderWater",                             // 0x7725989B
        "ApplyVertexColor",                            // 0x4F4F0636
        "ApplyVertexColorOn",                          // 0xBD94649A
        "ApplyVertexMovement",                         // 0x87D8F48A
        "ApplyWavelet",                                // 0xFB7AD5E4
        "ApplyWaveletOn",                              // 0x1140F45C
        "ApplyWavingAnim",                             // 0x105C6A52
        "ApplyWavingAnimation",                        // 0x764AECE7
        "ApplyWavingAnimOn",                           // 0xF801B859
        "CalculateInstancingPosition",                 // 0x4518960B
        "Color",                                       // 0x61B590F0
        "ComputeFinalColorType_Table",                 // 0xA19D807E
        "ComputeSoftParticleAlpha",                    // 0x52DCF96D
        "ComputeSoftParticleType_Table",               // 0x1F6F0483
        "DebugMode",                                   // 0x611DA1BE
        "DebugVertexWave",                             // 0x7D155D6D
        "DecodeDepthBuffer",                           // 0x2C6C023C
        "Default",                                     // 0xB18FE63D
        "DefaultTechnique",                            // 0x86CA5FE4
        "Depth",                                       // 0xFD40C470
        "DepthOffsetType_Table",                       // 0x152531DE
        "DirectionalLight_Table",                      // 0x907B83D2
        "DirectionalLightType_Table",                  // 0x8FF4ACEB
        "DrawDepth",                                   // 0xABAD7ABE
        "DrawDepthMode",                               // 0xE8DA5B62
        "DrawOffscreen",                               // 0xA1CDEFE9
        "Enable",                                      // 0x45CE6CA7
        "EnableLighting",                              // 0x0033C8B5
        "EnableLightingOff",                           // 0x93D6C21A
        "EnableLightingOn",                            // 0xD1E60FD9
        "ForceFarZ_Table",                             // 0x2BD1F674
        "g_AlphaAperture",                             // 0xD62BF368
        "g_AlphaMultiParam",                           // 0x07EDA444
        "g_AlphaMultiWeight",                          // 0x30B91319
        "g_AlphaOffset",                               // 0xD07A6A65
        "g_AlphaThreshold",                            // 0x29AC0223
        "g_AmbientOcclusionMask",                      // 0x575ABFB2
        "g_AngleClip",                                 // 0x71DBDA81
        "g_BackScatterPower",                          // 0xCB4383FC
        "g_CausticsPower",                             // 0x7071F15D
        "g_CausticsReflectionPowerBright",             // 0x0CC09E67
        "g_CausticsReflectionPowerDark",               // 0xC295EA6C
        "g_Color",                                     // 0xD27C58B9
        "g_ColorUVScale",                              // 0xA5D02C52
        "g_DetailColor",                               // 0xDD93D839
        "g_DetailColorFadeDistance",                   // 0xF3F28C58
        "g_DetailColorMipBias",                        // 0xB10AF2DA
        "g_DetailColorUvScale",                        // 0xC63D9716
        "g_DetailID",                                  // 0x8981D4D9
        "g_DetailNormalScale",                         // 0x9F42EDA2
        "g_DetailNormalUvScale",                       // 0x025A9BEE
        "g_DiffuseColor",                              // 0x2C2A34DD
        "g_EmissiveColor",                             // 0x38A64362
        "g_EnableLightShadow",                         // 0x5095E770
        "g_EnableShadow",                              // 0xBCEA8C11
        "g_EnvMapPower",                               // 0xEEF5665F
        "g_FadeDistance",                              // 0xC7D0DB1A
        "g_FarClip",                                   // 0xFF334651
        "g_Fresnel",                                   // 0xE3AA427A
        "g_FresnelValue0",                             // 0x62E44A4F
        "g_FurLength",                                 // 0xB42C4022
        "g_GlassIOR",                                  // 0x7801E004
        "g_GlassThicknessMax",                         // 0xC4647F37
        "g_Gradation",                                 // 0x94B40EEE
        "g_HairBackScatterRoughnessOffsetRate",        // 0x63F02C01
        "g_HairScatterColorShift",                     // 0xA9A9659B
        "g_HairSecondaryRoughnessOffsetRate",          // 0x6ECF00B5
        "g_HairSpecularBackScatterShift",              // 0x44921F03
        "g_HairSpecularPrimaryShift",                  // 0x61928F58
        "g_HairSpecularSecondaryShift",                // 0x539BCB4B
        "g_HeightMapScale",                            // 0xA320B199
        "g_HeightMapUVScale",                          // 0x5B99505D
        "g_HeightScale",                               // 0x8F8B0070
        "g_InclusionAperture",                         // 0xBCA22FD4
        "g_Intensity",                                 // 0xBCBA70E1
        "g_IrisOptionColorRate",                       // 0x29253809
        "g_IrisRingColor",                             // 0x50E36D56
        "g_IrisRingEmissiveIntensity",                 // 0x7DABA471
        "g_IrisRingForceColor",                        // 0x58DE06E2
        "g_IrisThickness",                             // 0x66C93D3E
        "g_LayerColor",                                // 0x35DC0B6F
        "g_LayerDepth",                                // 0xA9295FEF
        "g_LayerIrregularity",                         // 0x0A00B0A1
        "g_LayerScale",                                // 0xBFCC6602
        "g_LayerVelocity",                             // 0x72181E22
        "g_LightingType",                              // 0x2FEADDF4
        "g_LipFresnelValue0",                          // 0x174BB64E
        "g_LipRoughnessScale",                         // 0x3632401A
        "g_LipShininess",                              // 0x878B272C
        "g_MultiDetailColor",                          // 0x11FD4221
        "g_MultiDetailID",                             // 0xAC156136
        "g_MultiDetailNormalScale",                    // 0xA83DBDF1
        "g_MultiDiffuseColor",                         // 0x3F8AC211
        "g_MultiEmissiveColor",                        // 0xAA676D0F
        "g_MultiHeightScale",                          // 0x43E59A68
        "g_MultiNormalScale",                          // 0x793AC5A3
        "g_MultiSpecularColor",                        // 0x86D60CB8
        "g_MultiSSAOMask",                             // 0x926E860D
        "g_MultiWaveScale",                            // 0x37363FDD
        "g_MultiWhitecapDistortion",                   // 0x93504F3B
        "g_MultiWhitecapScale",                        // 0x312B69C1
        "g_NearClip",                                  // 0x17A52926
        "g_NormalScale",                               // 0xB5545FBB
        "g_NormalScale1",                              // 0x0DD83E61
        "g_NormalUVScale",                             // 0xBB99CF76
        "g_OutlineColor",                              // 0x623CC4FE
        "g_OutlineWidth",                              // 0x8870C938
        "g_PrefersFailure",                            // 0x5394405B
        "g_Ray",                                       // 0x827BDD09
        "g_ReflectionPower",                           // 0x223A3329
        "g_RefractionColor",                           // 0xBA163700
        "g_RLRReflectionPower",                        // 0xF2360709
        "g_ScatteringLevel",                           // 0xB500BB24
        "g_ShaderID",                                  // 0x59BDA0B1
        "g_ShadowAlphaThreshold",                      // 0xD925FF32
        "g_ShadowOffset",                              // 0x96D2B53D
        "g_ShadowPosOffset",                           // 0x5351646E
        "g_SheenAperture",                             // 0xF490F76E
        "g_SheenRate",                                 // 0x800EE35F
        "g_SheenTintRate",                             // 0x1F264897
        "g_Shininess",                                 // 0x992869AB
        "g_SingleWhitecapDistortion",                  // 0x97E83104
        "g_SingleWhitecapScale",                       // 0xB33DB142
        "g_SoftEadgDistance",                          // 0x2A57C3CC
        "g_SpecularColor",                             // 0x141722D5
        "g_SpecularColorMask",                         // 0xCB0338DC
        "g_SpecularMask",                              // 0x36080AD0
        "g_SpecularPower",                             // 0xD9CB6B9C
        "g_SpecularUVScale",                           // 0x8D03A782
        "g_SphereMapID",                               // 0x5106E045
        "g_SphereMapIndex",                            // 0x074953E9
        "g_SSAOMask",                                  // 0xB7FA33E2
        "g_SubSurfacePower",                           // 0xC159D2A6
        "g_SubSurfaceProfileID",                       // 0x2CDAA167
        "g_SubSurfaceWidth",                           // 0xE6C99629
        "g_TexAnim",                                   // 0x14D8E13D
        "g_TextureMipBias",                            // 0x39551220
        "g_TexU",                                      // 0x5926A043
        "g_TexV",                                      // 0xC02FF1F9
        "g_TileAlpha",                                 // 0x12C6AC9F
        "g_TileIndex",                                 // 0x4255F2F4
        "g_TileScale",                                 // 0x2E60B071
        "g_ToonIndex",                                 // 0xDF15112D
        "g_ToonLightScale",                            // 0x3CCE9E4C
        "g_ToonReflectionScale",                       // 0xD96FAF7A
        "g_ToonSpecIndex",                             // 0x00A680BC
        "g_Transparency",                              // 0x53E8417B
        "g_TransparencyDistance",                      // 0x1624F841
        "g_TripleWhitecapDistortion",                  // 0x960450D0
        "g_TripleWhitecapScale",                       // 0x113BAFDF
        "g_UseSubSurfaceRate",                         // 0xD7919CB2
        "g_UVScrollTime",                              // 0x9A696A17
        "g_VertexMovementScale",                       // 0x641E0F22
        "g_Water",                                     // 0x4F19048A
        "g_WaveletDistortion",                         // 0x3439B378
        "g_WaveletFadeDistance",                       // 0x4AD899B7
        "g_WaveletNoiseParam",                         // 0x1279815C
        "g_WaveletOffset",                             // 0x9BE8354A
        "g_WaveletScale",                              // 0xD62C681E
        "g_WaveSpeed",                                 // 0xE4C68FF3
        "g_WaveTime",                                  // 0x8EB9D2A6
        "g_WaveTime1",                                 // 0x6EE5BF35
        "g_WhitecapColor",                             // 0x29FA2AC1
        "g_WhitecapDistance",                          // 0x5D26B262
        "g_WhitecapDistortion",                        // 0x61053025
        "g_WhitecapNoiseScale",                        // 0x0FF95B0C
        "g_WhitecapScale",                             // 0xA3EA47AC
        "g_WhitecapSpeed",                             // 0x408A9CDE
        "g_WhiteEyeColor",                             // 0x11C90091
        "GeometryInstancing",                          // 0x853F83CB
        "GeometryInstancingOff",                       // 0xD7825D20
        "GeometryInstancingOn",                        // 0x815446B5
        "GetAmbientLight",                             // 0x8955127D
        "GetAmbientOcclusion",                         // 0x594F3698
        "GetColor",                                    // 0x0BD07791
        "GetCrystal",                                  // 0x97D3BEA3
        "GetCrystalType",                              // 0x59C8D2C1
        "GetCustumizeColorAura",                       // 0x8546FE49
        "GetDecalColor",                               // 0xD2777173
        "GetDecalColorAlpha",                          // 0x584265DD
        "GetDecalColorOff",                            // 0x4242B842
        "GetDecalColorRGBA",                           // 0xF35F5131
        "GetDetail",                                   // 0xA2B7EF2F
        "GetDetailMap",                                // 0xEB53FD3A
        "GetDiffuse",                                  // 0xBEF877CB
        "GetDiffuseMap",                               // 0x1A43D949
        "GetDiffuseMapOff",                            // 0x2CD41A98
        "GetDiffuseTex",                               // 0x63030C80
        "GetDiffuseTexOn",                             // 0xEFDEA8F6
        "GetDirectionalLight",                         // 0x8115916D
        "GetFade",                                     // 0x1EE40E93
        "GetFadeAlpha",                                // 0xDBD08C23
        "GetFakeSpecular",                             // 0x3C957CD3
        "GetHairFlow",                                 // 0xCD1484E7
        "GetInstanceData",                             // 0x086F8E39
        "GetInstancingData_Bush",                      // 0xC011FE92
        "GetLocalPosition",                            // 0xBB30A69D
        "GetMaterial",                                 // 0x8518992B
        "GetMaterialParameter",                        // 0x6448E37B
        "GetMaterialValue",                            // 0x380CAED0
        "GetMaterialValueBody",                        // 0x2BDB45F1
        "GetMulti",                                    // 0xA8177C7D
        "GetMultiValues",                              // 0x1DF2985C
        "GetMultiWhitecap",                            // 0xEC806138
        "GetMultiWhitecapOn",                          // 0xF0C11E20
        "GetNoInstancingData_Bush",                    // 0x4855866D
        "GetNormal",                                   // 0xAD34F858
        "GetNormalMap",                                // 0xCBDFD5EC
        "GetNormalMapOff",                             // 0xA66B15A1
        "GetReflect",                                  // 0x9EDD6FF3
        "GetReflectColor",                             // 0x67F75CDF
        "GetReflection",                               // 0xA2ECDF8E
        "GetReflectionPower",                          // 0xE041892A
        "GetReflectionPowerOff",                       // 0x32F05363
        "GetReflectionPowerOn",                        // 0x26E40878
        "GetRefraction",                               // 0x0E9C7A0B
        "GetRefractionMask",                           // 0x4A323184
        "GetRefractionMaskPlane",                      // 0xE7D8EA7E
        "GetRefractionPower",                          // 0xB5B1C44A
        "GetRefractionPowerOff",                       // 0x824D5B42
        "GetRLR",                                      // 0x11433F2D
        "GetShadow",                                   // 0xF9C71291
        "GetSpecular",                                 // 0x0B59CEE7
        "GetSpecularMap",                              // 0xBFC2E0F7
        "GetSpecularMapOff",                           // 0x772FF72B
        "GetSpecularOff",                              // 0x07D3170F
        "GetSpecularOn",                               // 0xB0089E50
        "GetSubColor",                                 // 0x24826489
        "GetUnderWaterLighting",                       // 0xEAC154EC
        "GetValue",                                    // 0x70F1674C
        "GetValues",                                   // 0xB616DC5A
        "GetValuesCompatibility",                      // 0x600EF9DF
        "GetValuesMultiMaterial",                      // 0x5CC605B5
        "GetVelocity",                                 // 0x477E3A17
        "GetVelocityStatic",                           // 0x3DD9525E
        "GetWater",                                    // 0x96B52BA2
        "GetWaterColor",                               // 0xF8EF655E
        "GetWaterColorDepth",                          // 0x08404EC3
        "GetWaterColorWaterDepth",                     // 0xE6A6AD27
        "GetWave",                                     // 0x8EB54EBA
        "GetWaveValues",                               // 0x115CB66A
        "GlassBlend",                                  // 0x2638D2A4
        "GlassBlendMode",                              // 0x9F2A6183
        "LightClip",                                   // 0x7DB09695
        "Lighting",                                    // 0x575CA84C
        "LightingLow",                                 // 0x2807B89E
        "MAIN",                                        // 0xA8F9FFCC
        "OutputType_Table",                            // 0xBB592EBB
        "PASS_0",                                      // 0xC5A5389C
        "PASS_10",                                     // 0xC6BE7BBA
        "PASS_12",                                     // 0x28B01A96
        "PASS_14",                                     // 0xC1D3BFA3
        "PASS_7",                                      // 0x5BC1AD3F
        "PASS_COMPOSITE_OPAQUE",                       // 0x955C0B73
        "PASS_COMPOSITE_SEMITRANSPARENCY",             // 0xC885BBD3
        "PASS_COMPOSITE_SEMITRANSPARENCY_UNDER_WATER", // 0xF21A038F
        "PASS_G_OPAQUE",                               // 0x03AC862E
        "PASS_G_SEMITRANSPARENCY",                     // 0x6006067F
        "PASS_ID",                                     // 0x76303D61
        "PASS_LIGHTING_OPAQUE",                        // 0xFBDE0A8F
        "PASS_LIGHTING_SEMITRANSPARENCY",              // 0x1F197698
        "PASS_SEMITRANSPARENCY",                       // 0x2D0C1A37
        "PASS_WATER",                                  // 0x8EF40D56
        "PASS_WATER_Z",                                // 0x24CDF1EA
        "PASS_WIREFRAME",                              // 0xEA06F2F7
        "PASS_Z_OPAQUE",                               // 0xE412A2D4
        "PointLightCount_Table",                       // 0x9391070F
        "PointLightPositionType_Table",                // 0x97C9F730
        "PointLightType_Table",                        // 0xF0E08E18
        "ReflectionMapType",                           // 0x607399CA
        "SelectOutput",                                // 0x8BBA71F8
        "ShadowDistanceFadeType",                      // 0x3312B7E1
        "ShadowSoftShadowType",                        // 0xA89D89F0
        "SpecularLighting",                            // 0x0D812FA4
        "SUB_VIEW_CUBE_0",                             // 0x66244231
        "SUB_VIEW_MAIN",                               // 0xF43B2F35
        "SUB_VIEW_ROOF",                               // 0xAE5E6A42
        "SUB_VIEW_SHADOW_0",                           // 0x99B22D1C
        "SUB_VIEW_SHADOW_1",                           // 0xEEB51D8A
        "TextureColor1_CalculateAlpha_Table",          // 0x75D3837A
        "TextureColor1_CalculateColor_Table",          // 0xD08FBF97
        "TextureColor1_ColorToAlpha_Table",            // 0x2182C4CC
        "TextureColor1_Decode_Table",                  // 0x38F81F3C
        "TextureColor1_Table",                         // 0x30450F85
        "TextureColor1_UvNo_Table",                    // 0x640B0CFA
        "TextureColor2_CalculateAlpha_Table",          // 0x4CAB2E3A
        "TextureColor2_CalculateColor_Table",          // 0xE9F712D7
        "TextureColor2_ColorToAlpha_Table",            // 0x8E2B8906
        "TextureColor2_Decode_Table",                  // 0x44993AE7
        "TextureColor2_Table",                         // 0x01AD1518
        "TextureColor2_UvNo_Table",                    // 0x1395DE0A
        "TextureColor3_CalculateAlpha_Table",          // 0x5B834AFA
        "TextureColor3_CalculateColor_Table",          // 0xFEDF7617
        "TextureColor3_ColorToAlpha_Table",            // 0xEB4CB240
        "TextureColor3_Decode_Table",                  // 0xD996DB91
        "TextureColor3_Table",                         // 0xA7DA1EAC
        "TextureColor3_UvNo_Table",                    // 0x88309265
        "TextureColor4_CalculateAlpha_Table",          // 0x3E5A74BA
        "TextureColor4_CalculateColor_Table",          // 0x9B064857
        "TextureColor4_ColorToAlpha_Table",            // 0x0A0814D3
        "TextureColor4_Decode_Table",                  // 0xBC5B7151
        "TextureColor4_Table",                         // 0x627D2022
        "TextureColor4_UvNo_Table",                    // 0xFCA87BEA
        "TextureDistortion",                           // 0xC2D54B39
        "TextureDistortion_UvNo_Table",                // 0x295712ED
        "TextureDistortion_UvSet0_Table",              // 0x97561751
        "TextureDistortion_UvSet1_Table",              // 0x31211CE5
        "TextureDistortion_UvSet2_Table",              // 0x00C90678
        "TextureDistortion_UvSet3_Table",              // 0xA6BE0DCC
        "TextureNormal_Table",                         // 0x094D2909
        "TextureNormal_UvNo_Table",                    // 0x95D37D89
        "TexturePalette_Table",                        // 0x837F9F33
        "TextureReflection_CalculateColor_Table",      // 0xA1F5312D
        "TextureReflection_Table",                     // 0xFAFCF387
        "TransformProj",                               // 0x09500613
        "TransformType",                               // 0xD7826DAA
        "TransformView",                               // 0xA5A1910D
        "Type",                                        // 0x0DA8270B
        "Type1",                                       // 0xC6017195
        "UvCompute0_Table",                            // 0x4B77C38D
        "UvCompute1_Table",                            // 0xED00C839
        "UvCompute2_Table",                            // 0xDCE8D2A4
        "UvCompute3_Table",                            // 0x7A9FD910
        "UvPrecisionType_Table",                       // 0x9A80A23B
        "UvSetCount_Table",                            // 0xFD7A2BE9
        "Vertex",                                      // 0x5F9FDDA0
        "VertexWave",                                  // 0x9E45B87D

        // From testing knownsuffixes with known constants
        "GetFadeAlphaDistance",
        "GetMaterialValueBodyJJM",
        "GetMaterialValueFaceEmissive",
        "GetSubColorFace",
        "GetSubColorHair",
        "g_LayerSoftEdge",  // d04cb491
        "GetCrystalTypeSphereMap", // e3531f26 - plausible but unsure. Does seem to be used by crystal and bg shaders.

        // More cracked
        "GetTripleWhitecap",
        "GetSingleWhitecap",
    ];

    private static readonly IReadOnlyList<string> KnownSuffixes =
        [
            "1",
            "2",
            "3",
            "4",
            "5",
            "_0_0",
            "_0",
            "_1_0",
            "_1_1",
            "_1",
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
            "_Off",
            "_On",
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
            "JJM",
            "Low",
            "Mask",
            "Material",
            "Mul",
            "Map",
            "Multi",
            "Apply",
            "Static",
            "MultiMaterial",
            "None",
            "Normal",
            "Off",
            "On",
            "Static",
            "ParallaxOcclusion",
            "Parameter",
            "Plane",
            "PlaneFar",
            "PlaneNear",
            "Power",
            "ReflectivityRGB",
            "RGBA",
            "Rigid",
            "Simple",
            "Skin",
            "TerrainEadg",
            "Tex",
            "Value",
            "Values",
            "WaterDepth",
            "Wave",
            "Single",
            "Double",
            "Triple",
            "Quadruple",
            "Distortion",
        ];
}
