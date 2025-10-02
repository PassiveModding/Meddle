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

            string[] ps = [..PossibleStrings, ..PossibleStrings2];
            foreach (var possibleString in ps)
            {
                var name = new Name(possibleString);
                buffer.TryAdd(name.Crc, name);
            }

            foreach (var constant in NamedItems)
            {
                foreach (var suffix in KnownSuffixes)
                {
                    var suffixedName = new SuffixedName($"{constant.KnownName}{suffix}");
                    buffer.TryAdd(suffixedName.Crc, suffixedName);
                }
            }
            
            foreach (var possibleString in ps)
            {
                foreach (var suffix in KnownSuffixes)
                {
                    var suffixedName = new SuffixedName($"{possibleString}{suffix}");
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
    
    /// <summary>
    /// These are constants that have either been cracked from their crc or pulled from the games strings
    /// The values pulled from game strings may be categorized incorrectly as I essentially did a lookup for if gamestring.StartsWith(existingKnownValue) and assumed it's a variant of that existing known value.
    /// There are some which satisfy that condition but are actually key types for example.
    /// </summary>
    private static readonly IReadOnlyList<NameItem> NamedItems =
    [
        // MaterialParam Unknown: 0x052ED035 Shaders: water.shpk Defaults: 0.07
        // MaterialParam Unknown: 0x064CBF83 Shaders: water.shpk Defaults: 0.5
        // MaterialParam Unknown: 0x0678C56A Shaders: crystal.shpk Defaults: 
        // MaterialParam Unknown: 0x093084AD Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        // MaterialParam Unknown: 0x0BA59580 Shaders: water.shpk Defaults: 10
        // MaterialParam Unknown: 0x12F6AB51 Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 3
        // MaterialParam Unknown: 0x15B70E35 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0x16AF3E5F Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x1A60F60E Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,0
        // MaterialParam Unknown: 0x2334AA21 Shaders: water.shpk Defaults: 0,900,10,100
        // MaterialParam Unknown: 0x2377A510 Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x25C3F71B Shaders: character.shpk Defaults: 
        // MaterialParam Unknown: 0x2B5EB116 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: -45
        // MaterialParam Unknown: 0x2F5837E2 Shaders: character.shpk Defaults: 
        // MaterialParam Unknown: 0x32A89D80 Shaders: water.shpk Defaults: 0.15
        // MaterialParam Unknown: 0x37C05873 Shaders: water.shpk Defaults: 0
        // MaterialParam Unknown: 0x3A75FA33 Shaders: crystal.shpk Defaults: 
        // MaterialParam Unknown: 0x3FD623A8 Shaders: water.shpk Defaults: 4
        // MaterialParam Unknown: 0x4172EDCC Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0x43345395 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0x44EF5418 Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0x45364F70 Shaders: water.shpk Defaults: 0.15
        // MaterialParam Unknown: 0x498092FD Shaders: water.shpk Defaults: 40
        // MaterialParam Unknown: 0x4EC3879E Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x5164FA14 Shaders: water.shpk Defaults: 5
        // MaterialParam Unknown: 0x5C598180 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 45
        // MaterialParam Unknown: 0x63747CC4 Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x6514A4DB Shaders: water.shpk Defaults: 300,300
        // MaterialParam Unknown: 0x6A197C9E Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0x6C159E95 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.85
        // MaterialParam Unknown: 0x6E0A1C94 Shaders: water.shpk Defaults: 0.5
        // MaterialParam Unknown: 0x71CC9A45 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0x720916BD Shaders: water.shpk Defaults: 0.9
        // MaterialParam Unknown: 0x72291E75 Shaders: water.shpk Defaults: 0,500,10,50
        // MaterialParam Unknown: 0x72B002C5 Shaders: water.shpk Defaults: 0.2
        // MaterialParam Unknown: 0x738A241C Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0x7A08F978 Shaders: water.shpk Defaults: 45
        // MaterialParam Unknown: 0x7B086C53 Shaders: water.shpk Defaults: 60,195,190
        // MaterialParam Unknown: 0x7B5813E0 Shaders: water.shpk Defaults: 30,1000,50,100
        // MaterialParam Unknown: 0x7D6268DD Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0x7DB2732C Shaders: water.shpk Defaults: 0
        // MaterialParam Unknown: 0x8500AEA4 Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x852A9263 Shaders: water.shpk Defaults: 70
        // MaterialParam Unknown: 0x8F4D585E Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x9E8B9C5A Shaders: water.shpk Defaults: 0.05
        // MaterialParam Unknown: 0xA2A01C0A Shaders: water.shpk Defaults: 0.5
        // MaterialParam Unknown: 0xA90DD1EF Shaders: water.shpk Defaults: 0.1
        // MaterialParam Unknown: 0xAD94E254 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xAE4F649C Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xB1542ADD Shaders: water.shpk Defaults: 15
        // MaterialParam Unknown: 0xB3A7C1B5 Shaders: water.shpk Defaults: 0,500,10,50
        // MaterialParam Unknown: 0xB61D7498 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xB6EEA089 Shaders: water.shpk Defaults: 0
        // MaterialParam Unknown: 0xB8827D5E Shaders: water.shpk Defaults: 500,500
        // MaterialParam Unknown: 0xB88B859A Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0xB8ACCE58 Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 50,100,50,100
        // MaterialParam Unknown: 0xB9766DBB Shaders: river.shpk|water.shpk Defaults: 50
        // MaterialParam Unknown: 0xBAD6CC20 Shaders: water.shpk Defaults: 0,1000,50,100
        // MaterialParam Unknown: 0xBFB7646B Shaders: water.shpk Defaults: 0.5
        // MaterialParam Unknown: 0xBFE9D12D Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        // MaterialParam Unknown: 0xC582F820 Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0xC598FE75 Shaders: water.shpk Defaults: 10
        // MaterialParam Unknown: 0xC70F951E Shaders: water.shpk Defaults: 25
        // MaterialParam Unknown: 0xCBF2CD55 Shaders: water.shpk Defaults: 0.8
        // MaterialParam Unknown: 0xCEC9B6FF Shaders: water.shpk Defaults: 20
        // MaterialParam Unknown: 0xD2F9EC63 Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0xD67F62C8 Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        // MaterialParam Unknown: 0xD721E19F Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0xD87BBC76 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0xDA3D022F Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0xDA8FA72C Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0xDE93031F Shaders: water.shpk Defaults: 0.25
        // MaterialParam Unknown: 0xE2BA75E1 Shaders: water.shpk Defaults: 0,700,10,100
        // MaterialParam Unknown: 0xE8C5CBFF Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xE9154EAA Shaders: water.shpk Defaults: 0.3
        // MaterialParam Unknown: 0xEA8375A6 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xF769298E Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.3,0.3,0.3,0.3
        // MaterialParam Unknown: 0xFA124634 Shaders: water.shpk Defaults: 0.5
        // MaterialParam Unknown: 0xFC9C8BD6 Shaders: water.shpk Defaults: -20
        new("g_AlphaAperture", 0xD62BF368, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 2
        new("g_AlphaMultiParam", 0x07EDA444, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0,0,0
        new("g_AlphaMultiWeight", 0x30B91319, NameItemCategory.MaterialParam), // Shaders: bg.shpk Defaults: 
        new("g_AlphaOffset", 0xD07A6A65, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_AlphaThreshold", 0x29AC0223, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|lightshaft.shpk|river.shpk|skin.shpk|verticalfog.shpk|water.shpk Defaults: 0
        new("g_AmbientOcclusionMask", 0x575ABFB2, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk|hair.shpk|skin.shpk Defaults: 
        new("g_AngleClip", 0x71DBDA81, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0
        new("g_CausticsPower", 0x7071F15D, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: [0.75|0.5]
        new("g_CausticsReflectionPowerBright", 0x0CC09E67, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 0.5
        new("g_CausticsReflectionPowerDark", 0xC295EA6C, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 0.5
        new("g_Color", 0xD27C58B9, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk|verticalfog.shpk Defaults: [1,1,1|0.8,0.8,0.8,1]
        new("g_ColorUVScale", 0xA5D02C52, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1,1
        new("g_DetailColor", 0xDD93D839, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.5,0.5,0.5
        new("g_DetailColorFadeDistance", 0xF3F28C58, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0
        new("g_DetailColorMipBias", 0xB10AF2DA, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0
        new("g_DetailColorUvScale", 0xC63D9716, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 4,4,4,4
        new("g_DetailID", 0x8981D4D9, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_DetailNormalFadeDistance", 0x236EE793, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0
        new("g_DetailNormalMipBias", 0x756DFE22, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0
        new("g_DetailNormalScale", 0x9F42EDA2, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_DetailNormalUvScale", 0x025A9BEE, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 4,4,4,4
        new("g_DiffuseColor", 0x2C2A34DD, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1,1,1
        new("g_EmissiveColor", 0x38A64362, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,0,0
        new("g_EnableLightShadow", 0x5095E770, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_EnableShadow", 0xBCEA8C11, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_EnvMapPower", 0xEEF5665F, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.85
        new("g_FadeDistance", 0xC7D0DB1A, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 1000,1500
        new("g_Fresnel", 0xE3AA427A, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 
        new("g_GlassIOR", 0x7801E004, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_GlassThicknessMax", 0xC4647F37, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.01
        new("g_Gradation", 0x94B40EEE, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.5
        new("g_HeightMapScale", 0xA320B199, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 0.5
        new("g_HeightMapUVScale", 0x5B99505D, NameItemCategory.MaterialParam), // Shaders: GameStrings|water.shpk Defaults: 0.25
        new("g_HeightScale", 0x8F8B0070, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.015
        new("g_InclusionAperture", 0xBCA22FD4, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_Intensity", 0xBCBA70E1, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.01
        new("g_IrisOptionColorEmissiveIntensity", 0x7918D232, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_IrisOptionColorEmissiveRate", 0x8EA14846, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_IrisOptionColorRate", 0x29253809, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_IrisRingColor", 0x50E36D56, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1,1,1
        new("g_IrisRingEmissiveIntensity", 0x7DABA471, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.25
        new("g_IrisRingForceColor", 0x58DE06E2, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,0,0
        new("g_IrisRingOddRate", 0x285F72D2, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1E-45
        new("g_IrisRingUvFadeWidth", 0x5B608CFE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.04,0.02
        new("g_IrisRingUvRadius", 0xE18398AE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.158,0.174
        new("g_IrisThickness", 0x66C93D3E, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.5
        new("g_IrisUvRadius", 0x37DEA328, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.2
        new("g_LayerColor", 0x35DC0B6F, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 1,1,1
        new("g_LayerDepth", 0xA9295FEF, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 10
        new("g_LayerIrregularity", 0x0A00B0A1, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.5
        new("g_LayerScale", 0xBFCC6602, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.01
        new("g_LayerSoftEdge", 0xD04CB491, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.05
        new("g_LayerVelocity", 0x72181E22, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 10,0
        new("g_LipRoughnessScale", 0x3632401A, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.7
        new("g_MultiDetailColor", 0x11FD4221, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.5,0.5,0.5
        new("g_MultiDetailID", 0xAC156136, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_MultiDetailNormalScale", 0xA83DBDF1, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_MultiDiffuseColor", 0x3F8AC211, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1
        new("g_MultiEmissiveColor", 0xAA676D0F, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0,0
        new("g_MultiHeightScale", 0x43E59A68, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.015
        new("g_MultiNormalScale", 0x793AC5A3, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_MultiSpecularColor", 0x86D60CB8, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1
        new("g_MultiSSAOMask", 0x926E860D, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_MultiWaveScale", 0x37363FDD, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk Defaults: 1,1,2,2
        new("g_MultiWhitecapDistortion", 0x93504F3B, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 
        new("g_MultiWhitecapScale", 0x312B69C1, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 4,4
        new("g_NearClip", 0x17A52926, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0.25
        new("g_NormalScale", 0xB5545FBB, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|river.shpk|skin.shpk|water.shpk Defaults: 1
        new("g_NormalScale1", 0x0DD83E61, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 1
        new("g_NormalUVScale", 0xBB99CF76, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1,1
        new("g_OutlineColor", 0x623CC4FE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,0,0
        new("g_OutlineWidth", 0x8870C938, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_PrefersFailure", 0x5394405B, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 1,0
        new("g_Ray", 0x827BDD09, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0,0,1
        new("g_ReflectionPower", 0x223A3329, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 0.25
        new("g_RefractionColor", 0xBA163700, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 0.4117,0.4313,0.4509
        new("g_RLRReflectionPower", 0xF2360709, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 0.1
        new("g_ScatteringLevel", 0xB500BB24, NameItemCategory.MaterialParam), // Shaders: bg.shpk|character.shpk|characterlegacy.shpk|hair.shpk Defaults: 
        new("g_SeaWaveScale", 0xA5FF109A, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 50
        new("g_ShaderID", 0x59BDA0B1, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ShadowAlphaThreshold", 0xD925FF32, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|lightshaft.shpk|river.shpk|skin.shpk|verticalfog.shpk|water.shpk Defaults: 0.5
        new("g_ShadowPosOffset", 0x5351646E, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_SheenAperture", 0xF490F76E, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_SheenRate", 0x800EE35F, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_SheenTintRate", 0x1F264897, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_Shininess", 0x992869AB, NameItemCategory.MaterialParam), // Shaders: hair.shpk|river.shpk|water.shpk Defaults: 
        new("g_SingleWaveScale", 0x5B22E864, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 1,1,2,2
        new("g_SingleWhitecapDistortion", 0x97E83104, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 
        new("g_SingleWhitecapScale", 0xB33DB142, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 4,4
        new("g_SoftEadgDistance", 0x2A57C3CC, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 0.3
        new("g_SpecularColor", 0x141722D5, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1
        new("g_SpecularColorMask", 0xCB0338DC, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1,1,1
        new("g_SpecularMask", 0x36080AD0, NameItemCategory.MaterialParam), // Shaders: hair.shpk Defaults: 
        new("g_SpecularPower", 0xD9CB6B9C, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 
        new("g_SpecularUVScale", 0x8D03A782, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1,1
        new("g_SphereMapID", 0x5106E045, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_SphereMapIndex", 0x074953E9, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_SSAOMask", 0xB7FA33E2, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_TexAnim", 0x14D8E13D, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0,250
        new("g_TextureMipBias", 0x39551220, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_TexU", 0x5926A043, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0,0,-1
        new("g_TexV", 0xC02FF1F9, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0,-1,0
        new("g_TileAlpha", 0x12C6AC9F, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_TileIndex", 0x4255F2F4, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_TileMipBiasOffset", 0x6421DD30, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_TileScale", 0x2E60B071, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 16,16
        new("g_ToonIndex", 0xDF15112D, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ToonLightScale", 0x3CCE9E4C, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 2
        new("g_ToonLightSpecAperture", 0x759036EE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 50
        new("g_ToonReflectionScale", 0xD96FAF7A, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 2.5
        new("g_ToonSpecIndex", 0x00A680BC, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 4E-45
        new("g_Transparency", 0x53E8417B, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 1
        new("g_TransparencyDistance", 0x1624F841, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: [50|100]
        new("g_TripleWhitecapDistortion", 0x960450D0, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 
        new("g_TripleWhitecapScale", 0x113BAFDF, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 8,8
        new("g_UVScrollTime", 0x9A696A17, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 10,10,10,10
        new("g_VertexMovementMaxLength", 0xD26FF0AE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_VertexMovementScale", 0x641E0F22, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_WaterDeepColor", 0xD315E728, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 0.3529,0.372549,0.3921
        new("g_WaveletDistortion", 0x3439B378, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveletFadeDistance", 0x4AD899B7, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveletNoiseParam", 0x1279815C, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveletOffset", 0x9BE8354A, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 0
        new("g_WaveletScale", 0xD62C681E, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveletSinParam", 0x2F41D796, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveParam_NormalScale", 0x592A312C, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 2
        new("g_WaveParam_ScatterAlpha", 0xD8A98BE7, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 0.1
        new("g_WaveParam_WhitecapAlpha", 0x0B46E7BE, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 1
        new("g_WaveSpeed", 0xE4C68FF3, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk Defaults: 1,1,1,1
        new("g_WaveTime", 0x8EB9D2A6, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 15
        new("g_WaveTime1", 0x6EE5BF35, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 15
        new("g_WhitecapColor", 0x29FA2AC1, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: [0.4509,0.4705,0.4901|0.4509,0.4705,0.4901,0.3]
        new("g_WhitecapDistance", 0x5D26B262, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 0.5
        new("g_WhitecapDistortion", 0x61053025, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WhitecapNoiseScale", 0x0FF95B0C, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 0.1,0.1
        new("g_WhitecapScale", 0xA3EA47AC, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 50
        new("g_WhitecapSpeed", 0x408A9CDE, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: [6,3,3|15]
        new("g_WhiteEyeColor", 0x11C90091, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1,1,1
        // Category: 0xEA931ECA
        new("AddLayer", 0xEA931ECA, NameItemCategory.SceneKey), // Shaders: verticalfog.shpk Defaults: AddLayer0
        new("AddLayer0", 0x5D82881C, NameItemCategory.SceneValue), // Shaders: GameStrings|verticalfog.shpk
        new("AddLayer1", 0x2A85B88A, NameItemCategory.SceneValue), // Shaders: GameStrings
        new("AddLayer2", 0xB38CE930, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xDCFC844E
        new("ApplyAlphaClip", 0xDCFC844E, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: ApplyAlphaClipOff
        new("ApplyAlphaClipOff", 0x7D5081DF, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("ApplyAlphaClipOn", 0x59C4E6DB, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xA9A3EE25
        new("ApplyAlphaTest", 0xA9A3EE25, NameItemCategory.MaterialKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: ApplyAlphaTestOff
        new("ApplyAlphaTestOff", 0x5D146A23, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk
        new("ApplyAlphaTestOn", 0x72AAA9AE, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|lightshaft.shpk
        // Category: 0xE62944E7
        new("ApplyAuraColor", 0xE62944E7, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: ApplyAuraColorOff
        new("ApplyAuraColorOff", 0x96BA161F, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("ApplyAuraColorOn", 0xBD65191E, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x28981633
        new("ApplyDepthWhitecap", 0x28981633, NameItemCategory.MaterialKey), // Shaders: water.shpk Defaults: ApplyDepthWhitecapOff
        new("ApplyDepthWhitecapOff", 0x28D137F1, NameItemCategory.MaterialValue), // Shaders: water.shpk
        new("ApplyDepthWhitecapOn", 0xDD54E76C, NameItemCategory.MaterialValue), // Shaders: bg.shpk|river.shpk|water.shpk
        // Category: 0x6313FD87
        new("ApplyDetailMap", 0x6313FD87, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: ApplyDetailMap_Disable
        new("ApplyDetailMap_Disable", 0x9615E0AB, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|GameStrings
        new("ApplyDetailMap_Enable", 0x7A3D9EFD, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xAD24ACAD
        new("ApplyDissolveColor", 0xAD24ACAD, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: ApplyDissolveColorOff
        new("ApplyDissolveColorOff", 0x03A11B1B, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("ApplyDissolveColorOn", 0x19229FD6, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x8B036665
        new("ApplyDitherClip", 0x8B036665, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|river.shpk|skin.shpk|water.shpk Defaults: ApplyDitherClipOff
        new("ApplyDitherClipOff", 0x0802566A, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|river.shpk|skin.shpk|water.shpk
        new("ApplyDitherClipOn", 0x61B0CF19, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xE5D84BEF
        new("ApplyDynamicWave", 0xE5D84BEF, NameItemCategory.SceneKey), // Shaders: bgprop.shpk Defaults: ApplyDynamicWaveOff
        new("ApplyDynamicWaveOff", 0xD58B99E1, NameItemCategory.SceneValue), // Shaders: bgprop.shpk|GameStrings
        new("ApplyDynamicWaveOn", 0xE5038D76, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x7725989B
        new("ApplyUnderWater", 0x7725989B, NameItemCategory.SceneKey), // Shaders: river.shpk|water.shpk Defaults: ApplyUnderWaterOff
        new("ApplyUnderWaterOff", 0xEF6A4182, NameItemCategory.SceneValue), // Shaders: GameStrings|river.shpk|water.shpk
        new("ApplyUnderWaterOn", 0xB18A2017, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x4F4F0636
        new("ApplyVertexColor", 0x4F4F0636, NameItemCategory.MaterialKey), // Shaders: bg.shpk Defaults: ApplyVertexColorOff
        new("ApplyVertexColorOff", 0x7C6FA05B, NameItemCategory.MaterialValue), // Shaders: bg.shpk
        new("ApplyVertexColorOn", 0xBD94649A, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|lightshaft.shpk
        // Category: 0x87D8F48A
        new("ApplyVertexMovement", 0x87D8F48A, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: ApplyVertexMovementOff
        new("ApplyVertexMovementOff", 0xF8CA223F, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("ApplyVertexMovementOn", 0xA657DE89, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xFB7AD5E4
        new("ApplyWavelet", 0xFB7AD5E4, NameItemCategory.MaterialKey), // Shaders: water.shpk Defaults: ApplyWaveletOff
        new("ApplyWaveletOff", 0x0EC4134E, NameItemCategory.MaterialValue), // Shaders: water.shpk
        new("ApplyWaveletOn", 0x1140F45C, NameItemCategory.MaterialValue), // Shaders: water.shpk
        // Category: 0x105C6A52
        new("ApplyWavingAnim", 0x105C6A52, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgprop.shpk Defaults: ApplyWavingAnimOff
        new("ApplyWavingAnimOn", 0xF801B859, NameItemCategory.MaterialValue), // Shaders: bg.shpk|GameStrings
        new("ApplyWavingAnimOff", 0x7E47A68D, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgprop.shpk|GameStrings
        new("ApplyWavingAnimation", 0x764AECE7, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("ApplyWavingAnimation_AutoPlacement", 0x7DDA17B6, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("ApplyWavingAnimation_Nothing", 0x69BA5521, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("ApplyWavingAnimation_Shigemi", 0x772A5C52, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0x4518960B
        new("CalculateInstancingPosition", 0x4518960B, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: CalculateInstancingPosition_Off
        new("CalculateInstancingPosition_Off", 0xD5ECB340, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("CalculateInstancingPosition_On", 0x82292C76, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x40D1481E
        // new("CategoryFlowMapType", 0x40D1481E, NameItemCategory.MaterialKey), // Shaders: character.shpk|characterscroll.shpk Defaults: Standard // Stub
        // new("Standard", 0x337C6BC4, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterinc.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk // Stub
        // new("Flow", 0x71ADA939, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterscroll.shpk // Stub
        // Category: 0xC8BD1DEF
        // new("CategorySpecularType", 0xC8BD1DEF, NameItemCategory.MaterialKey), // Shaders: characterlegacy.shpk Defaults: Default // Stub
        // new("Default", 0x198D11CD, NameItemCategory.MaterialValue), // Shaders: characterlegacy.shpk // Stub
        // new("Mask", 0xA02F4828, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterlegacy.shpk|characterstockings.shpk|hair.shpk // Stub
        // Category: 0x611DA1BE
        new("DebugMode", 0x611DA1BE, NameItemCategory.SceneKey), // Shaders: water.shpk Defaults: DebugMode_Off
        new("DebugMode_Off", 0x9F10EE69, NameItemCategory.SceneValue), // Shaders: water.shpk
        // Category: 0x7D155D6D
        new("DebugVertexWave", 0x7D155D6D, NameItemCategory.SceneKey), // Shaders: water.shpk Defaults: DebugVertexWave_Off
        new("DebugVertexWave_Off", 0xCBF7A4ED, NameItemCategory.SceneValue), // Shaders: water.shpk
        // Category: 0xE8DA5B62
        new("DrawDepthMode", 0xE8DA5B62, NameItemCategory.MaterialKey), // Shaders: characterglass.shpk|charactertransparency.shpk Defaults: DrawDepthMode_Dither
        new("DrawDepthMode_Dither", 0x7B804D6E, NameItemCategory.MaterialValue), // Shaders: characterglass.shpk|charactertransparency.shpk
        // Category: 0xA1CDEFE9
        new("DrawOffscreen", 0xA1CDEFE9, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: DrawOffscreenOff
        new("DrawOffscreenOff", 0x76B07811, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("DrawOffscreenOn", 0x62E20914, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x0033C8B5
        new("EnableLighting", 0x0033C8B5, NameItemCategory.MaterialKey), // Shaders: charactertransparency.shpk Defaults: EnableLightingOn
        new("EnableLightingOff", 0x93D6C21A, NameItemCategory.MaterialValue), // Shaders: charactertransparency.shpk
        new("EnableLightingOn", 0xD1E60FD9, NameItemCategory.MaterialValue), // Shaders: charactertransparency.shpk
        new("EnableLightingChara", 0xC29B0C9C, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("EnableLightingCharaOff", 0xD3D18627, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("EnableLightingCharaOn", 0x09A95433, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0x086F8E39
        new("GetInstanceData", 0x086F8E39, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: GeometryInstancingOn
        new("GeometryInstancingOn", 0x815446B5, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|GameStrings
        new("GetInstanceData_Geometry", 0xF1636233, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xB616DC5A
        new("GetValues", 0xB616DC5A, NameItemCategory.MaterialKey), // Shaders: bg.shpk|bguvscroll.shpk Defaults: GetSingleValues
        new("GetValues", 0xB616DC5A, NameItemCategory.MaterialKey), // Shaders: character.shpk|characterinc.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk Defaults: GetValuesMultiMaterial
        new("GetValues", 0xB616DC5A, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetMultiValues
        new("GetMultiValues", 0x1DF2985C, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgcrestchange.shpk|bgprop.shpk|crystal.shpk|river.shpk|water.shpk
        new("GetValuesMultiMaterial", 0x5CC605B5, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterinc.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk
        new("GetValuesCompatibility", 0x600EF9DF, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterinc.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk|hair.shpk|skin.shpk
        new("GetSingleValues", 0x669A451B, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bguvscroll.shpk
        new("GetAlphaMultiValues3", 0x939DE7BD, NameItemCategory.MaterialValue), // Shaders: bguvscroll.shpk|lightshaft.shpk
        new("GetAlphaMultiValues", 0x941820BE, NameItemCategory.MaterialValue), // Shaders: bg.shpk
        new("GetAlphaMultiValues2", 0xE49AD72B, NameItemCategory.MaterialValue), // Shaders: bguvscroll.shpk|lightshaft.shpk
        // Category: 0x8955127D
        new("GetAmbientLight", 0x8955127D, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: GetAmbientLight_Color
        new("GetAmbientLight_Color", 0x23C62FB0, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|GameStrings
        new("GetAmbientLight_None", 0x3FC15CC4, NameItemCategory.SceneValue), // Shaders: GameStrings
        new("GetAmbientLight_SH", 0xB1AD809A, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x59C8D2C1
        new("GetCrystalType", 0x59C8D2C1, NameItemCategory.MaterialKey), // Shaders: crystal.shpk Defaults: GetCrystalTypeEnvMap
        new("GetCrystalTypeEnvMap", 0x9A77AA04, NameItemCategory.MaterialValue), // Shaders: crystal.shpk
        new("GetCrystalTypeSphereMapCustum", 0xB29E6018, NameItemCategory.MaterialValue), // Shaders: crystal.shpk
        new("GetCrystalTypeSphereMap", 0xE3531F26, NameItemCategory.MaterialValue), // Shaders: bg.shpk|crystal.shpk
        // Category: 0xD2777173
        new("GetDecalColor", 0xD2777173, NameItemCategory.MaterialKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: GetDecalColorOff
        new("GetDecalColorOff", 0x4242B842, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk
        new("GetDecalColorAlpha", 0x584265DD, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterlegacy.shpk|charactertattoo.shpk|skin.shpk
        new("GetDecalColorRGBA", 0xF35F5131, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterglass.shpk|characterlegacy.shpk|characterocclusion.shpk|charactertattoo.shpk
        // Category: 0x63030C80
        new("GetDiffuseTex", 0x63030C80, NameItemCategory.MaterialKey), // Shaders: iris.shpk Defaults: GetDiffuseTexOff
        new("GetDiffuseTexOff", 0x3839C7E4, NameItemCategory.MaterialValue), // Shaders: iris.shpk
        new("GetDiffuseTexOn", 0xEFDEA8F6, NameItemCategory.MaterialValue), // Shaders: iris.shpk
        // Category: 0xDBD08C23
        new("GetFadeAlpha", 0xDBD08C23, NameItemCategory.MaterialKey), // Shaders: verticalfog.shpk Defaults: GetFadeAlphaNone
        new("GetFadeAlphaNone", 0x34775C9A, NameItemCategory.MaterialValue), // Shaders: verticalfog.shpk
        new("GetFadeAlphaDistance", 0x549290EA, NameItemCategory.MaterialValue), // Shaders: verticalfog.shpk
        // Category: 0xCD1484E7
        new("GetHairFlow", 0xCD1484E7, NameItemCategory.SceneKey), // Shaders: character.shpk|hair.shpk Defaults: GetHairFlowOff
        new("GetHairFlowOff", 0x05288D6B, NameItemCategory.SceneValue), // Shaders: character.shpk|GameStrings|hair.shpk
        new("GetHairFlowOn", 0x2601F093, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xBB30A69D
        new("GetLocalPosition", 0xBB30A69D, NameItemCategory.SceneKey), // Shaders: bg.shpk Defaults: GetLocalPositionNone
        new("GetLocalPositionNone", 0xEFCC34B1, NameItemCategory.SceneValue), // Shaders: bg.shpk|GameStrings
        new("GetLocalPositionTerrainEadg", 0xCF415365, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x380CAED0
        new("GetMaterialValue", 0x380CAED0, NameItemCategory.MaterialKey), // Shaders: skin.shpk Defaults: GetMaterialValueFace
        new("GetMaterialValueBody", 0x2BDB45F1, NameItemCategory.MaterialValue), // Shaders: skin.shpk
        new("GetMaterialValueBodyJJM", 0x57FF3B64, NameItemCategory.MaterialValue), // Shaders: skin.shpk
        new("GetMaterialValueFaceEmissive", 0x72E697CD, NameItemCategory.MaterialValue), // Shaders: skin.shpk
        new("GetMaterialValueFace", 0xF5673524, NameItemCategory.MaterialValue), // Shaders: skin.shpk
        // Category: 0xEC806138
        new("GetMultiWhitecap", 0xEC806138, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetMultiWhitecapOn
        new("GetMultiWhitecapOn", 0xF0C11E20, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        // Category: 0xCBDFD5EC
        new("GetNormalMap", 0xCBDFD5EC, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgprop.shpk Defaults: GetNormalMapOff
        new("GetNormalMapOff", 0xA66B15A1, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgprop.shpk|bguvscroll.shpk|GameStrings
        new("GetNormalMapOn", 0xD9994EF1, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("GetNormalMapParallaxOcclusion", 0xD9FD8A1C, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0xE041892A
        new("GetReflectionPower", 0xE041892A, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetReflectionPowerOff
        new("GetReflectionPowerOn", 0x26E40878, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        new("GetReflectionPowerOff", 0x32F05363, NameItemCategory.MaterialValue), // Shaders: river.shpk
        // Category: 0x4A323184
        new("GetRefractionMask", 0x4A323184, NameItemCategory.MaterialKey), // Shaders: water.shpk Defaults: GetRefractionMaskDepth
        new("GetRefractionMaskDepth", 0xDCC8DB97, NameItemCategory.MaterialValue), // Shaders: water.shpk
        new("GetRefractionMaskPlane", 0xE7D8EA7E, NameItemCategory.MaterialValue), // Shaders: river.shpk
        // Category: 0xB5B1C44A
        new("GetRefractionPower", 0xB5B1C44A, NameItemCategory.MaterialKey), // Shaders: river.shpk|water.shpk Defaults: GetRefractionPowerOn
        new("GetRefractionPowerOn", 0x4B740B02, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        new("GetRefractionPowerOff", 0x824D5B42, NameItemCategory.MaterialValue), // Shaders: bg.shpk|river.shpk|water.shpk
        // Category: 0x11433F2D
        new("GetRLR", 0x11433F2D, NameItemCategory.SceneKey), // Shaders: river.shpk|water.shpk Defaults: GetRLROff
        new("GetRLROff", 0x6B2E2D05, NameItemCategory.SceneValue), // Shaders: GameStrings|river.shpk|water.shpk
        new("GetRLROn", 0x4BA77904, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xABDA6DFB
        new("GetSingleWhitecap", 0xABDA6DFB, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetSingleWhitecapOn
        new("GetSingleWhitecapOn", 0x19A091DC, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        // Category: 0x24826489
        new("GetSubColor", 0x24826489, NameItemCategory.MaterialKey), // Shaders: hair.shpk Defaults: GetSubColorHair
        new("GetSubColorFace", 0x6E5B8F10, NameItemCategory.MaterialValue), // Shaders: characterocclusion.shpk|charactertattoo.shpk|hair.shpk
        new("GetSubColorHair", 0xF7B8956E, NameItemCategory.MaterialValue), // Shaders: hair.shpk
        // Category: 0x6E8CE685
        new("GetTripleWhitecap", 0x6E8CE685, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetTripleWhitecapOn
        new("GetTripleWhitecapOn", 0xD2765A5A, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        // Category: 0x477E3A17
        new("GetVelocity", 0x477E3A17, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: GetVelocityStatic
        new("GetVelocityStatic", 0x3DD9525E, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|GameStrings
        new("GetVelocityMovable", 0xD10C3AAC, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xF8EF655E
        new("GetWaterColor", 0xF8EF655E, NameItemCategory.MaterialKey), // Shaders: river.shpk|water.shpk Defaults: GetWaterColorDistance
        new("GetWaterColorDepth", 0x08404EC3, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        new("GetWaterColorDistance", 0x86B217C3, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        new("GetWaterColorWaterDepth", 0xE6A6AD27, NameItemCategory.MaterialValue), // Shaders: bg.shpk|river.shpk|water.shpk
        // Category: 0x9F2A6183
        new("GlassBlendMode", 0x9F2A6183, NameItemCategory.SceneKey), // Shaders: characterglass.shpk|charactertransparency.shpk Defaults: GlassBlendMode_Mul
        new("GlassBlendMode_Mul", 0x44425B98, NameItemCategory.SceneValue), // Shaders: characterglass.shpk|charactertransparency.shpk|GameStrings
        new("GlassBlendMode_Add", 0x105A09DE, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x575CA84C
        new("LightingLow", 0x2807B89E, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgprop.shpk|bguvscroll.shpk|GameStrings|lightshaft.shpk
        new("LightingNormal", 0x470E5A1E, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("LightingType", 0xE8001A59, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0x607399CA
        new("ReflectionMapType", 0x607399CA, NameItemCategory.SceneKey), // Shaders: river.shpk|water.shpk Defaults: ReflectionMapTypeSingle
        new("ReflectionMapTypeSingle", 0x21F13F6D, NameItemCategory.SceneValue), // Shaders: GameStrings|river.shpk|water.shpk
        new("ReflectionMapTypeArray", 0xF648D554, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xA5A1910D
        new("TransformView", 0xA5A1910D, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: TransformViewRigid
        new("TransformViewRigid", 0x4123B1A3, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("TransformViewSkin", 0x9C14C8E9, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x0DA8270B
        new("Type", 0x0DA8270B, NameItemCategory.MaterialKey), // Shaders: lightshaft.shpk Defaults: Type0
        new("Type0", 0xB1064103, NameItemCategory.MaterialValue), // Shaders: lightshaft.shpk
        new("Type1", 0xC6017195, NameItemCategory.MaterialValue), // Shaders: lightshaft.shpk
        new("Types", 0x5ED35129, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0x0B59CEE7
        new("GetSpecularOff", 0x07D3170F, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bguvscroll.shpk
        // Category: 0x115CB66A
        // MaterialValue Unknown: 0xF02D5351 Shaders: river.shpk|water.shpk
        // Category: 0x1A43D949
        new("GetDiffuseMapOff", 0x2CD41A98, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bguvscroll.shpk
        // Category: 0x36F72D5F
        // MaterialKey Unknown: 0x36F72D5F Shaders: bg.shpk Defaults: 0x88A3965A
        // MaterialValue Unknown: 0x1E314009 Shaders: bg.shpk
        // MaterialValue Unknown: 0x6936709F Shaders: bg.shpk
        // MaterialValue Unknown: 0x88A3965A Shaders: bg.shpk
        // MaterialValue Unknown: 0x9807BAC4 Shaders: bg.shpk
        // Category: 0x9E45B87D
        new("VertexWave", 0x9E45B87D, NameItemCategory.MaterialKey), // Shaders: water.shpk Defaults: VertexWave_Off
        // MaterialValue Unknown: 0x0DE05A8D Shaders: water.shpk
        // MaterialValue Unknown: 0xB18254EB Shaders: water.shpk
        new("VertexWave_Off", 0xB77F0FAF, NameItemCategory.MaterialValue), // Shaders: water.shpk
        // Category: 0xBFC2E0F7
        new("GetSpecularMapOff", 0x772FF72B, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bguvscroll.shpk
        // Category: 0xF52CCF05
        // MaterialKey Unknown: 0xF52CCF05 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0xDFE74BAC
        // MaterialValue Unknown: 0xA7D2FF60 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterscroll.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|skin.shpk
        // MaterialValue Unknown: 0xDFE74BAC Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk
        // Category: 0xF886E10E
        // MaterialKey Unknown: 0xF886E10E Shaders: characterscroll.shpk Defaults: 0x69EB4AE0
        // MaterialValue Unknown: 0x69EB4AE0 Shaders: characterscroll.shpk
        // MaterialValue Unknown: 0x9A8A46F5 Shaders: characterscroll.shpk
        // Category: 0x6448E37B
        new("GetMaterialParameter", 0x6448E37B, NameItemCategory.SceneKey), // Shaders: river.shpk Defaults: 0x812D4365
        new("GetMaterialParameter", 0x6448E37B, NameItemCategory.SceneKey), // Shaders: water.shpk Defaults: 0xD6294FD5
        // SceneValue Unknown: 0x812D4365 Shaders: river.shpk
        // SceneValue Unknown: 0xD6294FD5 Shaders: water.shpk
        // Category: 0xBEA6525E
        // SceneKey Unknown: 0xBEA6525E Shaders: iris.shpk Defaults: 0x3DAAF8BE
        // SceneValue Unknown: 0x3DAAF8BE Shaders: iris.shpk
        // Category: 0x00000001
        // SubViewKey Unknown: 0x00000001 Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|lightshaft.shpk|river.shpk|skin.shpk|water.shpk Defaults: Default
        // SubViewKey Unknown: 0x00000001 Shaders: verticalfog.shpk Defaults: Color
        new("Color", 0x61B590F0, NameItemCategory.SubViewValue), // Shaders: verticalfog.shpk
        new("Default", 0xB18FE63D, NameItemCategory.SubViewValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|lightshaft.shpk|river.shpk|skin.shpk|water.shpk
        new("DefaultMemorySpace", 0x8D0CA99A, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("DefaultTechnique", 0x86CA5FE4, NameItemCategory.SubViewValue), // Shaders: GameStrings
        // Category: 0x00000002
        // SubViewKey Unknown: 0x00000002 Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: SUB_VIEW_SHADOW_0
        // SubViewKey Unknown: 0x00000002 Shaders: characterocclusion.shpk|charactertattoo.shpk|river.shpk|water.shpk Defaults: SUB_VIEW_MAIN
        // SubViewKey Unknown: 0x00000002 Shaders: lightshaft.shpk|verticalfog.shpk Defaults: MAIN
        new("SUB_VIEW_SHADOW_0", 0x99B22D1C, NameItemCategory.SubViewValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("MAIN", 0xA8F9FFCC, NameItemCategory.SubViewValue), // Shaders: lightshaft.shpk|verticalfog.shpk
        new("SUB_VIEW_MAIN", 0xF43B2F35, NameItemCategory.SubViewValue), // Shaders: characterocclusion.shpk|charactertattoo.shpk|GameStrings|river.shpk|water.shpk
        new("MAIN_DOF", 0x93D399FA, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("SUB_VIEW_MAIN_DEPTH", 0xB4CC1981, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("SUB_VIEW_MAIN_SELECT", 0x0C0120CA, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("SUB_VIEW_MAIN_TESS", 0x22F7274B, NameItemCategory.SubViewValue), // Shaders: GameStrings
    ];

    private static readonly IReadOnlyList<string> PossibleStrings2 =
    [
        "g_WaveParam_ScatterAlpha",   // 0xd8a98be7
        "g_WaveParam_WhitecapAlpha",  // 0xb46e7be
        "g_TripleWhitecapDistortion", // 0x960450d0
        "g_SingleWhitecapDistortion", // 0x97e83104
        "g_AlphaMultiWeight",         // 0x30b91319
    ];
    
    
    // Manually curated from game strings
    private static readonly IReadOnlyList<string> PossibleStrings =
    [
        "AmbientLightMode",
        "AmbientLightModeCamera",
        "AmbientLightModeLocation",
        "AmbientLighting_Disable",
        "AmbientLighting_Enable",
        "AmbientLighting_EnableMultiply",
        "AmbientLighting_Table",
        "ApplyAlphaClip",
        "ApplyAlphaClipOff",
        "ApplyAlphaClipOn",
        "ApplyAttenuation",
        "ApplyAttenuation_Cubic",
        "ApplyAttenuation_Linear",
        "ApplyAttenuation_Quadratic",
        "ApplyAuraColor",
        "ApplyAuraColorOff",
        "ApplyAuraColorOn",
        "ApplyConeAttenuation",
        "ApplyConeAttenuation_Disable",
        "ApplyConeAttenuation_Enable",
        "ApplyDetailMap",
        "ApplyDetailMap_Disable",
        "ApplyDetailMap_Enable",
        "ApplyDissolveColor",
        "ApplyDissolveColorOff",
        "ApplyDissolveColorOn",
        "ApplyDitherClip",
        "ApplyDitherClipOff",
        "ApplyDitherClipOn",
        "ApplyDynamicWave",
        "ApplyDynamicWaveOff",
        "ApplyDynamicWaveOn",
        "ApplyFog_Alpha",
        "ApplyFog_None",
        "ApplyFog_RGB",
        "ApplyFog_Table",
        "ApplyLightBufferType_Disable",
        "ApplyLightBufferType_Enable",
        "ApplyLightBufferType_Table",
        "ApplyMaskTexture",
        "ApplyMaskTexture_Disable",
        "ApplyMaskTexture_Enable",
        "ApplyOmniShadow",
        "ApplyOmniShadow_Chara",
        "ApplyOmniShadow_Disable",
        "ApplyOmniShadow_Map",
        "ApplyOmniShadow_MapChara",
        "ApplyOmniShadow_MapChara_SSAO",
        "ApplyOmniShadow_Map_SSAO",
        "ApplyOmniShadow_ShadowMask",
        "ApplyShadowMask",
        "ApplyShadowMask_Disable",
        "ApplyShadowMask_Enable",
        "ApplyUnderWater",
        "ApplyUnderWaterOff",
        "ApplyUnderWaterOn",
        "ApplyVehicleForces",
        "ApplyVertexMovement",
        "ApplyVertexMovementOff",
        "ApplyVertexMovementOn",
        "ApplyWavingAnim",
        "ApplyWavingAnimOff",
        "ApplyWavingAnimOn",
        "ApplyWavingAnimation",
        "ApplyWavingAnimation_AutoPlacement",
        "ApplyWavingAnimation_Nothing",
        "ApplyWavingAnimation_Shigemi",
        "CalculateInstancingPosition",
        "CalculateInstancingPosition_Off",
        "CalculateInstancingPosition_On",
        "ComputeFinalColorType_LerpWhite",
        "ComputeFinalColorType_ModulateAlpha",
        "ComputeFinalColorType_NoneControl",
        "ComputeFinalColorType_Table",
        "ComputeSoftParticleAlpha",
        "ComputeSoftParticleAlphaOff",
        "ComputeSoftParticleAlphaOn",
        "ComputeSoftParticleType_Disable",
        "ComputeSoftParticleType_Enable",
        "ComputeSoftParticleType_Table",
        "DirectionalLightType_Ex",
        "DirectionalLightType_HalfLambert",
        "DirectionalLightType_Lambert",
        "DirectionalLightType_Table",
        "DirectionalLight_Disable",
        "DirectionalLight_Enable",
        "DirectionalLight_Table",
        "DynamicWaveCompositeType",
        "DynamicWaveCompositeType_Continue",
        "DynamicWaveCompositeType_ContinueRepulsion",
        "DynamicWaveCompositeType_Reset",
        "DynamicWaveType",
        "DynamicWaveType_Rigid",
        "DynamicWaveType_WindDirectional",
        "DynamicWaveType_WindRadial",
        "DynamicWaveType_WindTornade",
        "EnableLightingChara",
        "EnableLightingCharaOff",
        "EnableLightingCharaOn",
        "EnablePassZ",
        "EnablePassZ_Disable",
        "EnablePassZ_Enable",
        "GetAmbientLight",
        "GetAmbientLight_Color",
        "GetAmbientLight_None",
        "GetAmbientLight_SH",
        "GetAmbientOcclusion",
        "GetAmbientOcclusion_Apply",
        "GetAmbientOcclusion_None",
        "GetDirectionalLight",
        "GetDirectionalLight_Disable",
        "GetDirectionalLight_Enable",
        "GetDirectionalLight_Shadow",
        "GetFakeSpecular",
        "GetFakeSpecular_Disable",
        "GetFakeSpecular_Enable1",
        "GetFakeSpecular_Enable2",
        "GetFakeSpecular_Enable3",
        "GetHairFlow",
        "GetHairFlowOff",
        "GetHairFlowOn",
        "GetLocalPosition",
        "GetLocalPositionNone",
        "GetLocalPositionTerrainEadg",
        "GetMaterialType",
        "GetMaterialTypeColorOnly",
        "GetMaterialTypeDefault",
        "GetMaterialTypeUseNormal",
        "GetNormalMap",
        "GetNormalMapOff",
        "GetNormalMapOn",
        "GetNormalMapParallaxOcclusion",
        "GetRLR",
        "GetRLROff",
        "GetRLROn",
        "GetReflectColor",
        "GetReflectColor_None",
        "GetReflectColor_Texture",
        "GetShadow",
        "GetShadow_Enable",
        "GetUnderWaterLighting",
        "GetUnderWaterLighting_Disable",
        "GetUnderWaterLighting_Enable",
        "GetVelocity",
        "GetVelocityMovable",
        "GetVelocityStatic",
        "GetViewPosition",
        "GetViewPosition_Billboard",
        "GetViewPosition_Default",
        "GlassBlendMode",
        "GlassBlendMode_Add",
        "GlassBlendMode_Mul",
        "LightClip",
        "LightClip_Disable",
        "LightClip_Enable",
        "LightType",
        "Lighting",
        "LightingLow",
        "LightingNormal",
        "LightingType",
        "PointLightCount_0_0",
        "PointLightCount_0_1",
        "PointLightCount_1_0",
        "PointLightCount_1_1",
        "PointLightCount_Table",
        "PointLightPositionType_PerModel",
        "PointLightPositionType_PerPixel",
        "PointLightPositionType_Table",
        "PointLightType_Area",
        "PointLightType_Ex",
        "PointLightType_HalfLambert",
        "PointLightType_Lambert",
        "PointLightType_Table",
        "SampleAndBlend",
        "SampledBoneWeights",
        "SampledBones",
        "SampledFloatWeights",
        "SampledFloats",
        "Sampler_ColorTexture",
        "Sampler_Dither",
        "Sampler_HistoryDepthTexture",
        "Sampler_MaskTextureReadonly",
        "Sampler_SamplerShadow",
        "Sampler_StencilTexture",
        "Sampler_VelocityTexture",
        "Sampler_gCurrTextureIn",
        "Sampler_gCurrentDepthTexInFlt",
        "Sampler_gHistoryTexIn",
        "Sampler_g_edgesTextureReadonly",
        "Sampler_g_screenTextureReadonly",
        "ShaderType",
        "Shading_BG",
        "Shading_Hybrid",
        "Shading_None",
        "Shading_Table",
        "Shading_Uniform",
        "ShadowBgLOD",
        "ShadowCascadeCountType",
        "ShadowCascadeCountType_DX11",
        "ShadowDistanceFadeType",
        "ShadowDistanceFadeType_Disable",
        "ShadowDistanceFadeType_Enable",
        "ShadowLOD",
        "ShadowLOD_DX11",
        "ShadowLightValidType",
        "ShadowSoftShadowType",
        "ShadowSoftShadowType_1x1",
        "ShadowSoftShadowType_3x3",
        "ShadowSoftShadowType_5x5",
        "ShadowSoftShadowType_DX11",
        "ShadowSoftShadowType_PCSS",
        "ShadowTextureSizeType",
        "ShadowTextureSizeType_DX11",
        "ShadowVisibilityType",
        "ShadowVisibilityTypeEnemy",
        "ShadowVisibilityTypeEnemy_DX11",
        "ShadowVisibilityTypeOther",
        "ShadowVisibilityTypeOther_DX11",
        "ShadowVisibilityTypeParty",
        "ShadowVisibilityTypeParty_DX11",
        "ShadowVisibilityTypeSelf",
        "ShadowVisibilityTypeSelf_DX11",
        "SpecularLighting",
        "SpecularLighting_Disable",
        "SpecularLighting_Enable",
        "TransformProj",
        "TransformProjBox",
        "TransformProjPlaneFar",
        "TransformProjPlaneNear",
        "TransformType",
        "TransformTypeBox",
        "TransformTypePlane",
        "TransformView",
        "TransformViewRigid",
        "TransformViewSkin",
        "cAdaptLumParam",
        "cAlphaThreshold",
        "cArraySize",
        "cBlurLength",
        "cBlurStrength",
        "cBrightPassParam",
        "cC-BL",
        "cClearColor",
        "cCloudShadowParam",
        "cCommon",
        "cCommonTexParam",
        "cConstant0",
        "cConstant1",
        "cConstant2",
        "cDarkFilterParam",
        "cDepthThreshold",
        "cDonutMergeParam",
        "cDynamicViewportResolutionParam",
        "cExpHeightFogParam",
        "cFilter",
        "cFilterForDark",
        "cFlag",
        "cFogParam",
        "cFxaaParam",
        "cGaussianBlurParam",
        "cGlareParam",
        "cHDAOParam",
        "cHaloParam",
        "cInst",
        "cLensFlareParam",
        "cLetterBoxParam",
        "cLightParam",
        "cLutParam",
        "cMatrix",
        "cMergeWeight",
        "cMoonParam",
        "cMtxViewToShadowViewProj",
        "cNoiseTables0",
        "cNoiseTables1",
        "cNoiseTables2",
        "cOffset",
        "cParallelBlurParam",
        "cParallelMergeParam",
        "cParam",
        "cPrev",
        "cProjectionMatrix",
        "cQueryParam",
        "cRadialBlurParam",
        "cReprojectionMatrix",
        "cSamplingOffset",
        "cSamplingWeight",
        "cScale",
        "cSkyParam",
        "cSunParam",
        "cToneMapParam",
        "cUnderWaterSunParam",
        "cView",
        "cViewDepthFactor",
        "cViewMatrix",
        "cViewport",
        "cVignettingParam",
        "cVolumeParams",
        "cWeightSamplingParam",
        "cWorldMatrix",
        "cWorldViewProjMatrix",
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
