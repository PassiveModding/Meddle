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

    public record NameItem
    {
        public NameItem(string KnownName, uint? FixedCrcValue, NameItemCategory Category)
        {
            this.KnownName = KnownName;
            ComputedCrc = Crc32.GetHash(KnownName);
            this.FixedCrcValue = FixedCrcValue;
            if (this.FixedCrcValue != null && this.FixedCrcValue != ComputedCrc)
            {
                throw new Exception($"Provided CRC {FixedCrcValue} != Computed CRC {ComputedCrc} for {KnownName}");
            }
            this.Category = Category;
        }

        public string KnownName { get; init; }
        private uint? FixedCrcValue { get; init; }
        private uint ComputedCrc { get; init; }
        public uint Crc => FixedCrcValue ?? ComputedCrc;
        
        public NameItemCategory Category { get; init; }
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
            
            foreach (var constant in NamedItems)
            {
                var name = new Name(constant.KnownName);
                buffer[name.Crc] = name;
            }

            foreach (var constant in NamedItems)
            {
                foreach (var suffix in KnownSuffixes)
                {
                    var suffixedName = new SuffixedName($"{constant.KnownName}{suffix}");
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
                    if (!buffer.ContainsKey(name.Crc))
                    {
                        buffer[name.Crc] = name;
                    }
                }
            }
        }
        
        return Constants;
    }

    public enum NameItemCategory
    {
        MaterialParam,
        MaterialKey,
        MaterialValue,
        SceneKey,
        SceneValue,
        SubViewKey,
        SubViewValue
    }

    private static readonly IReadOnlyList<NameItem> NamedItems =
    [
        // new("0x052ED035",                         0x052ED035, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.07
        // new("0x064CBF83",                         0x064CBF83, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.5
        // new("0x093084AD",                         0x093084AD, NameItemCategory.MaterialParam),  // Unknown [crystal.shpk,bgcrestchange.shpk,bgcolorchange.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=1
        // new("0x0B46E7BE",                         0x0B46E7BE, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0x0BA59580",                         0x0BA59580, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=10
        // new("0x12F6AB51",                         0x12F6AB51, NameItemCategory.MaterialParam),  // Unknown [bgprop.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bguvscroll.shpk,bg.shpk]=3
        // new("0x15B70E35",                         0x15B70E35, NameItemCategory.MaterialParam),  // Unknown [characterlegacy.shpk,character.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk]=0
        // new("0x16AF3E5F",                         0x16AF3E5F, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0x1A60F60E",                         0x1A60F60E, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0,0
        // new("0x2334AA21",                         0x2334AA21, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0,900,10,100
        // new("0x2377A510",                         0x2377A510, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0x2B5EB116",                         0x2B5EB116, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=-45
        // new("0x32A89D80",                         0x32A89D80, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.15
        // new("0x37C05873",                         0x37C05873, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0
        // new("0x3FD623A8",                         0x3FD623A8, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=4
        // new("0x4172EDCC",                         0x4172EDCC, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1
        // new("0x43345395",                         0x43345395, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1
        // new("0x44EF5418",                         0x44EF5418, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1,0
        // new("0x45364F70",                         0x45364F70, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.15
        // new("0x498092FD",                         0x498092FD, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=40
        // new("0x4EC3879E",                         0x4EC3879E, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0x5164FA14",                         0x5164FA14, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=5
        // new("0x5C598180",                         0x5C598180, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=45
        // new("0x63747CC4",                         0x63747CC4, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0x6514A4DB",                         0x6514A4DB, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=300,300
        // new("0x6A197C9E",                         0x6A197C9E, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1,0
        // new("0x6C159E95",                         0x6C159E95, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0.85
        // new("0x6E0A1C94",                         0x6E0A1C94, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.5
        // new("0x71CC9A45",                         0x71CC9A45, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        // new("0x720916BD",                         0x720916BD, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.9
        // new("0x72291E75",                         0x72291E75, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0,500,10,50
        // new("0x72B002C5",                         0x72B002C5, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.2
        // new("0x738A241C",                         0x738A241C, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        // new("0x7A08F978",                         0x7A08F978, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=45
        // new("0x7B086C53",                         0x7B086C53, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=60,195,190
        // new("0x7B5813E0",                         0x7B5813E0, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=30,1000,50,100
        // new("0x7D6268DD",                         0x7D6268DD, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1,0
        // new("0x7DB2732C",                         0x7DB2732C, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0
        // new("0x8500AEA4",                         0x8500AEA4, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0x852A9263",                         0x852A9263, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=70
        // new("0x8F4D585E",                         0x8F4D585E, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0x9E8B9C5A",                         0x9E8B9C5A, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.05
        // new("0xA2A01C0A",                         0xA2A01C0A, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.5
        // new("0xA90DD1EF",                         0xA90DD1EF, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.1
        // new("0xAD94E254",                         0xAD94E254, NameItemCategory.MaterialParam),  // Unknown [character.shpk,characterlegacy.shpk,characterglass.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk]=0
        // new("0xAE4F649C",                         0xAE4F649C, NameItemCategory.MaterialParam),  // Unknown [characterglass.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        // new("0xB1542ADD",                         0xB1542ADD, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=15
        // new("0xB3A7C1B5",                         0xB3A7C1B5, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0,500,10,50
        // new("0xB61D7498",                         0xB61D7498, NameItemCategory.MaterialParam),  // Unknown [character.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,characterlegacy.shpk]=0
        // new("0xB6EEA089",                         0xB6EEA089, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0
        // new("0xB8827D5E",                         0xB8827D5E, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=500,500
        // new("0xB88B859A",                         0xB88B859A, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0xB8ACCE58",                         0xB8ACCE58, NameItemCategory.MaterialParam),  // Unknown [bg.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk]=50,100,50,100
        // new("0xB9766DBB",                         0xB9766DBB, NameItemCategory.MaterialParam),  // Unknown [river.shpk,water.shpk]=50
        // new("0xBAD6CC20",                         0xBAD6CC20, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0,1000,50,100
        // new("0xBFB7646B",                         0xBFB7646B, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.5
        // new("0xBFE9D12D",                         0xBFE9D12D, NameItemCategory.MaterialParam),  // Unknown [bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk]=1
        // new("0xC582F820",                         0xC582F820, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1,0
        // new("0xC598FE75",                         0xC598FE75, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=10
        // new("0xC70F951E",                         0xC70F951E, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=25
        // new("0xCBF2CD55",                         0xCBF2CD55, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.8
        // new("0xCEC9B6FF",                         0xCEC9B6FF, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=20
        // new("0xD2F9EC63",                         0xD2F9EC63, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1,0
        // new("0xD67F62C8",                         0xD67F62C8, NameItemCategory.MaterialParam),  // Unknown [bgprop.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bguvscroll.shpk,bg.shpk]=1
        // new("0xD721E19F",                         0xD721E19F, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0xD87BBC76",                         0xD87BBC76, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1
        // new("0xD8A98BE7",                         0xD8A98BE7, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.1
        // new("0xDA3D022F",                         0xDA3D022F, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1
        // new("0xDA8FA72C",                         0xDA8FA72C, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=1
        // new("0xDE93031F",                         0xDE93031F, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.25
        // new("0xE2BA75E1",                         0xE2BA75E1, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0,700,10,100
        // new("0xE8C5CBFF",                         0xE8C5CBFF, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        // new("0xE9154EAA",                         0xE9154EAA, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.3
        // new("0xEA8375A6",                         0xEA8375A6, NameItemCategory.MaterialParam),  // Unknown [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        // new("0xF769298E",                         0xF769298E, NameItemCategory.MaterialParam),  // Unknown [bg.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk,bgprop.shpk]=0.3,0.3,0.3,0.3
        // new("0xFA124634",                         0xFA124634, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=0.5
        // new("0xFC9C8BD6",                         0xFC9C8BD6, NameItemCategory.MaterialParam),  // Unknown [water.shpk]=-20
        new("g_AlphaAperture",                    0xD62BF368, NameItemCategory.MaterialParam),  // [character.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,characterlegacy.shpk]=2
        new("g_AlphaMultiParam",                  0x07EDA444, NameItemCategory.MaterialParam),  // [bg.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk]=0,0,0,0
        new("g_AlphaOffset",                      0xD07A6A65, NameItemCategory.MaterialParam),  // [character.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,characterlegacy.shpk]=0
        new("g_AlphaThreshold",                   0x29AC0223, NameItemCategory.MaterialParam),  // [characterlegacy.shpk,bgprop.shpk,bg.shpk,bguvscroll.shpk,character.shpk,bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,bgcolorchange.shpk,verticalfog.shpk,crystal.shpk,river.shpk,water.shpk,lightshaft.shpk]=0
        new("g_AmbientOcclusionMask",             0x575ABFB2, NameItemCategory.MaterialParam),  // [character.shpk]=
        new("g_AngleClip",                        0x71DBDA81, NameItemCategory.MaterialParam),  // [lightshaft.shpk]=0
        new("g_CausticsPower",                    0x7071F15D, NameItemCategory.MaterialParam),  // [river.shpk]=0.75;[water.shpk]=0.5
        new("g_CausticsReflectionPowerBright",    0x0CC09E67, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=0.5
        new("g_CausticsReflectionPowerDark",      0xC295EA6C, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=0.5
        new("g_Color",                            0xD27C58B9, NameItemCategory.MaterialParam),  // [lightshaft.shpk]=1,1,1;[verticalfog.shpk]=0.8,0.8,0.8,1
        new("g_ColorUVScale",                     0xA5D02C52, NameItemCategory.MaterialParam),  // [bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk]=1,1,1,1
        new("g_DetailColor",                      0xDD93D839, NameItemCategory.MaterialParam),  // [bg.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk,bgprop.shpk]=0.5,0.5,0.5
        new("g_DetailColorFadeDistance",          0xF3F28C58, NameItemCategory.MaterialParam),  // [bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=0,0
        new("g_DetailColorMipBias",               0xB10AF2DA, NameItemCategory.MaterialParam),  // [bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=0,0
        new("g_DetailColorUvScale",               0xC63D9716, NameItemCategory.MaterialParam),  // [bg.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk,bgprop.shpk]=4,4,4,4
        new("g_DetailID",                         0x8981D4D9, NameItemCategory.MaterialParam),  // [bg.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk,bgprop.shpk]=0
        new("g_DetailNormalFadeDistance",         0x236EE793, NameItemCategory.MaterialParam),  // [bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=0,0
        new("g_DetailNormalMipBias",              0x756DFE22, NameItemCategory.MaterialParam),  // [bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=0,0
        new("g_DetailNormalScale",                0x9F42EDA2, NameItemCategory.MaterialParam),  // [bg.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk,bgprop.shpk]=1
        new("g_DetailNormalUvScale",              0x025A9BEE, NameItemCategory.MaterialParam),  // [bg.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk,bgprop.shpk]=4,4,4,4
        new("g_DiffuseColor",                     0x2C2A34DD, NameItemCategory.MaterialParam),  // [bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1,1,1
        new("g_EmissiveColor",                    0x38A64362, NameItemCategory.MaterialParam),  // [bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0,0,0
        new("g_EnableLightShadow",                0x5095E770, NameItemCategory.MaterialParam),  // [bg.shpk,crystal.shpk,bguvscroll.shpk,bgprop.shpk,bgcolorchange.shpk,bgcrestchange.shpk]=0
        new("g_EnableShadow",                     0xBCEA8C11, NameItemCategory.MaterialParam),  // [bg.shpk,crystal.shpk,bguvscroll.shpk,bgprop.shpk,bgcolorchange.shpk,bgcrestchange.shpk]=0
        new("g_EnvMapPower",                      0xEEF5665F, NameItemCategory.MaterialParam),  // [crystal.shpk,bgcrestchange.shpk,bgcolorchange.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=0.85
        new("g_FadeDistance",                     0xC7D0DB1A, NameItemCategory.MaterialParam),  // [verticalfog.shpk]=1000,1500
        new("g_Fresnel",                          0xE3AA427A, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=
        new("g_GlassIOR",                         0x7801E004, NameItemCategory.MaterialParam),  // [characterglass.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1
        new("g_GlassThicknessMax",                0xC4647F37, NameItemCategory.MaterialParam),  // [characterglass.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0.01
        new("g_Gradation",                        0x94B40EEE, NameItemCategory.MaterialParam),  // [verticalfog.shpk]=0.5
        new("g_HeightMapScale",                   0xA320B199, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=0.5
        new("g_HeightMapUVScale",                 0x5B99505D, NameItemCategory.MaterialParam),  // [water.shpk]=0.25
        new("g_HeightScale",                      0x8F8B0070, NameItemCategory.MaterialParam),  // [bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk]=0.015
        new("g_InclusionAperture",                0xBCA22FD4, NameItemCategory.MaterialParam),  // [crystal.shpk,bgcrestchange.shpk,bgcolorchange.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=1
        new("g_Intensity",                        0xBCBA70E1, NameItemCategory.MaterialParam),  // [verticalfog.shpk]=0.01
        new("g_IrisOptionColorEmissiveIntensity", 0x7918D232, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1
        new("g_IrisOptionColorEmissiveRate",      0x8EA14846, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        new("g_IrisOptionColorRate",              0x29253809, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        new("g_IrisRingColor",                    0x50E36D56, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1,1,1
        new("g_IrisRingEmissiveIntensity",        0x7DABA471, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0.25
        new("g_IrisRingForceColor",               0x58DE06E2, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0,0,0
        new("g_IrisRingOddRate",                  0x285F72D2, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1E-45
        new("g_IrisRingUvFadeWidth",              0x5B608CFE, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0.04,0.02
        new("g_IrisRingUvRadius",                 0xE18398AE, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0.158,0.174
        new("g_IrisThickness",                    0x66C93D3E, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0.5
        new("g_IrisUvRadius",                     0x37DEA328, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0.2
        new("g_LayerColor",                       0x35DC0B6F, NameItemCategory.MaterialParam),  // [verticalfog.shpk]=1,1,1
        new("g_LayerDepth",                       0xA9295FEF, NameItemCategory.MaterialParam),  // [verticalfog.shpk]=10
        new("g_LayerIrregularity",                0x0A00B0A1, NameItemCategory.MaterialParam),  // [verticalfog.shpk]=0.5
        new("g_LayerScale",                       0xBFCC6602, NameItemCategory.MaterialParam),  // [verticalfog.shpk]=0.01
        new("g_LayerSoftEdge",                    0xD04CB491, NameItemCategory.MaterialParam),  // [verticalfog.shpk]=0.05
        new("g_LayerVelocity",                    0x72181E22, NameItemCategory.MaterialParam),  // [verticalfog.shpk]=10,0
        new("g_LipRoughnessScale",                0x3632401A, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0.7
        new("g_MultiDetailColor",                 0x11FD4221, NameItemCategory.MaterialParam),  // [bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk]=0.5,0.5,0.5
        new("g_MultiDetailID",                    0xAC156136, NameItemCategory.MaterialParam),  // [bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk]=0
        new("g_MultiDetailNormalScale",           0xA83DBDF1, NameItemCategory.MaterialParam),  // [bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk]=1
        new("g_MultiDiffuseColor",                0x3F8AC211, NameItemCategory.MaterialParam),  // [crystal.shpk,bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,bgprop.shpk]=1,1,1
        new("g_MultiEmissiveColor",               0xAA676D0F, NameItemCategory.MaterialParam),  // [crystal.shpk,bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,bgprop.shpk]=0,0,0
        new("g_MultiHeightScale",                 0x43E59A68, NameItemCategory.MaterialParam),  // [bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk]=0.015
        new("g_MultiNormalScale",                 0x793AC5A3, NameItemCategory.MaterialParam),  // [bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk]=1
        new("g_MultiSpecularColor",               0x86D60CB8, NameItemCategory.MaterialParam),  // [crystal.shpk,bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,bgprop.shpk]=1,1,1
        new("g_MultiSSAOMask",                    0x926E860D, NameItemCategory.MaterialParam),  // [bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk]=1
        new("g_MultiWaveScale",                   0x37363FDD, NameItemCategory.MaterialParam),  // [river.shpk]=1,1,2,2
        new("g_MultiWhitecapScale",               0x312B69C1, NameItemCategory.MaterialParam),  // [river.shpk]=4,4
        new("g_NearClip",                         0x17A52926, NameItemCategory.MaterialParam),  // [lightshaft.shpk]=0.25
        new("g_NormalScale",                      0xB5545FBB, NameItemCategory.MaterialParam),  // [characterlegacy.shpk,bg.shpk,bgprop.shpk,crystal.shpk,river.shpk,bguvscroll.shpk,character.shpk,bgcolorchange.shpk,water.shpk,characterglass.shpk,charactertransparency.shpk,bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk]=1
        new("g_NormalScale1",                     0x0DD83E61, NameItemCategory.MaterialParam),  // [water.shpk]=1
        new("g_NormalUVScale",                    0xBB99CF76, NameItemCategory.MaterialParam),  // [bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk]=1,1,1,1
        new("g_OutlineColor",                     0x623CC4FE, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0,0,0
        new("g_OutlineWidth",                     0x8870C938, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        new("g_PrefersFailure",                   0x5394405B, NameItemCategory.MaterialParam),  // [water.shpk]=1,0
        new("g_Ray",                              0x827BDD09, NameItemCategory.MaterialParam),  // [lightshaft.shpk]=0,0,1
        new("g_ReflectionPower",                  0x223A3329, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=0.25
        new("g_RefractionColor",                  0xBA163700, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=0.4117,0.4313,0.4509
        new("g_RLRReflectionPower",               0xF2360709, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=0.1
        new("g_SeaWaveScale",                     0xA5FF109A, NameItemCategory.MaterialParam),  // [water.shpk]=50
        new("g_ShaderID",                         0x59BDA0B1, NameItemCategory.MaterialParam),  // [bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        new("g_ShadowAlphaThreshold",             0xD925FF32, NameItemCategory.MaterialParam),  // [character.shpk,charactertransparency.shpk,bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,characterlegacy.shpk,bgcolorchange.shpk,verticalfog.shpk,crystal.shpk,river.shpk,water.shpk,lightshaft.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=0.5
        new("g_ShadowPosOffset",                  0x5351646E, NameItemCategory.MaterialParam),  // [character.shpk,characterlegacy.shpk,characterglass.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk]=0
        new("g_SheenAperture",                    0xF490F76E, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1
        new("g_SheenRate",                        0x800EE35F, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        new("g_SheenTintRate",                    0x1F264897, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        new("g_SingleWaveScale",                  0x5B22E864, NameItemCategory.MaterialParam),  // [river.shpk]=1,1,2,2
        new("g_SingleWhitecapScale",              0xB33DB142, NameItemCategory.MaterialParam),  // [river.shpk]=4,4
        new("g_SoftEadgDistance",                 0x2A57C3CC, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=0.3
        new("g_SpecularColor",                    0x141722D5, NameItemCategory.MaterialParam),  // [bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,bgcolorchange.shpk,bgcrestchange.shpk]=1,1,1
        new("g_SpecularColorMask",                0xCB0338DC, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1,1,1
        new("g_SpecularPower",                    0xD9CB6B9C, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=
        new("g_SpecularUVScale",                  0x8D03A782, NameItemCategory.MaterialParam),  // [bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk]=1,1,1,1
        new("g_SphereMapID",                      0x5106E045, NameItemCategory.MaterialParam),  // [crystal.shpk,bgcrestchange.shpk,bgcolorchange.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=0
        new("g_SphereMapIndex",                   0x074953E9, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        new("g_SSAOMask",                         0xB7FA33E2, NameItemCategory.MaterialParam),  // [characterlegacy.shpk,bg.shpk,bgprop.shpk,crystal.shpk,bguvscroll.shpk,character.shpk,bgcolorchange.shpk,bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk]=1
        new("g_TexAnim",                          0x14D8E13D, NameItemCategory.MaterialParam),  // [lightshaft.shpk]=0,250
        new("g_TextureMipBias",                   0x39551220, NameItemCategory.MaterialParam),  // [characterlegacy.shpk,character.shpk,characterglass.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk]=0
        new("g_TexU",                             0x5926A043, NameItemCategory.MaterialParam),  // [lightshaft.shpk]=0,0,-1
        new("g_TexV",                             0xC02FF1F9, NameItemCategory.MaterialParam),  // [lightshaft.shpk]=0,-1,0
        new("g_TileAlpha",                        0x12C6AC9F, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1
        new("g_TileIndex",                        0x4255F2F4, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        new("g_TileMipBiasOffset",                0x6421DD30, NameItemCategory.MaterialParam),  // [character.shpk,characterlegacy.shpk,characterglass.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk]=0
        new("g_TileScale",                        0x2E60B071, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=16,16
        new("g_ToonIndex",                        0xDF15112D, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=0
        new("g_ToonLightScale",                   0x3CCE9E4C, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=2
        new("g_ToonLightSpecAperture",            0x759036EE, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=50
        new("g_ToonReflectionScale",              0xD96FAF7A, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=2.5
        new("g_ToonSpecIndex",                    0x00A680BC, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=4E-45
        new("g_Transparency",                     0x53E8417B, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=1
        new("g_TransparencyDistance",             0x1624F841, NameItemCategory.MaterialParam),  // [river.shpk]=50;[water.shpk]=100
        new("g_TripleWhitecapScale",              0x113BAFDF, NameItemCategory.MaterialParam),  // [river.shpk]=8,8
        new("g_UVScrollTime",                     0x9A696A17, NameItemCategory.MaterialParam),  // [bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bg.shpk]=10,10,10,10
        new("g_VertexMovementMaxLength",          0xD26FF0AE, NameItemCategory.MaterialParam),  // [characterlegacy.shpk,character.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk]=1
        new("g_VertexMovementScale",              0x641E0F22, NameItemCategory.MaterialParam),  // [characterlegacy.shpk,character.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk]=1
        new("g_WaterDeepColor",                   0xD315E728, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=0.3529,0.372549,0.3921
        new("g_WaveletDistortion",                0x3439B378, NameItemCategory.MaterialParam),  // [water.shpk]=
        new("g_WaveletFadeDistance",              0x4AD899B7, NameItemCategory.MaterialParam),  // [water.shpk]=
        new("g_WaveletNoiseParam",                0x1279815C, NameItemCategory.MaterialParam),  // [water.shpk]=
        new("g_WaveletOffset",                    0x9BE8354A, NameItemCategory.MaterialParam),  // [water.shpk]=0
        new("g_WaveletScale",                     0xD62C681E, NameItemCategory.MaterialParam),  // [water.shpk]=
        new("g_WaveletSinParam",                  0x2F41D796, NameItemCategory.MaterialParam),  // [water.shpk]=
        new("g_WaveParam_NormalScale",            0x592A312C, NameItemCategory.MaterialParam),  // [water.shpk]=2
        new("g_WaveSpeed",                        0xE4C68FF3, NameItemCategory.MaterialParam),  // [river.shpk]=1,1,1,1
        new("g_WaveTime",                         0x8EB9D2A6, NameItemCategory.MaterialParam),  // [river.shpk,water.shpk]=15
        new("g_WaveTime1",                        0x6EE5BF35, NameItemCategory.MaterialParam),  // [water.shpk]=15
        new("g_WhitecapColor",                    0x29FA2AC1, NameItemCategory.MaterialParam),  // [river.shpk]=0.4509,0.4705,0.4901;[water.shpk]=0.4509,0.4705,0.4901,0.3
        new("g_WhitecapDistance",                 0x5D26B262, NameItemCategory.MaterialParam),  // [water.shpk]=0.5
        new("g_WhitecapNoiseScale",               0x0FF95B0C, NameItemCategory.MaterialParam),  // [water.shpk]=0.1,0.1
        new("g_WhitecapScale",                    0xA3EA47AC, NameItemCategory.MaterialParam),  // [water.shpk]=50
        new("g_WhitecapSpeed",                    0x408A9CDE, NameItemCategory.MaterialParam),  // [river.shpk]=6,3,3;[water.shpk]=15
        new("g_WhiteEyeColor",                    0x11C90091, NameItemCategory.MaterialParam),  // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=1,1,1
        // new("0xF52CCF05",                         0xF52CCF05, NameItemCategory.MaterialKey),   // Unknown [characterlegacy.shpk,character.shpk,charactertransparency.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk]=0xDFE74BAC
        // new("0xA7D2FF60",                         0xA7D2FF60, NameItemCategory.MaterialValue),  // Unknown 
        // new("0xDFE74BAC",                         0xDFE74BAC, NameItemCategory.MaterialValue),  // Unknown 
        // new("0x36F72D5F",                         0x36F72D5F, NameItemCategory.MaterialKey),   // Unknown [bg.shpk]=0x88A3965A
        // new("0x1E314009",                         0x1E314009, NameItemCategory.MaterialValue),  // Unknown 
        // new("0x6936709F",                         0x6936709F, NameItemCategory.MaterialValue),  // Unknown 
        // new("0x88A3965A",                         0x88A3965A, NameItemCategory.MaterialValue),  // Unknown 
        // new("0x9807BAC4",                         0x9807BAC4, NameItemCategory.MaterialValue),  // Unknown 
        // new("0xF886E10E",                         0xF886E10E, NameItemCategory.MaterialKey),   // Unknown [characterscroll.shpk]=0x69EB4AE0
        // new("0x69EB4AE0",                         0x69EB4AE0, NameItemCategory.MaterialValue),  // Unknown 
        new("ApplyAlphaTest",                     0xA9A3EE25, NameItemCategory.MaterialKey),   // [bgprop.shpk,bg.shpk,bguvscroll.shpk,bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk]=ApplyAlphaTestOff
        new("ApplyAlphaTestOff",                  0x5D146A23, NameItemCategory.MaterialValue), 
        new("ApplyAlphaTestOn",                   0x72AAA9AE, NameItemCategory.MaterialValue), 
        new("ApplyDepthWhitecap",                 0x28981633, NameItemCategory.MaterialKey),   // [water.shpk]=ApplyDepthWhitecapOff
        new("ApplyDepthWhitecapOff",              0x28D137F1, NameItemCategory.MaterialValue), 
        new("ApplyDepthWhitecapOn",               0xDD54E76C, NameItemCategory.MaterialValue), 
        new("ApplyVertexColor",                   0x4F4F0636, NameItemCategory.MaterialKey),   // [bg.shpk]=ApplyVertexColorOff
        new("ApplyVertexColorOff",                0x7C6FA05B, NameItemCategory.MaterialValue), 
        new("ApplyVertexColorOn",                 0xBD94649A, NameItemCategory.MaterialValue), 
        new("ApplyWavelet",                       0xFB7AD5E4, NameItemCategory.MaterialKey),   // [water.shpk]=ApplyWaveletOff
        new("ApplyWaveletOff",                    0x0EC4134E, NameItemCategory.MaterialValue), 
        // new("CategoryFlowMapType",                0x40D1481E, NameItemCategory.MaterialKey),   // Stub [character.shpk,characterscroll.shpk]=Standard
        // new("Standard",                           0x337C6BC4, NameItemCategory.MaterialValue),  // Stub 
        // new("CategorySpecularType",               0xC8BD1DEF, NameItemCategory.MaterialKey),   // Stub [characterlegacy.shpk]=Default
        // new("Default",                            0x198D11CD, NameItemCategory.MaterialValue),  // Stub 
        // new("Mask",                               0xA02F4828, NameItemCategory.MaterialValue),  // Stub 
        new("DrawDepthMode",                      0xE8DA5B62, NameItemCategory.MaterialKey),   // [characterglass.shpk,charactertransparency.shpk]=DrawDepthMode_Dither
        new("DrawDepthMode_Dither",               0x7B804D6E, NameItemCategory.MaterialValue), 
        new("EnableLighting",                     0x0033C8B5, NameItemCategory.MaterialKey),   // [charactertransparency.shpk]=EnableLightingOn
        new("EnableLightingOn",                   0xD1E60FD9, NameItemCategory.MaterialValue), 
        new("GetCrystalType",                     0x59C8D2C1, NameItemCategory.MaterialKey),   // [crystal.shpk]=GetCrystalTypeEnvMap
        new("GetCrystalTypeEnvMap",               0x9A77AA04, NameItemCategory.MaterialValue), 
        new("GetCrystalTypeSphereMap",            0xE3531F26, NameItemCategory.MaterialValue), 
        new("GetCrystalTypeSphereMapCustum",      0xB29E6018, NameItemCategory.MaterialValue), 
        new("GetDecalColor",                      0xD2777173, NameItemCategory.MaterialKey),   // [characterlegacy.shpk,character.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk]=GetDecalColorOff
        new("GetDecalColorOff",                   0x4242B842, NameItemCategory.MaterialValue), 
        new("GetDecalColorRGBA",                  0xF35F5131, NameItemCategory.MaterialValue), 
        new("GetDiffuseMap",                      0x1A43D949, NameItemCategory.MaterialKey),  
        new("GetDiffuseMapOff",                   0x2CD41A98, NameItemCategory.MaterialValue), 
        new("GetDiffuseTex",                      0x63030C80, NameItemCategory.MaterialKey),   // [iris.shpk]=GetDiffuseTexOff
        new("GetDiffuseTexOff",                   0x3839C7E4, NameItemCategory.MaterialValue), 
        new("GetFadeAlpha",                       0xDBD08C23, NameItemCategory.MaterialKey),   // [verticalfog.shpk]=GetFadeAlphaNone
        new("GetFadeAlphaNone",                   0x34775C9A, NameItemCategory.MaterialValue), 
        new("GetMaterialValue",                   0x380CAED0, NameItemCategory.MaterialKey),   // [skin.shpk]=GetMaterialValueFace
        new("GetMaterialValueFace",               0xF5673524, NameItemCategory.MaterialValue), 
        new("GetMultiWhitecap",                   0xEC806138, NameItemCategory.MaterialKey),   // [river.shpk]=GetMultiWhitecapOn
        new("GetMultiWhitecapOn",                 0xF0C11E20, NameItemCategory.MaterialValue), 
        new("GetNormalMap",                       0xCBDFD5EC, NameItemCategory.MaterialKey),  
        new("GetNormalMapOff",                    0xA66B15A1, NameItemCategory.MaterialValue), 
        new("GetReflectionPower",                 0xE041892A, NameItemCategory.MaterialKey),   // [river.shpk]=GetReflectionPowerOff
        new("GetReflectionPowerOff",              0x32F05363, NameItemCategory.MaterialValue), 
        new("GetRefractionMask",                  0x4A323184, NameItemCategory.MaterialKey),   // [water.shpk]=GetRefractionMaskDepth
        new("GetRefractionMaskDepth",             0xDCC8DB97, NameItemCategory.MaterialValue), 
        new("GetRefractionPower",                 0xB5B1C44A, NameItemCategory.MaterialKey),   // [water.shpk,river.shpk]=GetRefractionPowerOn
        new("GetRefractionPowerOff",              0x824D5B42, NameItemCategory.MaterialValue), 
        new("GetRefractionPowerOn",               0x4B740B02, NameItemCategory.MaterialValue), 
        new("GetSingleWhitecap",                  0xABDA6DFB, NameItemCategory.MaterialKey),   // [river.shpk]=GetSingleWhitecapOn
        new("GetSingleWhitecapOn",                0x19A091DC, NameItemCategory.MaterialValue), 
        new("GetSpecular",                        0x0B59CEE7, NameItemCategory.MaterialKey),  
        new("GetSpecularOff",                     0x07D3170F, NameItemCategory.MaterialValue), 
        new("GetSpecularMap",                     0xBFC2E0F7, NameItemCategory.MaterialKey),  
        new("GetSpecularMapOff",                  0x772FF72B, NameItemCategory.MaterialValue), 
        new("GetSubColor",                        0x24826489, NameItemCategory.MaterialKey),   // [hair.shpk]=GetSubColorHair
        new("GetSubColorHair",                    0xF7B8956E, NameItemCategory.MaterialValue), 
        new("GetTripleWhitecap",                  0x6E8CE685, NameItemCategory.MaterialKey),   // [river.shpk]=GetTripleWhitecapOn
        new("GetTripleWhitecapOn",                0xD2765A5A, NameItemCategory.MaterialValue), 
        new("GetValues",                          0xB616DC5A, NameItemCategory.MaterialKey),   // [characterlegacy.shpk,character.shpk,charactertransparency.shpk,characterinc.shpk,characterscroll.shpk]=GetValuesMultiMaterial;[bg.shpk,bguvscroll.shpk]=GetSingleValues;[river.shpk]=GetMultiValues
        new("GetAlphaMultiValues",                0x941820BE, NameItemCategory.MaterialValue), 
        new("GetAlphaMultiValues2",               0xE49AD72B, NameItemCategory.MaterialValue), 
        new("GetAlphaMultiValues3",               0x939DE7BD, NameItemCategory.MaterialValue), 
        new("GetMultiValues",                     0x1DF2985C, NameItemCategory.MaterialValue), 
        new("GetSingleValues",                    0x669A451B, NameItemCategory.MaterialValue), 
        new("GetValuesCompatibility",             0x600EF9DF, NameItemCategory.MaterialValue), 
        new("GetValuesMultiMaterial",             0x5CC605B5, NameItemCategory.MaterialValue), 
        new("GetWaterColor",                      0xF8EF655E, NameItemCategory.MaterialKey),   // [river.shpk,water.shpk]=GetWaterColorDistance
        new("GetWaterColorDepth",                 0x08404EC3, NameItemCategory.MaterialValue), 
        new("GetWaterColorDistance",              0x86B217C3, NameItemCategory.MaterialValue), 
        new("GetWaterColorWaterDepth",            0xE6A6AD27, NameItemCategory.MaterialValue), 
        new("Lighting",                           0x575CA84C, NameItemCategory.MaterialKey),  
        new("LightingLow",                        0x2807B89E, NameItemCategory.MaterialValue), 
        new("Type",                               0x0DA8270B, NameItemCategory.MaterialKey),   // [lightshaft.shpk]=Type0
        new("Type0",                              0xB1064103, NameItemCategory.MaterialValue), 
        new("VertexWave",                         0x9E45B87D, NameItemCategory.MaterialKey),   // [water.shpk]=VertexWave_Off
        new("VertexWave_Off",                     0xB77F0FAF, NameItemCategory.MaterialValue), 
        // new("0xE62944E7",                         0xE62944E7, NameItemCategory.SceneKey),      // Unknown [bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=0x96BA161F
        // new("0x96BA161F",                         0x96BA161F, NameItemCategory.SceneValue),    // Unknown 
        // new("0xBEA6525E",                         0xBEA6525E, NameItemCategory.SceneKey),      // Unknown [iris.shpk]=0x3DAAF8BE
        // new("0x3DAAF8BE",                         0x3DAAF8BE, NameItemCategory.SceneValue),    // Unknown 
        new("AddLayer",                           0xEA931ECA, NameItemCategory.SceneKey),      // [verticalfog.shpk]=AddLayer0
        new("AddLayer0",                          0x5D82881C, NameItemCategory.SceneValue),   
        new("ApplyAlphaClip",                     0xDCFC844E, NameItemCategory.SceneKey),      // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=ApplyAlphaClipOff
        new("ApplyAlphaClipOff",                  0x7D5081DF, NameItemCategory.SceneValue),   
        new("ApplyDetailMap",                     0x6313FD87, NameItemCategory.SceneKey),      // [bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=ApplyDetailMap_Disable
        new("ApplyDetailMap_Disable",             0x9615E0AB, NameItemCategory.SceneValue),   
        new("ApplyDissolveColor",                 0xAD24ACAD, NameItemCategory.SceneKey),      // [bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=ApplyDissolveColorOff
        new("ApplyDissolveColorOff",              0x03A11B1B, NameItemCategory.SceneValue),   
        new("ApplyDitherClip",                    0x8B036665, NameItemCategory.SceneKey),      // [bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk,bgcolorchange.shpk,crystal.shpk,river.shpk,water.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=ApplyDitherClipOff
        new("ApplyDitherClipOff",                 0x0802566A, NameItemCategory.SceneValue),   
        new("ApplyDynamicWave",                   0xE5D84BEF, NameItemCategory.SceneKey),      // [bgprop.shpk]=ApplyDynamicWaveOff
        new("ApplyDynamicWaveOff",                0xD58B99E1, NameItemCategory.SceneValue),   
        new("ApplyUnderWater",                    0x7725989B, NameItemCategory.SceneKey),      // [river.shpk,water.shpk]=ApplyUnderWaterOff
        new("ApplyUnderWaterOff",                 0xEF6A4182, NameItemCategory.SceneValue),   
        new("ApplyVertexMovement",                0x87D8F48A, NameItemCategory.SceneKey),      // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=ApplyVertexMovementOff
        new("ApplyVertexMovementOff",             0xF8CA223F, NameItemCategory.SceneValue),   
        new("ApplyWavingAnim",                    0x105C6A52, NameItemCategory.SceneKey),      // [bgprop.shpk,bg.shpk]=ApplyWavingAnimOff
        new("ApplyWavingAnimOff",                 0x7E47A68D, NameItemCategory.SceneValue),   
        new("CalculateInstancingPosition",        0x4518960B, NameItemCategory.SceneKey),      // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=CalculateInstancingPosition_Off
        new("CalculateInstancingPosition_Off",    0xD5ECB340, NameItemCategory.SceneValue),   
        new("DebugMode",                          0x611DA1BE, NameItemCategory.SceneKey),      // [water.shpk]=DebugMode_Off
        new("DebugMode_Off",                      0x9F10EE69, NameItemCategory.SceneValue),   
        new("DebugVertexWave",                    0x7D155D6D, NameItemCategory.SceneKey),      // [water.shpk]=DebugVertexWave_Off
        new("DebugVertexWave_Off",                0xCBF7A4ED, NameItemCategory.SceneValue),   
        new("DrawOffscreen",                      0xA1CDEFE9, NameItemCategory.SceneKey),      // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=DrawOffscreenOff
        new("DrawOffscreenOff",                   0x76B07811, NameItemCategory.SceneValue),   
        new("GetAmbientLight",                    0x8955127D, NameItemCategory.SceneKey),      // [bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=GetAmbientLight_Color
        new("GetAmbientLight_Color",              0x23C62FB0, NameItemCategory.SceneValue),   
        new("GetHairFlow",                        0xCD1484E7, NameItemCategory.SceneKey),      // [hair.shpk,character.shpk]=GetHairFlowOff
        new("GetHairFlowOff",                     0x05288D6B, NameItemCategory.SceneValue),   
        new("GetInstanceData",                    0x086F8E39, NameItemCategory.SceneKey),      // [bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=GeometryInstancingOn
        new("GeometryInstancingOn",               0x815446B5, NameItemCategory.SceneValue),   
        new("GetLocalPosition",                   0xBB30A69D, NameItemCategory.SceneKey),      // [bg.shpk]=GetLocalPositionNone
        new("GetLocalPositionNone",               0xEFCC34B1, NameItemCategory.SceneValue),   
        new("GetMaterialParameter",               0x6448E37B, NameItemCategory.SceneKey),      // [river.shpk]=0x812D4365;[water.shpk]=0xD6294FD5
        // new("0x812D4365",                         0x812D4365, NameItemCategory.SceneValue),    // Unknown 
        // new("0xD6294FD5",                         0xD6294FD5, NameItemCategory.SceneValue),    // Unknown 
        new("GetNormalMap",                       0xCBDFD5EC, NameItemCategory.SceneKey),      // [bgprop.shpk,bg.shpk]=GetNormalMapOff
        new("GetNormalMapOff",                    0xA66B15A1, NameItemCategory.SceneValue),   
        new("GetRLR",                             0x11433F2D, NameItemCategory.SceneKey),      // [river.shpk,water.shpk]=GetRLROff
        new("GetRLROff",                          0x6B2E2D05, NameItemCategory.SceneValue),   
        new("GetVelocity",                        0x477E3A17, NameItemCategory.SceneKey),      // [bgcrestchange.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=GetVelocityStatic
        new("GetVelocityStatic",                  0x3DD9525E, NameItemCategory.SceneValue),   
        new("GlassBlendMode",                     0x9F2A6183, NameItemCategory.SceneKey),      // [characterglass.shpk,charactertransparency.shpk]=GlassBlendMode_Mul
        new("GlassBlendMode_Mul",                 0x44425B98, NameItemCategory.SceneValue),   
        new("ReflectionMapType",                  0x607399CA, NameItemCategory.SceneKey),      // [river.shpk,water.shpk]=ReflectionMapTypeSingle
        new("ReflectionMapTypeSingle",            0x21F13F6D, NameItemCategory.SceneValue),   
        new("TransformView",                      0xA5A1910D, NameItemCategory.SceneKey),      // [characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk]=TransformViewRigid
        new("TransformViewRigid",                 0x4123B1A3, NameItemCategory.SceneValue),   
        // new("0x00000002",                         0x00000002, NameItemCategory.SubViewKey),    // Unknown [bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,iris.shpk,hair.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk,bgcolorchange.shpk,crystal.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=SUB_VIEW_SHADOW_0;[charactertattoo.shpk,characterocclusion.shpk,river.shpk,water.shpk]=SUB_VIEW_MAIN;[verticalfog.shpk,lightshaft.shpk]=MAIN
        new("MAIN",                               0xA8F9FFCC, NameItemCategory.SubViewValue), 
        new("SUB_VIEW_MAIN",                      0xF43B2F35, NameItemCategory.SubViewValue), 
        new("SUB_VIEW_SHADOW_0",                  0x99B22D1C, NameItemCategory.SubViewValue), 
        // new("0x00000001",                         0x00000001, NameItemCategory.SubViewKey),    // Unknown [bgcrestchange.shpk,characterinc.shpk,characterstockings.shpk,characterscroll.shpk,characterreflection.shpk,charactertattoo.shpk,iris.shpk,hair.shpk,characterocclusion.shpk,skin.shpk,characterglass.shpk,charactertransparency.shpk,character.shpk,characterlegacy.shpk,bgcolorchange.shpk,crystal.shpk,river.shpk,water.shpk,lightshaft.shpk,bgprop.shpk,bguvscroll.shpk,bg.shpk]=Default;[verticalfog.shpk]=Color
        new("Color",                              0x61B590F0, NameItemCategory.SubViewValue), 
        new("Default",                            0xB18FE63D, NameItemCategory.SubViewValue), 
    ];

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
        "ApplyDepthWhitecapOff",                       // 0x28d137f1
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
        "ApplyWaveletOff",                             // 0xec4134e
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
        "GetCrystalTypeEnvMap",                        // 0x9a77aa04
        "GetCrystalTypeSphereMap",                     // 0xe3531f26
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
        "GetDiffuseTexOff",                            // 0x3839c7e4
        "GetDirectionalLight",                         // 0x8115916D
        "GetFade",                                     // 0x1EE40E93
        "GetFadeAlpha",                                // 0xDBD08C23
        "GetFadeAlphaNone",                            // 0x34775c9a
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
        "GetRefractionMaskDepth",                      // 0xdcc8db97
        "GetRefractionPower",                          // 0xB5B1C44A
        "GetRefractionPowerOff",                       // 0x824D5B42
        "GetRefractionPowerOn",                        // 0x4b740b02
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
        "GetWaterColorDistance",                       // 0x86b217c3
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
        "Type0",                                       // 0xb1064103
        "Type1",                                       // 0xC6017195
        "UvCompute0_Table",                            // 0x4B77C38D
        "UvCompute1_Table",                            // 0xED00C839
        "UvCompute2_Table",                            // 0xDCE8D2A4
        "UvCompute3_Table",                            // 0x7A9FD910
        "UvPrecisionType_Table",                       // 0x9A80A23B
        "UvSetCount_Table",                            // 0xFD7A2BE9
        "Vertex",                                      // 0x5F9FDDA0
        "VertexWave",                                  // 0x9E45B87D
        "VertexWave_Off",                              // 0xb77f0faf

        // More cracked
        "GetFadeAlphaDistance",               // 0x549290EA
        "GetMaterialValueBodyJJM",            // 0x57ff3b64
        "GetMaterialValueFace",               // 0xf5673524
        "GetMaterialValueFaceEmissive",       // 0x0x72E697CD
        "GetSubColorFace",                    // 0x6e5b8f10
        "GetSubColorHair",                    // 0xf7b8956e
        "g_LayerSoftEdge",                    // 0xd04cb491
        "GetCrystalTypeSphereMap",           // 0xe3531f26 - plausible but unsure, used by crystal.shpk
        "GetCrystalTypeSphereMapCustum",     // 0xb29e6018 - plausible but unsure, used by crystal.shpk
        "g_DetailColorMipBias",               // 0xb10af2da
        "g_DetailNormalMipBias",              // 0x756dfe22
        "GetValuesSimple",                    // 0x22a4aabf
        "ApplyVertexColorOff",                // 0x7c6fa05b
        "GetTripleWhitecap",                  // 0x6e8ce685
        "GetSingleWhitecap",                  // 0xabda6dfb
        "DrawDepthMode_Dither",               // 0x7b804d6e
        "GetAlphaMultiValues",                // 0x941820be
        "GetAlphaMultiValues2",               // 0xe49ad72b - BGUVScroll only
        "GetAlphaMultiValues3",               // 0x939DE7BD - BGUVScroll only
        "g_WaveletSinParam",                  // 0x2f41d796
        "g_SingleWaveScale",                  // 0x5b22e864
        "g_TileMipBiasOffset",                // 0x6421dd30
        "g_IrisUvRadius",                     // 0x37dea328
        "g_IrisRingOddRate",                  // 0x285f72d2
        "g_IrisRingUvRadius",                 // 0xe18398ae
        "ApplyAlphaTest",                     // 0xa9a3ee25
        "ApplyAlphaTestOn",                   // 0x72aaa9ae
        "ApplyAlphaTestOff",                  // 0x5d146a23
        "g_WaterDeepColor",                   // 0xd315e728
        "g_IrisOptionColorEmissiveIntensity", // 0x7918d232
        "g_IrisOptionColorEmissiveRate",      // 0x8ea14846
        "g_IrisRingUvFadeWidth",              // 0x5b608cfe
        "g_ToonLightSpecAperture",            // 0x759036ee - plausible but unsure, used by characterinc.shpk
        "g_DetailNormalFadeDistance", // 0x236ee793 - should validate this one
        "g_VertexMovementMaxLength",  // 0xd26ff0ae - should validate this one
        "g_WaveParam_NormalScale",    // 0x592a312c
        "GetSingleValues",            // 0x669a451b
        "GetTripleWhitecapOn",        // 0xd2765a5a
        "GetSingleWhitecapOn",        // 0x19a091dc
        "g_SeaWaveScale", // 0xa5ff109a
    ];

    private static readonly IReadOnlyList<string> KnownSuffixes =
        [
            "0",
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
