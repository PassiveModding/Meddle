namespace Meddle.Formats.Constants;

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
        public uint? Key { get; init; }

        public static implicit operator Name(string value) => new(value);
    }

    public record NameItem : ICrcPair
    {
        public NameItem(string KnownName, uint? FixedCrcValue, uint Key, NameItemCategory Category)
        {
            this.Value = KnownName;
            ComputedCrc = Crc32.GetHash(KnownName);
            this.FixedCrcValue = FixedCrcValue;
            this.Key = Key;
            if (this.FixedCrcValue != null && this.FixedCrcValue != ComputedCrc)
            {
                throw new Exception($"Provided CRC {FixedCrcValue} != Computed CRC {ComputedCrc} for {KnownName}");
            }
            this.Category = Category;
        }

        private uint? FixedCrcValue { get; }
        public uint Key { get; }
        private uint ComputedCrc { get; }
        public string Value { get; }
        public uint Crc => FixedCrcValue ?? ComputedCrc;
        
        public NameItemCategory Category { get; }
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
                buffer[constant.Crc] = constant;
            }

            foreach (var stub in StubNames)
            {
                buffer.TryAdd(stub.Crc, stub);
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
        // MaterialParam Unknown: 0x052649EA Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0x052ED035 Shaders: water.shpk Defaults: 0.07
        // MaterialParam Unknown: 0x05C8BD4F Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0x064CBF83 Shaders: water.shpk Defaults: 0.5
        // MaterialParam Unknown: 0x0678C56A Shaders: crystal.shpk Defaults: 
        // MaterialParam Unknown: 0x07C3EFE6 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,1
        // MaterialParam Unknown: 0x0760F7B3 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
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
        // MaterialParam Unknown: 0x4048A102 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0x44EF5418 Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0x45364F70 Shaders: water.shpk Defaults: 0.15
        // MaterialParam Unknown: 0x498092FD Shaders: water.shpk Defaults: 40
        // MaterialParam Unknown: 0x4B9CB34A Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0x4EC3879E Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x5164FA14 Shaders: water.shpk Defaults: 5
        // MaterialParam Unknown: 0x5C598180 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 45
        // MaterialParam Unknown: 0x63747CC4 Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x6514A4DB Shaders: water.shpk Defaults: 300,300
        // MaterialParam Unknown: 0x6A197C9E Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0x6BE878E0 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0x6C159E95 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.85
        // MaterialParam Unknown: 0x6E0A1C94 Shaders: water.shpk Defaults: 0.5
        // MaterialParam Unknown: 0x7067C725 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0x720916BD Shaders: water.shpk Defaults: 0.9
        // MaterialParam Unknown: 0x7221797C Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0x72291E75 Shaders: water.shpk Defaults: 0,500,10,50
        // MaterialParam Unknown: 0x72B002C5 Shaders: water.shpk Defaults: 0.2
        // MaterialParam Unknown: 0x72CF8DD9 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0x7A08F978 Shaders: water.shpk Defaults: 45
        // MaterialParam Unknown: 0x7B086C53 Shaders: water.shpk Defaults: 60,195,190
        // MaterialParam Unknown: 0x7B5813E0 Shaders: water.shpk Defaults: 30,1000,50,100
        // MaterialParam Unknown: 0x7D6268DD Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0x7DB2732C Shaders: water.shpk Defaults: 0
        // MaterialParam Unknown: 0x8500AEA4 Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x852A9263 Shaders: water.shpk Defaults: 70
        // MaterialParam Unknown: 0x8F4D585E Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0x9E8B9C5A Shaders: water.shpk Defaults: 0.05
        // MaterialParam Unknown: 0x9ECABE5C Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,1
        // MaterialParam Unknown: 0xA2A01C0A Shaders: water.shpk Defaults: 0.5
        // MaterialParam Unknown: 0xA90DD1EF Shaders: water.shpk Defaults: 0.1
        // MaterialParam Unknown: 0xAD94E254 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xAE4F649C Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xB1542ADD Shaders: water.shpk Defaults: 15
        // MaterialParam Unknown: 0xB3A7C1B5 Shaders: water.shpk Defaults: 0,500,10,50
        // MaterialParam Unknown: 0xB61D7498 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xB6EEA089 Shaders: water.shpk Defaults: 0
        // MaterialParam Unknown: 0xB8827D5E Shaders: water.shpk Defaults: 500,500
        // MaterialParam Unknown: 0xB8ACCE58 Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 50,100,50,100
        // MaterialParam Unknown: 0xBAD6CC20 Shaders: water.shpk Defaults: 0,1000,50,100
        // MaterialParam Unknown: 0xBFB7646B Shaders: water.shpk Defaults: 0.5
        // MaterialParam Unknown: 0xC582F820 Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0xC70F951E Shaders: water.shpk Defaults: 25
        // MaterialParam Unknown: 0xCBF2CD55 Shaders: water.shpk Defaults: 0.8
        // MaterialParam Unknown: 0xD2F9EC63 Shaders: water.shpk Defaults: 1,0
        // MaterialParam Unknown: 0xD4F2D701 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xD67F62C8 Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        // MaterialParam Unknown: 0xD721E19F Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0xDA8FA72C Shaders: water.shpk Defaults: 1
        // MaterialParam Unknown: 0xDE93031F Shaders: water.shpk Defaults: 0.25
        // MaterialParam Unknown: 0xE2BA75E1 Shaders: water.shpk Defaults: 0,700,10,100
        // MaterialParam Unknown: 0xE9154EAA Shaders: water.shpk Defaults: 0.3
        // MaterialParam Unknown: 0xEBC6DC63 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        // MaterialParam Unknown: 0xEB2828C6 Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0xE9CD8ECA Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,1
        // MaterialParam Unknown: 0xE96E969F Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        // MaterialParam Unknown: 0xF769298E Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.3,0.3,0.3,0.3
        // MaterialParam Unknown: 0xFC9C8BD6 Shaders: water.shpk Defaults: -20
        new("g_AlphaAperture", 0xD62BF368, 0xD62BF368, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 2
        new("g_AlphaMultiParam", 0x07EDA444, 0x07EDA444, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0,0,0
        new("g_AlphaMultiWeight", 0x30B91319, 0x30B91319, NameItemCategory.MaterialParam), // Shaders: bg.shpk Defaults: 
        new("g_AlphaOffset", 0xD07A6A65, 0xD07A6A65, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_AlphaThreshold", 0x29AC0223, 0x29AC0223, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|lightshaft.shpk|river.shpk|skin.shpk|verticalfog.shpk|water.shpk Defaults: 0
        new("g_AmbientOcclusionMask", 0x575ABFB2, 0x575ABFB2, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk|hair.shpk|skin.shpk Defaults: 
        new("g_AngleClip", 0x71DBDA81, 0x71DBDA81, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0
        new("g_CausticsPower", 0x7071F15D, 0x7071F15D, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: [0.75|0.5]
        new("g_CausticsReflectionPowerBright", 0x0CC09E67, 0x0CC09E67, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 0.5
        new("g_CausticsReflectionPowerDark", 0xC295EA6C, 0xC295EA6C, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 0.5
        new("g_Color", 0xD27C58B9, 0xD27C58B9, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk|verticalfog.shpk Defaults: [1,1,1|0.8,0.8,0.8,1]
        new("g_ColorUVScale", 0xA5D02C52, 0xA5D02C52, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1,1
        new("g_DetailColor", 0xDD93D839, 0xDD93D839, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.5,0.5,0.5
        new("g_DetailColorFadeDistance", 0xF3F28C58, 0xF3F28C58, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0
        new("g_DetailColorMipBias", 0xB10AF2DA, 0xB10AF2DA, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0
        new("g_DetailColorUvScale", 0xC63D9716, 0xC63D9716, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 4,4,4,4
        new("g_DetailID", 0x8981D4D9, 0x8981D4D9, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_DetailNormalFadeDistance", 0x236EE793, 0x236EE793, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0
        new("g_DetailNormalMipBias", 0x756DFE22, 0x756DFE22, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0
        new("g_DetailNormalScale", 0x9F42EDA2, 0x9F42EDA2, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_DetailNormalUvScale", 0x025A9BEE, 0x025A9BEE, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 4,4,4,4
        new("g_DetailOmniVerMatrix", 0xB9766DBB, 0xB9766DBB, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 50
        new("g_DiffuseColor", 0x2C2A34DD, 0x2C2A34DD, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1,1,1
        new("g_EmissiveColor", 0x38A64362, 0x38A64362, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,0,0
        new("g_EmissiveGlarePower", 0xBFE9D12D, 0xBFE9D12D, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bgskin.shpk|bguvscroll.shpk|crystal.shpk|terrain.shpk Defaults: 1
        new("g_EnableLightShadow", 0x5095E770, 0x5095E770, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_EnableShadow", 0xBCEA8C11, 0xBCEA8C11, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_EnvMapPower", 0xEEF5665F, 0xEEF5665F, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.85
        new("g_FadeDistance", 0xC7D0DB1A, 0xC7D0DB1A, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 1000,1500
        new("g_Fresnel", 0xE3AA427A, 0xE3AA427A, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 
        new("g_GlassIOR", 0x7801E004, 0x7801E004, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_GlassThicknessMax", 0xC4647F37, 0xC4647F37, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.01
        new("g_Gradation", 0x94B40EEE, 0x94B40EEE, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.5
        new("g_HeightMapScale", 0xA320B199, 0xA320B199, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 0.5
        new("g_HeightMapUVScale", 0x5B99505D, 0x5B99505D, NameItemCategory.MaterialParam), // Shaders: GameStrings|water.shpk Defaults: 0.25
        new("g_HeightScale", 0x8F8B0070, 0x8F8B0070, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.015
        new("g_InclusionAperture", 0xBCA22FD4, 0xBCA22FD4, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_Intensity", 0xBCBA70E1, 0xBCBA70E1, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.01
        new("g_IrisOptionColorEmissiveIntensity", 0x7918D232, 0x7918D232, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_IrisOptionColorEmissiveRate", 0x8EA14846, 0x8EA14846, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_IrisOptionColorRate", 0x29253809, 0x29253809, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_IrisRingColor", 0x50E36D56, 0x50E36D56, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1,1,1
        new("g_IrisRingEmissiveIntensity", 0x7DABA471, 0x7DABA471, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.25
        new("g_IrisRingForceColor", 0x58DE06E2, 0x58DE06E2, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,0,0
        new("g_IrisRingOddRate", 0x285F72D2, 0x285F72D2, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1E-45
        new("g_IrisRingUvFadeWidth", 0x5B608CFE, 0x5B608CFE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.04,0.02
        new("g_IrisRingUvRadius", 0xE18398AE, 0xE18398AE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.158,0.174
        new("g_IrisThickness", 0x66C93D3E, 0x66C93D3E, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.5
        new("g_IrisUvRadius", 0x37DEA328, 0x37DEA328, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.2
        new("g_LayerColor", 0x35DC0B6F, 0x35DC0B6F, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 1,1,1
        new("g_LayerDepth", 0xA9295FEF, 0xA9295FEF, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 10
        new("g_LayerIrregularity", 0x0A00B0A1, 0x0A00B0A1, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.5
        new("g_LayerScale", 0xBFCC6602, 0xBFCC6602, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.01
        new("g_LayerSoftEdge", 0xD04CB491, 0xD04CB491, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 0.05
        new("g_LayerVelocity", 0x72181E22, 0x72181E22, NameItemCategory.MaterialParam), // Shaders: verticalfog.shpk Defaults: 10,0
        new("g_LipRoughnessScale", 0x3632401A, 0x3632401A, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0.7
        new("g_MultiDetailColor", 0x11FD4221, 0x11FD4221, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.5,0.5,0.5
        new("g_MultiDetailID", 0xAC156136, 0xAC156136, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_MultiDetailNormalScale", 0xA83DBDF1, 0xA83DBDF1, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_MultiDiffuseColor", 0x3F8AC211, 0x3F8AC211, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1
        new("g_MultiEmissiveColor", 0xAA676D0F, 0xAA676D0F, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0,0,0
        new("g_MultiHeightScale", 0x43E59A68, 0x43E59A68, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0.015
        new("g_MultiNormalScale", 0x793AC5A3, 0x793AC5A3, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_MultiSpecularColor", 0x86D60CB8, 0x86D60CB8, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1
        new("g_MultiSSAOMask", 0x926E860D, 0x926E860D, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1
        new("g_MultiWaveScale", 0x37363FDD, 0x37363FDD, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk Defaults: 1,1,2,2
        new("g_MultiWhitecapDistortion", 0x93504F3B, 0x93504F3B, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 
        new("g_MultiWhitecapScale", 0x312B69C1, 0x312B69C1, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 4,4
        new("g_NearClip", 0x17A52926, 0x17A52926, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0.25
        new("g_NormalScale", 0xB5545FBB, 0xB5545FBB, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|river.shpk|skin.shpk|water.shpk Defaults: 1
        new("g_NormalScale1", 0x0DD83E61, 0x0DD83E61, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 1
        new("g_NormalUVScale", 0xBB99CF76, 0xBB99CF76, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1,1
        new("g_OutlineColor", 0x623CC4FE, 0x623CC4FE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0,0,0
        new("g_OutlineWidth", 0x8870C938, 0x8870C938, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_PrefersFailure", 0x5394405B, 0x5394405B, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 1,0
        new("g_Ray", 0x827BDD09, 0x827BDD09, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0,0,1
        new("g_ReflectionPower", 0x223A3329, 0x223A3329, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 0.25
        new("g_RefractionColor", 0xBA163700, 0xBA163700, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 0.4117,0.4313,0.4509
        new("g_RLRReflectionPower", 0xF2360709, 0xF2360709, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 0.1
        new("g_ScatteringLevel", 0xB500BB24, 0xB500BB24, NameItemCategory.MaterialParam), // Shaders: bg.shpk|character.shpk|characterlegacy.shpk|hair.shpk Defaults: 
        new("g_ScrollBaseScaleU_1", 0x43345395, 0x43345395, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_ScrollBaseScaleU_2", 0xDA3D022F, 0xDA3D022F, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_ScrollBaseScaleV_1", 0x4172EDCC, 0x4172EDCC, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_ScrollBaseScaleV_2", 0xD87BBC76, 0xD87BBC76, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_ScrollRate_1", 0xDCA5BF7D, 0xDCA5BF7D, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ScrollRate_2", 0x45ACEEC7, 0x45ACEEC7, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ScrollSphereMapIndex_1", 0x72DFF568, 0x72DFF568, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ScrollSphereMapIndex_2", 0xEBD6A4D2, 0xEBD6A4D2, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ScrollTransTimeU_1", 0x738A241C, 0x738A241C, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ScrollTransTimeU_2", 0xEA8375A6, 0xEA8375A6, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ScrollTransTimeV_1", 0x71CC9A45, 0x71CC9A45, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ScrollTransTimeV_2", 0xE8C5CBFF, 0xE8C5CBFF, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_SeaWaveScale", 0xA5FF109A, 0xA5FF109A, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 50
        new("g_ShaderID", 0x59BDA0B1, 0x59BDA0B1, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ShadowAlphaThreshold", 0xD925FF32, 0xD925FF32, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|lightshaft.shpk|river.shpk|skin.shpk|verticalfog.shpk|water.shpk Defaults: 0.5
        new("g_ShadowPosOffset", 0x5351646E, 0x5351646E, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_SheenAperture", 0xF490F76E, 0xF490F76E, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_SheenRate", 0x800EE35F, 0x800EE35F, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_SheenTintRate", 0x1F264897, 0x1F264897, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_Shininess", 0x992869AB, 0x992869AB, NameItemCategory.MaterialParam), // Shaders: hair.shpk|river.shpk|water.shpk Defaults: 
        new("g_SingleWaveScale", 0x5B22E864, 0x5B22E864, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 1,1,2,2
        new("g_SingleWhitecapDistortion", 0x97E83104, 0x97E83104, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 
        new("g_SingleWhitecapScale", 0xB33DB142, 0xB33DB142, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 4,4
        new("g_SoftEadgDistance", 0x2A57C3CC, 0x2A57C3CC, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 0.3
        new("g_SpecularColor", 0x141722D5, 0x141722D5, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1
        new("g_SpecularColorMask", 0xCB0338DC, 0xCB0338DC, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1,1,1
        new("g_SpecularMask", 0x36080AD0, 0x36080AD0, NameItemCategory.MaterialParam), // Shaders: hair.shpk Defaults: 
        new("g_SpecularPower", 0xD9CB6B9C, 0xD9CB6B9C, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 
        new("g_SpecularUVScale", 0x8D03A782, 0x8D03A782, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 1,1,1,1
        new("g_SphereMapID", 0x5106E045, 0x5106E045, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 0
        new("g_SphereMapIndex", 0x074953E9, 0x074953E9, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_SphereMapRate", 0xE211758E, 0xE211758E, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_SSAOMask", 0xB7FA33E2, 0xB7FA33E2, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_TexAnim", 0x14D8E13D, 0x14D8E13D, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0,250
        new("g_TextureMipBias", 0x39551220, 0x39551220, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_TexU", 0x5926A043, 0x5926A043, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0,0,-1
        new("g_TexV", 0xC02FF1F9, 0xC02FF1F9, NameItemCategory.MaterialParam), // Shaders: lightshaft.shpk Defaults: 0,-1,0
        new("g_TileAlpha", 0x12C6AC9F, 0x12C6AC9F, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_TileIndex", 0x4255F2F4, 0x4255F2F4, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_TileMipBiasOffset", 0x6421DD30, 0x6421DD30, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_TileScale", 0x2E60B071, 0x2E60B071, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 16,16
        new("g_ToonIndex", 0xDF15112D, 0xDF15112D, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0
        new("g_ToonLightScale", 0x3CCE9E4C, 0x3CCE9E4C, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 2
        new("g_ToonLightSpecAperture", 0x759036EE, 0x759036EE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 50
        new("g_ToonReflectionScale", 0xD96FAF7A, 0xD96FAF7A, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 2.5
        new("g_ToonSpecIndex", 0x00A680BC, 0x00A680BC, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 4E-45
        new("g_Transparency", 0x53E8417B, 0x53E8417B, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 1
        new("g_TransparencyDistance", 0x1624F841, 0x1624F841, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: [50|100]
        new("g_TripleWhitecapDistortion", 0x960450D0, 0x960450D0, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 
        new("g_TripleWhitecapScale", 0x113BAFDF, 0x113BAFDF, NameItemCategory.MaterialParam), // Shaders: river.shpk Defaults: 8,8
        new("g_UVScrollTime", 0x9A696A17, 0x9A696A17, NameItemCategory.MaterialParam), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: 10,10,10,10
        new("g_VertexMovementMaxLength", 0xD26FF0AE, 0xD26FF0AE, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_VertexMovementScale", 0x641E0F22, 0x641E0F22, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1
        new("g_WaterDeepColor", 0xD315E728, 0xD315E728, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: 0.3529,0.372549,0.3921
        new("g_WaveletDistortion", 0x3439B378, 0x3439B378, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveletFadeDistance", 0x4AD899B7, 0x4AD899B7, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveletNoiseParam", 0x1279815C, 0x1279815C, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveletOffset", 0x9BE8354A, 0x9BE8354A, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 0
        new("g_WaveletScale", 0xD62C681E, 0xD62C681E, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveletSinParam", 0x2F41D796, 0x2F41D796, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WaveParam_NormalScale", 0x592A312C, 0x592A312C, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 2
        new("g_WaveParam_ScatterAlpha", 0xD8A98BE7, 0xD8A98BE7, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 0.1
        new("g_WaveParam_ScatterLengthScale", 0xCEC9B6FF, 0xCEC9B6FF, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 20
        new("g_WaveParam_ScatterMinLength", 0x3FD623A8, 0x3FD623A8, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 4
        new("g_WaveParam_WhitecapAlpha", 0x0B46E7BE, 0x0B46E7BE, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 1
        new("g_WaveParam_WhitecapHeightScale", 0xB88B859A, 0xB88B859A, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 1
        new("g_WaveParam_WhitecapMinHeight", 0xFA124634, 0xFA124634, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 0.5
        new("g_WaveParam_WhitecapNormalPower", 0xC598FE75, 0xC598FE75, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 10
        new("g_WaveSpeed", 0xE4C68FF3, 0xE4C68FF3, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk Defaults: 1,1,1,1
        new("g_WaveTime", 0x8EB9D2A6, 0x8EB9D2A6, NameItemCategory.MaterialParam), // Shaders: GameStrings|river.shpk|water.shpk Defaults: 15
        new("g_WaveTime1", 0x6EE5BF35, 0x6EE5BF35, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 15
        new("g_WhitecapColor", 0x29FA2AC1, 0x29FA2AC1, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: [0.4509,0.4705,0.4901|0.4509,0.4705,0.4901,0.3]
        new("g_WhitecapDistance", 0x5D26B262, 0x5D26B262, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 0.5
        new("g_WhitecapDistortion", 0x61053025, 0x61053025, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 
        new("g_WhitecapNoiseScale", 0x0FF95B0C, 0x0FF95B0C, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 0.1,0.1
        new("g_WhitecapScale", 0xA3EA47AC, 0xA3EA47AC, NameItemCategory.MaterialParam), // Shaders: water.shpk Defaults: 50
        new("g_WhitecapSpeed", 0x408A9CDE, 0x408A9CDE, NameItemCategory.MaterialParam), // Shaders: river.shpk|water.shpk Defaults: [6,3,3|15]
        new("g_WhiteEyeColor", 0x11C90091, 0x11C90091, NameItemCategory.MaterialParam), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 1,1,1
        // Category: 0xEA931ECA
        new("AddLayer", 0xEA931ECA, 0xEA931ECA, NameItemCategory.SceneKey), // Shaders: verticalfog.shpk Defaults: AddLayer0
        new("AddLayer0", 0x5D82881C, 0xEA931ECA, NameItemCategory.SceneValue), // Shaders: GameStrings|verticalfog.shpk
        new("AddLayer1", 0x2A85B88A, 0xEA931ECA, NameItemCategory.SceneValue), // Shaders: GameStrings
        new("AddLayer2", 0xB38CE930, 0xEA931ECA, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xDCFC844E
        new("ApplyAlphaClip", 0xDCFC844E, 0xDCFC844E, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: ApplyAlphaClipOff
        new("ApplyAlphaClipOff", 0x7D5081DF, 0xDCFC844E, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("ApplyAlphaClipOn", 0x59C4E6DB, 0xDCFC844E, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xA9A3EE25
        new("ApplyAlphaTest", 0xA9A3EE25, 0xA9A3EE25, NameItemCategory.MaterialKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: ApplyAlphaTestOff
        new("ApplyAlphaTestOff", 0x5D146A23, 0xA9A3EE25, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk
        new("ApplyAlphaTestOn", 0x72AAA9AE, 0xA9A3EE25, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|lightshaft.shpk
        // Category: 0xE62944E7
        new("ApplyAuraColor", 0xE62944E7, 0xE62944E7, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: ApplyAuraColorOff
        new("ApplyAuraColorOff", 0x96BA161F, 0xE62944E7, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("ApplyAuraColorOn", 0xBD65191E, 0xE62944E7, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x28981633
        new("ApplyDepthWhitecap", 0x28981633, 0x28981633, NameItemCategory.MaterialKey), // Shaders: water.shpk Defaults: ApplyDepthWhitecapOff
        new("ApplyDepthWhitecapOff", 0x28D137F1, 0x28981633, NameItemCategory.MaterialValue), // Shaders: water.shpk
        new("ApplyDepthWhitecapOn", 0xDD54E76C, 0x28981633, NameItemCategory.MaterialValue), // Shaders: bg.shpk|river.shpk|water.shpk
        // Category: 0x6313FD87
        new("ApplyDetailMap", 0x6313FD87, 0x6313FD87, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: ApplyDetailMap_Disable
        new("ApplyDetailMap_Disable", 0x9615E0AB, 0x6313FD87, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|GameStrings
        new("ApplyDetailMap_Enable", 0x7A3D9EFD, 0x6313FD87, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xAD24ACAD
        new("ApplyDissolveColor", 0xAD24ACAD, 0xAD24ACAD, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: ApplyDissolveColorOff
        new("ApplyDissolveColorOff", 0x03A11B1B, 0xAD24ACAD, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("ApplyDissolveColorOn", 0x19229FD6, 0xAD24ACAD, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x8B036665
        new("ApplyDitherClip", 0x8B036665, 0x8B036665, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|river.shpk|skin.shpk|water.shpk Defaults: ApplyDitherClipOff
        new("ApplyDitherClipOff", 0x0802566A, 0x8B036665, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|river.shpk|skin.shpk|water.shpk
        new("ApplyDitherClipOn", 0x61B0CF19, 0x8B036665, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xE5D84BEF
        new("ApplyDynamicWave", 0xE5D84BEF, 0xE5D84BEF, NameItemCategory.SceneKey), // Shaders: bgprop.shpk Defaults: ApplyDynamicWaveOff
        new("ApplyDynamicWaveOff", 0xD58B99E1, 0xE5D84BEF, NameItemCategory.SceneValue), // Shaders: bgprop.shpk|GameStrings
        new("ApplyDynamicWaveOn", 0xE5038D76, 0xE5D84BEF, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x7725989B
        new("ApplyUnderWater", 0x7725989B, 0x7725989B, NameItemCategory.SceneKey), // Shaders: river.shpk|water.shpk Defaults: ApplyUnderWaterOff
        new("ApplyUnderWaterOff", 0xEF6A4182, 0x7725989B, NameItemCategory.SceneValue), // Shaders: GameStrings|river.shpk|water.shpk
        new("ApplyUnderWaterOn", 0xB18A2017, 0x7725989B, NameItemCategory.SceneValue), // Shaders: GameStrings
        // SceneValue Unknown: 0xBF6B8761 Shaders: river.shpk|water.shpk
        // Category: 0x4F4F0636
        new("ApplyVertexColor", 0x4F4F0636, 0x4F4F0636, NameItemCategory.MaterialKey), // Shaders: bg.shpk Defaults: ApplyVertexColorOff
        new("ApplyVertexColorOff", 0x7C6FA05B, 0x4F4F0636, NameItemCategory.MaterialValue), // Shaders: bg.shpk
        new("ApplyVertexColorOn", 0xBD94649A, 0x4F4F0636, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|lightshaft.shpk
        // Category: 0x87D8F48A
        new("ApplyVertexMovement", 0x87D8F48A, 0x87D8F48A, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: ApplyVertexMovementOff
        new("ApplyVertexMovementOff", 0xF8CA223F, 0x87D8F48A, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("ApplyVertexMovementOn", 0xA657DE89, 0x87D8F48A, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xFB7AD5E4
        new("ApplyWavelet", 0xFB7AD5E4, 0xFB7AD5E4, NameItemCategory.MaterialKey), // Shaders: water.shpk Defaults: ApplyWaveletOff
        new("ApplyWaveletOff", 0x0EC4134E, 0xFB7AD5E4, NameItemCategory.MaterialValue), // Shaders: water.shpk
        new("ApplyWaveletOn", 0x1140F45C, 0xFB7AD5E4, NameItemCategory.MaterialValue), // Shaders: water.shpk
        // Category: 0x105C6A52
        new("ApplyWavingAnim", 0x105C6A52, 0x105C6A52, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgprop.shpk Defaults: ApplyWavingAnimOff
        new("ApplyWavingAnimOn", 0xF801B859, 0x105C6A52, NameItemCategory.MaterialValue), // Shaders: bg.shpk|GameStrings
        new("ApplyWavingAnimOff", 0x7E47A68D, 0x105C6A52, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgprop.shpk|GameStrings
        new("ApplyWavingAnimation", 0x764AECE7, 0x105C6A52, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("ApplyWavingAnimation_AutoPlacement", 0x7DDA17B6, 0x105C6A52, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("ApplyWavingAnimation_Nothing", 0x69BA5521, 0x105C6A52, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("ApplyWavingAnimation_Shigemi", 0x772A5C52, 0x105C6A52, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0x4518960B
        new("CalculateInstancingPosition", 0x4518960B, 0x4518960B, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: CalculateInstancingPosition_Off
        new("CalculateInstancingPosition_Off", 0xD5ECB340, 0x4518960B, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("CalculateInstancingPosition_On", 0x82292C76, 0x4518960B, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x40D1481E
        // new("CategoryFlowMapType", 0x40D1481E, 0x40D1481E, NameItemCategory.MaterialKey), // Shaders: character.shpk|characterscroll.shpk Defaults: Standard // Stub
        // new("Standard", 0x337C6BC4, 0x40D1481E, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterinc.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk // Stub
        // new("Flow", 0x71ADA939, 0x40D1481E, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterscroll.shpk // Stub
        // Category: 0xC8BD1DEF
        // new("CategorySpecularType", 0xC8BD1DEF, 0xC8BD1DEF, NameItemCategory.MaterialKey), // Shaders: characterlegacy.shpk Defaults: Default // Stub
        // new("Default", 0x198D11CD, 0xC8BD1DEF, NameItemCategory.MaterialValue), // Shaders: characterlegacy.shpk // Stub
        // new("Mask", 0xA02F4828, 0xC8BD1DEF, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterlegacy.shpk|characterstockings.shpk|hair.shpk // Stub
        // Category: 0x611DA1BE
        new("DebugMode", 0x611DA1BE, 0x611DA1BE, NameItemCategory.SceneKey), // Shaders: water.shpk Defaults: DebugMode_Off
        new("DebugMode_Off", 0x9F10EE69, 0x611DA1BE, NameItemCategory.SceneValue), // Shaders: water.shpk
        // Category: 0x7D155D6D
        new("DebugVertexWave", 0x7D155D6D, 0x7D155D6D, NameItemCategory.SceneKey), // Shaders: water.shpk Defaults: DebugVertexWave_Off
        new("DebugVertexWave_Off", 0xCBF7A4ED, 0x7D155D6D, NameItemCategory.SceneValue), // Shaders: water.shpk
        // Category: 0xE8DA5B62
        new("DrawDepthMode", 0xE8DA5B62, 0xE8DA5B62, NameItemCategory.MaterialKey), // Shaders: characterglass.shpk|charactertransparency.shpk Defaults: DrawDepthMode_Dither
        new("DrawDepthMode_Dither", 0x7B804D6E, 0xE8DA5B62, NameItemCategory.MaterialValue), // Shaders: characterglass.shpk|charactertransparency.shpk
        new("DrawDepthMode_AlphaTest", 0x71A1FBF8, 0xE8DA5B62, NameItemCategory.MaterialValue), // Shaders: characterglass.shpk|charactertransparency.shpk
        // Category: 0xA1CDEFE9
        new("DrawOffscreen", 0xA1CDEFE9, 0xA1CDEFE9, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: DrawOffscreenOff
        new("DrawOffscreenOff", 0x76B07811, 0xA1CDEFE9, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("DrawOffscreenOn", 0x62E20914, 0xA1CDEFE9, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x0033C8B5
        new("EnableLighting", 0x0033C8B5, 0x0033C8B5, NameItemCategory.MaterialKey), // Shaders: charactertransparency.shpk Defaults: EnableLightingOn
        new("EnableLightingOff", 0x93D6C21A, 0x0033C8B5, NameItemCategory.MaterialValue), // Shaders: charactertransparency.shpk
        new("EnableLightingOn", 0xD1E60FD9, 0x0033C8B5, NameItemCategory.MaterialValue), // Shaders: charactertransparency.shpk
        new("EnableLightingChara", 0xC29B0C9C, 0x0033C8B5, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("EnableLightingCharaOff", 0xD3D18627, 0x0033C8B5, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("EnableLightingCharaOn", 0x09A95433, 0x0033C8B5, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0x086F8E39
        new("GetInstanceData", 0x086F8E39, 0x086F8E39, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: GeometryInstancingOn
        new("GeometryInstancingOn", 0x815446B5, 0x086F8E39, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|GameStrings
        new("GetInstanceData_Geometry", 0xF1636233, 0x086F8E39, NameItemCategory.SceneValue), // Shaders: GameStrings
        new("GetInstancingData_Bush", 0xC011FE92, 0x086F8E39, NameItemCategory.SceneValue), // Shaders: grass.shpk
        new("GetNoInstancingData_Bush", 0x4855866D, 0x086F8E39, NameItemCategory.SceneValue), // Shaders: grass.shpk
        new("GetInstanceData_Grass", 0x12C313B3, 0x086F8E39, NameItemCategory.SceneValue), // Shaders: grass.shpk
        // Category: 0xB616DC5A
        new("GetValues", 0xB616DC5A, 0xB616DC5A, NameItemCategory.MaterialKey), // Shaders: bg.shpk|bguvscroll.shpk Defaults: GetSingleValues
        new("GetValues", 0xB616DC5A, 0xB616DC5A, NameItemCategory.MaterialKey), // Shaders: character.shpk|characterinc.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk Defaults: GetValuesMultiMaterial
        new("GetValues", 0xB616DC5A, 0xB616DC5A, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetMultiValues
        new("GetMultiValues", 0x1DF2985C, 0xB616DC5A, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgcrestchange.shpk|bgprop.shpk|crystal.shpk|river.shpk|water.shpk
        new("GetValuesMultiMaterial", 0x5CC605B5, 0xB616DC5A, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterinc.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk
        new("GetValuesCompatibility", 0x600EF9DF, 0xB616DC5A, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterinc.shpk|characterlegacy.shpk|characterscroll.shpk|charactertransparency.shpk|hair.shpk|skin.shpk
        new("GetSingleValues", 0x669A451B, 0xB616DC5A, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bguvscroll.shpk
        new("GetAlphaMultiValues3", 0x939DE7BD, 0xB616DC5A, NameItemCategory.MaterialValue), // Shaders: bguvscroll.shpk|lightshaft.shpk
        new("GetAlphaMultiValues", 0x941820BE, 0xB616DC5A, NameItemCategory.MaterialValue), // Shaders: bg.shpk
        new("GetAlphaMultiValues2", 0xE49AD72B, 0xB616DC5A, NameItemCategory.MaterialValue), // Shaders: bguvscroll.shpk|lightshaft.shpk
        // Category: 0x8955127D
        new("GetAmbientLight", 0x8955127D, 0x8955127D, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: GetAmbientLight_Color
        new("GetAmbientLight_Color", 0x23C62FB0, 0x8955127D, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|GameStrings
        new("GetAmbientLight_None", 0x3FC15CC4, 0x8955127D, NameItemCategory.SceneValue), // Shaders: GameStrings
        new("GetAmbientLight_SH", 0xB1AD809A, 0x8955127D, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x59C8D2C1
        new("GetCrystalType", 0x59C8D2C1, 0x59C8D2C1, NameItemCategory.MaterialKey), // Shaders: crystal.shpk Defaults: GetCrystalTypeEnvMap
        new("GetCrystalTypeEnvMap", 0x9A77AA04, 0x59C8D2C1, NameItemCategory.MaterialValue), // Shaders: crystal.shpk
        new("GetCrystalTypeSphereMapCustum", 0xB29E6018, 0x59C8D2C1, NameItemCategory.MaterialValue), // Shaders: crystal.shpk
        new("GetCrystalTypeSphereMap", 0xE3531F26, 0x59C8D2C1, NameItemCategory.MaterialValue), // Shaders: bg.shpk|crystal.shpk
        // Category: 0xD2777173
        new("GetDecalColor", 0xD2777173, 0xD2777173, NameItemCategory.MaterialKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: GetDecalColorOff
        new("GetDecalColorOff", 0x4242B842, 0xD2777173, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk
        new("GetDecalColorAlpha", 0x584265DD, 0xD2777173, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterlegacy.shpk|charactertattoo.shpk|skin.shpk
        new("GetDecalColorRGBA", 0xF35F5131, 0xD2777173, NameItemCategory.MaterialValue), // Shaders: character.shpk|characterglass.shpk|characterlegacy.shpk|characterocclusion.shpk|charactertattoo.shpk
        // Category: 0x63030C80
        new("GetDiffuseTex", 0x63030C80, 0x63030C80, NameItemCategory.MaterialKey), // Shaders: iris.shpk Defaults: GetDiffuseTexOff
        new("GetDiffuseTexOff", 0x3839C7E4, 0x63030C80, NameItemCategory.MaterialValue), // Shaders: iris.shpk
        new("GetDiffuseTexOn", 0xEFDEA8F6, 0x63030C80, NameItemCategory.MaterialValue), // Shaders: iris.shpk
        // Category: 0xDBD08C23
        new("GetFadeAlpha", 0xDBD08C23, 0xDBD08C23, NameItemCategory.MaterialKey), // Shaders: verticalfog.shpk Defaults: GetFadeAlphaNone
        new("GetFadeAlphaNone", 0x34775C9A, 0xDBD08C23, NameItemCategory.MaterialValue), // Shaders: verticalfog.shpk
        new("GetFadeAlphaDistance", 0x549290EA, 0xDBD08C23, NameItemCategory.MaterialValue), // Shaders: verticalfog.shpk
        new("GetFadeAlphaVertex", 0x0D3994E3, 0xDBD08C23, NameItemCategory.MaterialValue), // Shaders: verticalfog.shpk
        new("GetFadeAlphaBoth", 0xB03D6544, 0xDBD08C23, NameItemCategory.MaterialValue), // Shaders: verticalfog.shpk
        // Category: 0xCD1484E7
        new("GetHairFlow", 0xCD1484E7, 0xCD1484E7, NameItemCategory.SceneKey), // Shaders: character.shpk|hair.shpk Defaults: GetHairFlowOff
        new("GetHairFlowOff", 0x05288D6B, 0xCD1484E7, NameItemCategory.SceneValue), // Shaders: character.shpk|GameStrings|hair.shpk
        new("GetHairFlowOn", 0x2601F093, 0xCD1484E7, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xBB30A69D
        new("GetLocalPosition", 0xBB30A69D, 0xBB30A69D, NameItemCategory.SceneKey), // Shaders: bg.shpk Defaults: GetLocalPositionNone
        new("GetLocalPositionNone", 0xEFCC34B1, 0xBB30A69D, NameItemCategory.SceneValue), // Shaders: bg.shpk|GameStrings
        new("GetLocalPositionTerrainEadg", 0xCF415365, 0xBB30A69D, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x380CAED0
        new("GetMaterialValue", 0x380CAED0, 0x380CAED0, NameItemCategory.MaterialKey), // Shaders: skin.shpk Defaults: GetMaterialValueFace
        new("GetMaterialValueBody", 0x2BDB45F1, 0x380CAED0, NameItemCategory.MaterialValue), // Shaders: skin.shpk
        new("GetMaterialValueBodyJJM", 0x57FF3B64, 0x380CAED0, NameItemCategory.MaterialValue), // Shaders: skin.shpk
        new("GetMaterialValueFaceEmissive", 0x72E697CD, 0x380CAED0, NameItemCategory.MaterialValue), // Shaders: skin.shpk
        new("GetMaterialValueFace", 0xF5673524, 0x380CAED0, NameItemCategory.MaterialValue), // Shaders: skin.shpk
        new("GetMaterialValueFace2", 0xF421D264, 0x380CAED0, NameItemCategory.MaterialValue), // Shaders: skin.shpk
        // Category: 0xEC806138
        new("GetMultiWhitecap", 0xEC806138, 0xEC806138, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetMultiWhitecapOn
        new("GetMultiWhitecapOn", 0xF0C11E20, 0xEC806138, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        // Category: 0xCBDFD5EC
        new("GetNormalMap", 0xCBDFD5EC, 0xCBDFD5EC, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgprop.shpk Defaults: GetNormalMapOff
        new("GetNormalMapOff", 0xA66B15A1, 0xCBDFD5EC, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgprop.shpk|bguvscroll.shpk|GameStrings
        new("GetNormalMapOn", 0xD9994EF1, 0xCBDFD5EC, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("GetNormalMapParallaxOcclusion", 0xD9FD8A1C, 0xCBDFD5EC, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0xE041892A
        new("GetReflectionPower", 0xE041892A, 0xE041892A, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetReflectionPowerOff
        new("GetReflectionPowerOn", 0x26E40878, 0xE041892A, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        new("GetReflectionPowerOff", 0x32F05363, 0xE041892A, NameItemCategory.MaterialValue), // Shaders: river.shpk
        // Category: 0x4A323184
        new("GetRefractionMask", 0x4A323184, 0x4A323184, NameItemCategory.MaterialKey), // Shaders: water.shpk Defaults: GetRefractionMaskDepth
        new("GetRefractionMaskDepth", 0xDCC8DB97, 0x4A323184, NameItemCategory.MaterialValue), // Shaders: water.shpk
        new("GetRefractionMaskPlane", 0xE7D8EA7E, 0x4A323184, NameItemCategory.MaterialValue), // Shaders: river.shpk
        // Category: 0xB5B1C44A
        new("GetRefractionPower", 0xB5B1C44A, 0xB5B1C44A, NameItemCategory.MaterialKey), // Shaders: river.shpk|water.shpk Defaults: GetRefractionPowerOn
        new("GetRefractionPowerOn", 0x4B740B02, 0xB5B1C44A, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        new("GetRefractionPowerOff", 0x824D5B42, 0xB5B1C44A, NameItemCategory.MaterialValue), // Shaders: bg.shpk|river.shpk|water.shpk
        // Category: 0x11433F2D
        new("GetRLR", 0x11433F2D, 0x11433F2D, NameItemCategory.SceneKey), // Shaders: river.shpk|water.shpk Defaults: GetRLROff
        new("GetRLROff", 0x6B2E2D05, 0x11433F2D, NameItemCategory.SceneValue), // Shaders: GameStrings|river.shpk|water.shpk
        new("GetRLROn", 0x4BA77904, 0x11433F2D, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xABDA6DFB
        new("GetSingleWhitecap", 0xABDA6DFB, 0xABDA6DFB, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetSingleWhitecapOn
        new("GetSingleWhitecapOn", 0x19A091DC, 0xABDA6DFB, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        // Category: 0x24826489
        new("GetSubColor", 0x24826489, 0x24826489, NameItemCategory.MaterialKey), // Shaders: hair.shpk Defaults: GetSubColorHair
        new("GetSubColorFace", 0x6E5B8F10, 0x24826489, NameItemCategory.MaterialValue), // Shaders: characterocclusion.shpk|charactertattoo.shpk|hair.shpk
        new("GetSubColorHair", 0xF7B8956E, 0x24826489, NameItemCategory.MaterialValue), // Shaders: hair.shpk
        // MaterialValue Unknown: 0x4B3D7863 Shaders: hair.shpk
        // Category: 0x6E8CE685
        new("GetTripleWhitecap", 0x6E8CE685, 0x6E8CE685, NameItemCategory.MaterialKey), // Shaders: river.shpk Defaults: GetTripleWhitecapOn
        new("GetTripleWhitecapOn", 0xD2765A5A, 0x6E8CE685, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        // Category: 0x477E3A17
        new("GetVelocity", 0x477E3A17, 0x477E3A17, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk Defaults: GetVelocityStatic
        new("GetVelocityStatic", 0x3DD9525E, 0x477E3A17, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|GameStrings
        new("GetVelocityMovable", 0xD10C3AAC, 0x477E3A17, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xF8EF655E
        new("GetWaterColor", 0xF8EF655E, 0xF8EF655E, NameItemCategory.MaterialKey), // Shaders: river.shpk|water.shpk Defaults: GetWaterColorDistance
        new("GetWaterColorDepth", 0x08404EC3, 0xF8EF655E, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        new("GetWaterColorDistance", 0x86B217C3, 0xF8EF655E, NameItemCategory.MaterialValue), // Shaders: river.shpk|water.shpk
        new("GetWaterColorWaterDepth", 0xE6A6AD27, 0xF8EF655E, NameItemCategory.MaterialValue), // Shaders: bg.shpk|river.shpk|water.shpk
        // Category: 0x9F2A6183
        new("GlassBlendMode", 0x9F2A6183, 0x9F2A6183, NameItemCategory.SceneKey), // Shaders: characterglass.shpk|charactertransparency.shpk Defaults: GlassBlendMode_Mul
        new("GlassBlendMode_Mul", 0x44425B98, 0x9F2A6183, NameItemCategory.SceneValue), // Shaders: characterglass.shpk|charactertransparency.shpk|GameStrings
        new("GlassBlendMode_Add", 0x105A09DE, 0x9F2A6183, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x575CA84C
        new("LightingLow", 0x2807B89E, 0x575CA84C, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bgprop.shpk|bguvscroll.shpk|GameStrings|lightshaft.shpk
        new("LightingNormal", 0x470E5A1E, 0x575CA84C, NameItemCategory.MaterialValue), // Shaders: GameStrings
        new("LightingType", 0xE8001A59, 0x575CA84C, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0x607399CA
        new("ReflectionMapType", 0x607399CA, 0x607399CA, NameItemCategory.SceneKey), // Shaders: river.shpk|water.shpk Defaults: ReflectionMapTypeSingle
        new("ReflectionMapTypeSingle", 0x21F13F6D, 0x607399CA, NameItemCategory.SceneValue), // Shaders: GameStrings|river.shpk|water.shpk
        new("ReflectionMapTypeArray", 0xF648D554, 0x607399CA, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0xA5A1910D
        new("TransformView", 0xA5A1910D, 0xA5A1910D, NameItemCategory.SceneKey), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: TransformViewRigid
        new("TransformViewRigid", 0x4123B1A3, 0xA5A1910D, NameItemCategory.SceneValue), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("TransformViewSkin", 0x9C14C8E9, 0xA5A1910D, NameItemCategory.SceneValue), // Shaders: GameStrings
        // Category: 0x0DA8270B
        new("Type", 0x0DA8270B, 0x0DA8270B, NameItemCategory.MaterialKey), // Shaders: lightshaft.shpk Defaults: Type0
        new("Type0", 0xB1064103, 0x0DA8270B, NameItemCategory.MaterialValue), // Shaders: lightshaft.shpk
        new("Type1", 0xC6017195, 0x0DA8270B, NameItemCategory.MaterialValue), // Shaders: lightshaft.shpk
        new("Types", 0x5ED35129, 0x0DA8270B, NameItemCategory.MaterialValue), // Shaders: GameStrings
        // Category: 0x0B59CEE7
        new("GetSpecularOff", 0x07D3170F, 0x0B59CEE7, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bguvscroll.shpk
        // Category: 0x115CB66A
        // MaterialValue Unknown: 0xF02D5351 Shaders: river.shpk|water.shpk
        // Category: 0x1A43D949
        new("GetDiffuseMapOff", 0x2CD41A98, 0x1A43D949, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bguvscroll.shpk
        // Category: 0x36F72D5F
        // MaterialKey Unknown: 0x36F72D5F Shaders: bg.shpk Defaults: 0x88A3965A
        // MaterialValue Unknown: 0x1E314009 Shaders: bg.shpk
        // MaterialValue Unknown: 0x6936709F Shaders: bg.shpk
        // MaterialValue Unknown: 0x88A3965A Shaders: bg.shpk
        // MaterialValue Unknown: 0x9807BAC4 Shaders: bg.shpk
        // Category: 0x9E45B87D
        new("VertexWave", 0x9E45B87D, 0x9E45B87D, NameItemCategory.MaterialKey), // Shaders: water.shpk Defaults: VertexWave_Off
        // MaterialValue Unknown: 0x0DE05A8D Shaders: water.shpk
        // MaterialValue Unknown: 0xB18254EB Shaders: water.shpk
        new("VertexWave_Off", 0xB77F0FAF, 0x9E45B87D, NameItemCategory.MaterialValue), // Shaders: water.shpk
        // Category: 0xBFC2E0F7
        new("GetSpecularMapOff", 0x772FF72B, 0xBFC2E0F7, NameItemCategory.MaterialValue), // Shaders: bg.shpk|bguvscroll.shpk
        // Category: 0xF52CCF05
        // Category: 0xF886E10E
        new("GetUvScrollTexture", 0xF886E10E, 0xF886E10E, NameItemCategory.MaterialKey), // Shaders: characterscroll.shpk Defaults: 0x69EB4AE0
        new("GetUvScrollTexture_SphereMap_SphereMap", 0x69EB4AE0, 0xF886E10E, NameItemCategory.MaterialValue), // Shaders: characterscroll.shpk
        new("GetUvScrollTexture_CatchLight_CatchLight", 0x9A8A46F5, 0xF886E10E, NameItemCategory.MaterialValue), // Shaders: characterscroll.shpk
        new("GetUvScrollTexture_None", 0xAC8BBC4C, 0xF886E10E, NameItemCategory.MaterialValue), // Shaders: charactermultitile.shpk
        new("GetUvScrollTexture_CatchLight_SphereMap", 0xD21B9BED, 0xF886E10E, NameItemCategory.MaterialValue), // Shaders: charactermultitile.shpk|characterscroll.shpk
        // Category: 0x6448E37B
        new("GetMaterialParameter", 0x6448E37B, 0x6448E37B, NameItemCategory.SceneKey), // Shaders: river.shpk Defaults: 0x812D4365
        new("GetMaterialParameter", 0x6448E37B, 0x6448E37B, NameItemCategory.SceneKey), // Shaders: water.shpk Defaults: 0xD6294FD5
        // SceneValue Unknown: 0x812D4365 Shaders: river.shpk
        new("GetMaterialParameter_Material", 0xD6294FD5, 0x6448E37B, NameItemCategory.SceneValue), // Shaders: water.shpk
        // Category: 0xBEA6525E
        new("DisableEyeDepth", 0xBEA6525E, 0xBEA6525E, NameItemCategory.SceneKey), // Shaders: iris.shpk Defaults: 0x3DAAF8BE
        new("DisableEyeDepthOff", 0x3DAAF8BE, 0xBEA6525E, NameItemCategory.SceneValue), // Shaders: iris.shpk
        new("DisableEyeDepthOn", 0x7CB6E131, 0xBEA6525E, NameItemCategory.SceneValue), // Shaders: iris.shpk
        // Category: 0x00000001
        // SubViewKey Unknown: 0x00000001 Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|lightshaft.shpk|river.shpk|skin.shpk|water.shpk Defaults: Default
        // SubViewKey Unknown: 0x00000001 Shaders: verticalfog.shpk Defaults: Color
        new("Color", 0x61B590F0, 0x00000001, NameItemCategory.SubViewValue), // Shaders: verticalfog.shpk
        new("Default", 0xB18FE63D, 0x00000001, NameItemCategory.SubViewValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|lightshaft.shpk|river.shpk|skin.shpk|water.shpk
        new("DefaultMemorySpace", 0x8D0CA99A, 0x00000001, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("DefaultTechnique", 0x86CA5FE4, 0x00000001, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("Depth", 0xFD40C470, 0x00000001, NameItemCategory.SubViewValue), // Shaders: verticalfog.shpk
        // SubViewValue Unknown: 0x02209404 Shaders: createviewposition.shpk
        // SubViewValue Unknown: 0x1536FAC1 Shaders: weather.shpk
        // SubViewValue Unknown: 0x15AEB407 Shaders: characterinc.shpk
        // SubViewValue Unknown: 0x3624FA8F Shaders: weather.shpk
        // SubViewValue Unknown: 0x5CF29B55 Shaders: grass.shpk
        // SubViewValue Unknown: 0xA2F76B97 Shaders: cloud.shpk
        // SubViewValue Unknown: 0xB7F5D591 Shaders: weather.shpk
        // SubViewValue Unknown: 0xCCCCCBA0 Shaders: createviewposition.shpk
        // SubViewValue Unknown: 0xD9D58038 Shaders: cloud.shpk
        // SubViewValue Unknown: 0xE252291F Shaders: createviewposition.shpk
        // Category: 0x00000002
        // SubViewKey Unknown: 0x00000002 Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: SUB_VIEW_SHADOW_0
        // SubViewKey Unknown: 0x00000002 Shaders: characterocclusion.shpk|charactertattoo.shpk|river.shpk|water.shpk Defaults: SUB_VIEW_MAIN
        // SubViewKey Unknown: 0x00000002 Shaders: lightshaft.shpk|verticalfog.shpk Defaults: MAIN
        new("SUB_VIEW_SHADOW_0", 0x99B22D1C, 0x00000002, NameItemCategory.SubViewValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertransparency.shpk|crystal.shpk|GameStrings|hair.shpk|iris.shpk|skin.shpk
        new("MAIN", 0xA8F9FFCC, 0x00000002, NameItemCategory.SubViewValue), // Shaders: lightshaft.shpk|verticalfog.shpk
        new("SUB_VIEW_MAIN", 0xF43B2F35, 0x00000002, NameItemCategory.SubViewValue), // Shaders: characterocclusion.shpk|charactertattoo.shpk|GameStrings|river.shpk|water.shpk
        new("MAIN_DOF", 0x93D399FA, 0x00000002, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("SUB_VIEW_MAIN_DEPTH", 0xB4CC1981, 0x00000002, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("SUB_VIEW_MAIN_SELECT", 0x0C0120CA, 0x00000002, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("SUB_VIEW_MAIN_TESS", 0x22F7274B, 0x00000002, NameItemCategory.SubViewValue), // Shaders: GameStrings
        new("SHADOW", 0x344CE408, 0x00000002, NameItemCategory.SubViewValue), // Shaders: cloud.shpk
        new("SUB_VIEW_CUBE_0", 0x66244231, 0x00000002, NameItemCategory.SubViewValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bgskin.shpk|bguvscroll.shpk|character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertransparency.shpk|crystal.shpk|hair.shpk|iris.shpk|skin.shpk|terrain.shpk
        new("SUB_VIEW_ROOF", 0xAE5E6A42, 0x00000002, NameItemCategory.SubViewValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bgskin.shpk|bguvscroll.shpk|crystal.shpk|terrain.shpk
        // SubViewValue Unknown: 0x01EF9A04 Shaders: subsurfaceblur.shpk
        // SubViewValue Unknown: 0x1CA2DD46 Shaders: watercaustics.shpk
        // SubViewValue Unknown: 0x3D355377 Shaders: watercaustics.shpk
        // SubViewValue Unknown: 0x69AA9D39 Shaders: planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // SubViewValue Unknown: 0x77E130EA Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // SubViewValue Unknown: 0xA587D200 Shaders: planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // SubViewValue Unknown: 0xE1456E37 Shaders: watercaustics.shpk
        // Category: 0x00C90678
        new("TextureDistortion_UvSet2_Table", 0x00C90678, 0x00C90678, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureDistortion_UvSet_Disable
        // Category: 0x01AD1518
        new("TextureColor2_Table", 0x01AD1518, 0x01AD1518, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor2_Disable
        new("TextureColor2_Disable", 0x3A7EA2AA, 0x01AD1518, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor2_Enable", 0xAD86DFD6, 0x01AD1518, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x094D2909
        new("TextureNormal_Table", 0x094D2909, 0x094D2909, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureNormal_Disable
        new("TextureNormal_Disable", 0x46C8F5DD, 0x094D2909, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureNormal_Enable", 0xC73E1F18, 0x094D2909, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk
        // Category: 0x09500613
        new("TransformProj", 0x09500613, 0x09500613, NameItemCategory.SceneKey), // Shaders: directionalshadow.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk Defaults: TransformProjPlaneNear
        new("TransformProjBox", 0xECF43C9A, 0x09500613, NameItemCategory.SceneValue), // Shaders: directionalshadow.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("TransformProjPlaneFar", 0xD6E21545, 0x09500613, NameItemCategory.SceneValue), // Shaders: directionalshadow.shpk
        new("TransformProjPlaneNear", 0x1B3986BB, 0x09500613, NameItemCategory.SceneValue), // Shaders: directionalshadow.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // Category: 0x0A0814D3
        new("TextureColor4_ColorToAlpha_Table", 0x0A0814D3, 0x0A0814D3, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor4_ColorToAlpha_Off
        new("TextureColor4_ColorToAlpha_Off", 0x8F96D86E, 0x0A0814D3, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor4_ColorToAlpha_On", 0xFD9D1988, 0x0A0814D3, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x0BD07791
        new("GetColor", 0x0BD07791, 0x0BD07791, NameItemCategory.SceneKey), // Shaders: cloud.shpk|weather.shpk Defaults: 0xF77E7B7F|GetColor0
        new("GetColor0", 0xA1DA4309, 0x0BD07791, NameItemCategory.SceneValue), // Shaders: cloud.shpk
        new("GetColor1", 0xD6DD739F, 0x0BD07791, NameItemCategory.SceneValue), // Shaders: cloud.shpk
        new("GetColor_Texture", 0xF3BA7D0E, 0x0BD07791, NameItemCategory.SceneValue), // Shaders: weather.shpk
        new("GetColor_Material", 0xF77E7B7F, 0x0BD07791, NameItemCategory.SceneValue), // Shaders: weather.shpk
        // Category: 0x0D812FA4
        new("SpecularLighting", 0x0D812FA4, 0x0D812FA4, NameItemCategory.SceneKey), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk Defaults: SpecularLighting_Disable
        new("SpecularLighting_Disable", 0xAB1CE916, 0x0D812FA4, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("SpecularLighting_Enable", 0xABA1F498, 0x0D812FA4, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // Category: 0x1314FE08
        new("DynamicWaveCompositeType", 0x1314FE08, 0x1314FE08, NameItemCategory.SceneKey), // Shaders: grassdynamicwave.shpk Defaults: 0x70EA6CC6
        // SceneValue Unknown: 0x65B675CF Shaders: grassdynamicwave.shpk
        // SceneValue Unknown: 0x70EA6CC6 Shaders: grassdynamicwave.shpk
        // SceneValue Unknown: 0xC15B62CE Shaders: grassdynamicwave.shpk
        // Category: 0x134A17A0
        new("Transform", 0x134A17A0, 0x134A17A0, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bgskin.shpk|bguvscroll.shpk|crystal.shpk|terrain.shpk Defaults: TransformRigid
        new("TransformRigid", 0x168E351C, 0x134A17A0, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bguvscroll.shpk|crystal.shpk|terrain.shpk
        new("TransformSkin", 0x5925529D, 0x134A17A0, NameItemCategory.SceneValue), // Shaders: bgskin.shpk
        // Category: 0x1395DE0A
        new("TextureColor2_UvNo_Table", 0x1395DE0A, 0x1395DE0A, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor2_Uv_0
        new("TextureColor2_Uv_0", 0xDF8499D5, 0x1395DE0A, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor2_Uv_1", 0xA883A943, 0x1395DE0A, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor2_Uv_2", 0x318AF8F9, 0x1395DE0A, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor2_Uv_3", 0x468DC86F, 0x1395DE0A, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x152531DE
        new("DepthOffsetType_Table", 0x152531DE, 0x152531DE, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: DepthOffsetType_Legacy
        new("DepthOffsetType_FixedIntervalNDC", 0x051B89C4, 0x152531DE, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("DepthOffsetType_Legacy", 0x3BFEF1AB, 0x152531DE, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x1F6F0483
        new("ComputeSoftParticleType_Table", 0x1F6F0483, 0x1F6F0483, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: ComputeSoftParticleType_Disable
        new("ComputeSoftParticleType_Disable", 0xC27BCF4B, 0x1F6F0483, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("ComputeSoftParticleType_Enable", 0xCA45570B, 0x1F6F0483, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // SceneValue Unknown: 0xBA048F2E Shaders: apricot_fogmodel.shpk
        // Category: 0x2182C4CC
        new("TextureColor1_ColorToAlpha_Table", 0x2182C4CC, 0x2182C4CC, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor1_ColorToAlpha_Off
        new("TextureColor1_ColorToAlpha_Off", 0x6B6CB989, 0x2182C4CC, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor1_ColorToAlpha_On", 0x0933F504, 0x2182C4CC, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x295712ED
        new("TextureDistortion_UvNo_Table", 0x295712ED, 0x295712ED, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureDistortion_Uv_0
        new("TextureDistortion_Uv_0", 0x9FBC8583, 0x295712ED, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureDistortion_Uv_1", 0xE8BBB515, 0x295712ED, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureDistortion_Uv_2", 0x71B2E4AF, 0x295712ED, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureDistortion_Uv_3", 0x06B5D439, 0x295712ED, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x2BD1F674
        new("ForceFarZ_Table", 0x2BD1F674, 0x2BD1F674, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: ForceFarZ_Disable
        new("ForceFarZ_Disable", 0xCA351FBF, 0x2BD1F674, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("ForceFarZ_Enable", 0xE9A88E46, 0x2BD1F674, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x30450F85
        new("TextureColor1_Table", 0x30450F85, 0x30450F85, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor1_Disable|TextureColor1_Enable
        new("TextureColor1_Disable", 0x03F39E6F, 0x30450F85, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor1_Enable", 0x2309D835, 0x30450F85, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x30BA6874
        new("BgObjectInfinite", 0x30BA6874, 0x30BA6874, NameItemCategory.SceneKey), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bgskin.shpk|bguvscroll.shpk|crystal.shpk|terrain.shpk Defaults: BgObjectInfiniteOff
        new("BgObjectInfiniteOff", 0x39D8B930, 0x30BA6874, NameItemCategory.SceneValue), // Shaders: bg.shpk|bgcolorchange.shpk|bgcrestchange.shpk|bgprop.shpk|bgskin.shpk|bguvscroll.shpk|crystal.shpk|terrain.shpk
        // Category: 0x31211CE5
        new("TextureDistortion_UvSet1_Table", 0x31211CE5, 0x31211CE5, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureDistortion_UvSet_Disable
        new("TextureDistortion_UvSet_Enable", 0xA374AA87, 0x31211CE5, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x3312B7E1
        new("ShadowDistanceFadeType", 0x3312B7E1, 0x3312B7E1, NameItemCategory.SceneKey), // Shaders: directionalshadow.shpk Defaults: ShadowDistanceFadeType_Disable
        new("ShadowDistanceFadeType_Disable", 0x93A57FCE, 0x3312B7E1, NameItemCategory.SceneValue), // Shaders: directionalshadow.shpk
        new("ShadowDistanceFadeType_Enable", 0xCAF5E83A, 0x3312B7E1, NameItemCategory.SceneValue), // Shaders: directionalshadow.shpk
        // Category: 0x38F81F3C
        new("TextureColor1_Decode_Table", 0x38F81F3C, 0x38F81F3C, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor1_Decode_Off
        new("TextureColor1_Decode_Off", 0xEA226A56, 0x38F81F3C, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor1_Decode_On", 0xF9F6E7D0, 0x38F81F3C, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x3C957CD3
        new("GetFakeSpecular", 0x3C957CD3, 0x3C957CD3, NameItemCategory.SceneKey), // Shaders: directionallighting.shpk Defaults: GetFakeSpecular_Disable
        new("GetFakeSpecular_Disable", 0x1BB2530E, 0x3C957CD3, NameItemCategory.SceneValue), // Shaders: directionallighting.shpk
        new("GetFakeSpecular_Enable1", 0x1F0F00A0, 0x3C957CD3, NameItemCategory.SceneValue), // Shaders: directionallighting.shpk
        new("GetFakeSpecular_Enable2", 0x8606511A, 0x3C957CD3, NameItemCategory.SceneValue), // Shaders: directionallighting.shpk
        new("GetFakeSpecular_Enable3", 0xF101618C, 0x3C957CD3, NameItemCategory.SceneValue), // Shaders: directionallighting.shpk
        // Category: 0x3CC170DC
        new("AmbientLighting_Table", 0x3CC170DC, 0x3CC170DC, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: AmbientLighting_Disable
        new("AmbientLighting_Disable", 0xA830185B, 0x3CC170DC, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("AmbientLighting_Enable", 0x31B2B51A, 0x3CC170DC, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("AmbientLighting_EnableMultiply", 0xD997F2D8, 0x3CC170DC, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk
        // Category: 0x3E5A74BA
        new("TextureColor4_CalculateAlpha_Table", 0x3E5A74BA, 0x3E5A74BA, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor4_CalculateAlpha_Mul
        new("TextureColor4_CalculateAlpha_Max", 0x832D1F80, 0x3E5A74BA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor4_CalculateAlpha_Min", 0xBF2020D9, 0x3E5A74BA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_shape.shpk
        new("TextureColor4_CalculateAlpha_Mul", 0xB7591CA8, 0x3E5A74BA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor4_CalculateAlpha_None", 0xD4500CDB, 0x3E5A74BA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x409D1EF9
        new("FurBlurPass", 0x409D1EF9, 0x409D1EF9, NameItemCategory.SceneKey), // Shaders: furblur.shpk Defaults: 0xD680116D
        new("FurBlurPass_Blur", 0xD680116D, 0x409D1EF9, NameItemCategory.SceneValue), // Shaders: furblur.shpk
        // Category: 0x44993AE7
        new("TextureColor2_Decode_Table", 0x44993AE7, 0x44993AE7, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor2_Decode_Off
        new("TextureColor2_Decode_Off", 0x9DBCB8A6, 0x44993AE7, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x4B77C38D
        new("UvCompute0_Table", 0x4B77C38D, 0x4B77C38D, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: UvCompute_ByParameter
        new("UvCompute_ByParameter", 0x692CA241, 0x4B77C38D, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x4CAB2E3A
        new("TextureColor2_CalculateAlpha_Table", 0x4CAB2E3A, 0x4CAB2E3A, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor2_CalculateAlpha_Mul
        new("TextureColor2_CalculateAlpha_Max", 0x070E8255, 0x4CAB2E3A, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor2_CalculateAlpha_Min", 0x3B03BD0C, 0x4CAB2E3A, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor2_CalculateAlpha_Mul", 0x337A817D, 0x4CAB2E3A, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor2_CalculateAlpha_None", 0x226D091D, 0x4CAB2E3A, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x51668572
        new("ApplyOmniShadow", 0x51668572, 0x51668572, NameItemCategory.SceneKey), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk Defaults: ApplyOmniShadow_Disable
        new("ApplyOmniShadow_Chara", 0x3E6FD38A, 0x51668572, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("ApplyOmniShadow_Disable", 0x7E17135D, 0x51668572, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("ApplyOmniShadow_Map", 0xCECC3682, 0x51668572, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("ApplyOmniShadow_MapChara", 0x5CC2C7B3, 0x51668572, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // SceneValue Unknown: 0x3AD8BEF7 Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("ApplyOmniShadow_ShadowMask", 0x61B8EF85, 0x51668572, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // SceneValue Unknown: 0xD1AA9B91 Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // Category: 0x52D21D34
        new("ApplyConeAttenuation", 0x52D21D34, 0x52D21D34, NameItemCategory.SceneKey), // Shaders: spotlighting.shpk Defaults: ApplyConeAttenuation_Disable
        new("ApplyConeAttenuation_Disable", 0x8B39CBDF, 0x52D21D34, NameItemCategory.SceneValue), // Shaders: spotlighting.shpk
        new("ApplyConeAttenuation_Enable", 0xE1068EED, 0x52D21D34, NameItemCategory.SceneValue), // Shaders: spotlighting.shpk
        // Category: 0x52DCF96D
        new("ComputeSoftParticleAlpha", 0x52DCF96D, 0x52DCF96D, NameItemCategory.SceneKey), // Shaders: 3dui.shpk Defaults: ComputeSoftParticleAlphaOff
        new("ComputeSoftParticleAlphaOff", 0xE6A45E9B, 0x52DCF96D, NameItemCategory.SceneValue), // Shaders: 3dui.shpk
        new("ComputeSoftParticleAlphaOn", 0xC9D9121B, 0x52DCF96D, NameItemCategory.SceneValue), // Shaders: 3dui.shpk
        // Category: 0x53AF00ED
        new("ApplyAttenuation", 0x53AF00ED, 0x53AF00ED, NameItemCategory.SceneKey), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk Defaults: ApplyAttenuation_Linear
        new("ApplyAttenuation_Cubic", 0x4495A6B1, 0x53AF00ED, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("ApplyAttenuation_Linear", 0x2795EAA4, 0x53AF00ED, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("ApplyAttenuation_Quadratic", 0xE79A9E9B, 0x53AF00ED, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // Category: 0x5973804A
        // SceneKey Unknown: 0x5973804A Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: 0x20EE3040
        // SceneValue Unknown: 0x20EE3040 Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // SceneValue Unknown: 0x7BF4E250 Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x5B834AFA
        new("TextureColor3_CalculateAlpha_Table", 0x5B834AFA, 0x5B834AFA, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor3_CalculateAlpha_Mul
        new("TextureColor3_CalculateAlpha_Max", 0x6269B913, 0x5B834AFA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor3_CalculateAlpha_Min", 0x5E64864A, 0x5B834AFA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_shape.shpk
        new("TextureColor3_CalculateAlpha_Mul", 0x561DBA3B, 0x5B834AFA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor3_CalculateAlpha_None", 0xBDB78A83, 0x5B834AFA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x627D2022
        new("TextureColor4_Table", 0x627D2022, 0x627D2022, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor4_Disable
        new("TextureColor4_Disable", 0x4964DB20, 0x627D2022, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor4_Enable", 0x6BE9D651, 0x627D2022, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x63236112
        new("DynamicWaveType", 0x63236112, 0x63236112, NameItemCategory.SceneKey), // Shaders: grassdynamicwave.shpk Defaults: 0x5807BECE
        // SceneValue Unknown: 0x41EA30E9 Shaders: grassdynamicwave.shpk
        new("DynamicWaveType_Rigid", 0x5807BECE, 0x63236112, NameItemCategory.SceneValue), // Shaders: grassdynamicwave.shpk
        new("DynamicWaveType_WindDirectional", 0x9C83A1C1, 0x63236112, NameItemCategory.SceneValue), // Shaders: grassdynamicwave.shpk
        // SceneValue Unknown: 0xF9C54246 Shaders: grassdynamicwave.shpk
        // Category: 0x640B0CFA
        new("TextureColor1_UvNo_Table", 0x640B0CFA, 0x640B0CFA, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor1_Uv_0
        new("TextureColor1_Uv_0", 0x5910EB7B, 0x640B0CFA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor1_Uv_1", 0x2E17DBED, 0x640B0CFA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor1_Uv_2", 0xB71E8A57, 0x640B0CFA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor1_Uv_3", 0xC019BAC1, 0x640B0CFA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x764AECE7
        new("ApplyWavingAnimation", 0x764AECE7, 0x764AECE7, NameItemCategory.SceneKey), // Shaders: grass.shpk Defaults: ApplyWavingAnimation_Nothing
        // Category: 0x771D346C
        // SceneKey Unknown: 0x771D346C Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: 0xFA57973D
        // SceneValue Unknown: 0xAC9CA4F0 Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // SceneValue Unknown: 0xD4F18773 Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_powder.shpk|apricot_shape.shpk
        // SceneValue Unknown: 0xFA57973D Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x7A85F3BA
        // SceneKey Unknown: 0x7A85F3BA Shaders: charactertattoo.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: 0xF4F5D1BF
        // SceneValue Unknown: 0xF4F5D1BF Shaders: charactertattoo.shpk|hair.shpk|iris.shpk|skin.shpk
        // Category: 0x7A9FD910
        new("UvCompute3_Table", 0x7A9FD910, 0x7A9FD910, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: UvCompute_ByParameter
        // Category: 0x7DB09695
        new("LightClip", 0x7DB09695, 0x7DB09695, NameItemCategory.SceneKey), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk Defaults: LightClip_Disable
        new("LightClip_Disable", 0x61628441, 0x7DB09695, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("LightClip_Enable", 0x6F0E2969, 0x7DB09695, NameItemCategory.SceneValue), // Shaders: linelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // Category: 0x8115916D
        new("GetDirectionalLight", 0x8115916D, 0x8115916D, NameItemCategory.SceneKey), // Shaders: directionallighting.shpk Defaults: GetDirectionalLight_Disable
        new("GetDirectionalLight_Disable", 0x4C5E8831, 0x8115916D, NameItemCategory.SceneValue), // Shaders: directionallighting.shpk
        new("GetDirectionalLight_Enable", 0x51EDD496, 0x8115916D, NameItemCategory.SceneValue), // Shaders: directionallighting.shpk
        new("GetDirectionalLight_Shadow", 0xD73B9E89, 0x8115916D, NameItemCategory.SceneValue), // Shaders: directionallighting.shpk
        // Category: 0x82730F4C
        new("ApplyLightBufferType_Table", 0x82730F4C, 0x82730F4C, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: ApplyLightBufferType_Disable
        new("ApplyLightBufferType_Disable", 0x19CB405E, 0x82730F4C, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x837F9F33
        new("TexturePalette_Table", 0x837F9F33, 0x837F9F33, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TexturePalette_Disable
        new("TexturePalette_Disable", 0x4163B72F, 0x837F9F33, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TexturePalette_Enable", 0x01B8F41C, 0x837F9F33, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x83E5D9C5
        new("TextureDistortion_Table", 0x83E5D9C5, 0x83E5D9C5, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureDistortion_Disable
        new("TextureDistortion_Disable", 0x9C3A152B, 0x83E5D9C5, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureDistortion_Enable", 0x55663973, 0x83E5D9C5, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x88309265
        new("TextureColor3_UvNo_Table", 0x88309265, 0x88309265, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor3_Uv_0
        new("TextureColor3_Uv_0", 0x14D84A70, 0x88309265, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor3_Uv_1", 0x63DF7AE6, 0x88309265, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor3_Uv_2", 0xFAD62B5C, 0x88309265, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor3_Uv_3", 0x8DD11BCA, 0x88309265, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x8E2B8906
        new("TextureColor2_ColorToAlpha_Table", 0x8E2B8906, 0x8E2B8906, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor2_ColorToAlpha_Off
        new("TextureColor2_ColorToAlpha_Off", 0x81EA64EB, 0x8E2B8906, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor2_ColorToAlpha_On", 0x5AA9AE80, 0x8E2B8906, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0x8FF4ACEB
        new("DirectionalLightType_Table", 0x8FF4ACEB, 0x8FF4ACEB, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: DirectionalLightType_Lambert
        new("DirectionalLightType_Ex", 0xFC588A87, 0x8FF4ACEB, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk
        new("DirectionalLightType_HalfLambert", 0xBAAA5E93, 0x8FF4ACEB, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk
        new("DirectionalLightType_Lambert", 0x1E35D60A, 0x8FF4ACEB, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x907B83D2
        new("DirectionalLight_Table", 0x907B83D2, 0x907B83D2, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: DirectionalLight_Disable
        new("DirectionalLight_Disable", 0x12073AC9, 0x907B83D2, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("DirectionalLight_Enable", 0xD6A622EE, 0x907B83D2, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk
        // Category: 0x91F08CFF
        // SceneKey Unknown: 0x91F08CFF Shaders: iris.shpk Defaults: 0x78A3D396
        // SceneValue Unknown: 0x78A3D396 Shaders: iris.shpk
        // Category: 0x9391070F
        new("PointLightCount_Table", 0x9391070F, 0x9391070F, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: PointLightCount_0_0
        new("PointLightCount_0_0", 0xA1CC1C77, 0x9391070F, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("PointLightCount_1_0", 0xA00E7640, 0x9391070F, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk
        new("PointLightCount_1_1", 0xD70946D6, 0x9391070F, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk
        // SceneValue Unknown: 0xD6CB2CE1 Shaders: apricot_fogmodel.shpk
        // Category: 0x95D37D89
        new("TextureNormal_UvNo_Table", 0x95D37D89, 0x95D37D89, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureNormal_Uv_0
        new("TextureNormal_Uv_0", 0x5285A598, 0x95D37D89, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureNormal_Uv_1", 0x2582950E, 0x95D37D89, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk
        new("TextureNormal_Uv_2", 0xBC8BC4B4, 0x95D37D89, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk
        new("TextureNormal_Uv_3", 0xCB8CF422, 0x95D37D89, NameItemCategory.SceneValue), // Shaders: apricot_model.shpk|apricot_model_morph.shpk
        // Category: 0x97561751
        new("TextureDistortion_UvSet0_Table", 0x97561751, 0x97561751, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureDistortion_UvSet_Disable
        new("TextureDistortion_UvSet_Disable", 0x261C31BD, 0x97561751, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x979063BD
        new("GetMaterialType", 0x979063BD, 0x979063BD, NameItemCategory.SceneKey), // Shaders: bgdecal.shpk Defaults: GetMaterialTypeDefault
        new("GetMaterialTypeColorOnly", 0xB1393643, 0x979063BD, NameItemCategory.SceneValue), // Shaders: bgdecal.shpk
        new("GetMaterialTypeDefault", 0x8FDC4BA4, 0x979063BD, NameItemCategory.SceneValue), // Shaders: bgdecal.shpk
        new("GetMaterialTypeUseNormal", 0x5C9BED2C, 0x979063BD, NameItemCategory.SceneValue), // Shaders: bgdecal.shpk
        // Category: 0x97C9F730
        new("PointLightPositionType_Table", 0x97C9F730, 0x97C9F730, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: PointLightPositionType_PerPixel
        new("PointLightPositionType_PerModel", 0x6EB1F7A6, 0x97C9F730, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk
        new("PointLightPositionType_PerPixel", 0xC696A15D, 0x97C9F730, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0x9A80A23B
        new("UvPrecisionType_Table", 0x9A80A23B, 0x9A80A23B, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_dummy.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_dummy.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_dummy.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_powder_dummy.shpk|apricot_shape.shpk|apricot_shape_dummy.shpk Defaults: UvPrecisionType_High
        new("UvPrecisionType_High", 0x51D70427, 0x9A80A23B, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_dummy.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_dummy.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_dummy.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_powder_dummy.shpk|apricot_shape.shpk|apricot_shape_dummy.shpk
        new("UvPrecisionType_Low", 0x45E00267, 0x9A80A23B, NameItemCategory.SceneValue), // Shaders: apricot_decal_dummy.shpk|apricot_gbuffer_decal_dummy.shpk|apricot_model_dummy.shpk|apricot_powder_dummy.shpk|apricot_shape.shpk|apricot_shape_dummy.shpk
        new("UvPrecisionType_Medium", 0x3AA90E96, 0x9A80A23B, NameItemCategory.SceneValue), // Shaders: apricot_decal_dummy.shpk|apricot_gbuffer_decal_dummy.shpk|apricot_model_dummy.shpk|apricot_powder_dummy.shpk|apricot_shape.shpk|apricot_shape_dummy.shpk
        // Category: 0x9B064857
        new("TextureColor4_CalculateColor_Table", 0x9B064857, 0x9B064857, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor4_CalculateColor_Mul
        new("TextureColor4_CalculateColor_Add", 0xD9034ADB, 0x9B064857, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor4_CalculateColor_Max", 0xB96F1BB5, 0x9B064857, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor4_CalculateColor_Min", 0x856224EC, 0x9B064857, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_shape.shpk
        new("TextureColor4_CalculateColor_Mul", 0x8D1B189D, 0x9B064857, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor4_CalculateColor_None", 0x82D98AFC, 0x9B064857, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor4_CalculateColor_Sub", 0x7C1BBBE0, 0x9B064857, NameItemCategory.SceneValue), // Shaders: apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_shape.shpk
        // Category: 0xA19D807E
        new("ComputeFinalColorType_Table", 0xA19D807E, 0xA19D807E, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: ComputeFinalColorType_NoneControl
        new("ComputeFinalColorType_LerpWhite", 0x9B8DF63B, 0xA19D807E, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("ComputeFinalColorType_ModulateAlpha", 0x1321587A, 0xA19D807E, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("ComputeFinalColorType_NoneControl", 0xDA313F25, 0xA19D807E, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0xA1F5312D
        new("TextureReflection_CalculateColor_Table", 0xA1F5312D, 0xA1F5312D, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureReflection_CalculateColor_Mul
        new("TextureReflection_CalculateColor_Add", 0x98095274, 0xA1F5312D, NameItemCategory.SceneValue), // Shaders: apricot_model.shpk|apricot_model_morph.shpk
        new("TextureReflection_CalculateColor_Max", 0xF865031A, 0xA1F5312D, NameItemCategory.SceneValue), // Shaders: apricot_model.shpk
        new("TextureReflection_CalculateColor_Mul", 0xCC110032, 0xA1F5312D, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureReflection_CalculateColor_Sub", 0x3D11A34F, 0xA1F5312D, NameItemCategory.SceneValue), // Shaders: apricot_model.shpk
        // Category: 0xA4EAB6DB
        new("Shading_Table", 0xA4EAB6DB, 0xA4EAB6DB, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: Shading_None
        new("Shading_BG", 0x343F1E19, 0xA4EAB6DB, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk
        new("Shading_Hybrid", 0xC5F39B7E, 0xA4EAB6DB, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk
        new("Shading_None", 0xDB21DB5A, 0xA4EAB6DB, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("Shading_Uniform", 0x2C1F45D4, 0xA4EAB6DB, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0xA6BE0DCC
        new("TextureDistortion_UvSet3_Table", 0xA6BE0DCC, 0xA6BE0DCC, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureDistortion_UvSet_Disable
        // Category: 0xA7DA1EAC
        new("TextureColor3_Table", 0xA7DA1EAC, 0xA7DA1EAC, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor3_Disable
        new("TextureColor3_Disable", 0x2D05B6E9, 0xA7DA1EAC, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor3_Enable", 0x612CDF48, 0xA7DA1EAC, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0xA89D89F0
        new("ShadowSoftShadowType", 0xA89D89F0, 0xA89D89F0, NameItemCategory.SceneKey), // Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk|directionalshadow.shpk|linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk Defaults: ShadowSoftShadowType_1x1
        new("ShadowSoftShadowType_1x1", 0x749F8AB2, 0xA89D89F0, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk|directionalshadow.shpk|linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("ShadowSoftShadowType_3x3", 0x99153FF0, 0xA89D89F0, NameItemCategory.SceneValue), // Shaders: directionalshadow.shpk|linelighting.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // SceneValue Unknown: 0x2B16DE56 Shaders: directionalshadow.shpk|planelighting.shpk|spotlighting.shpk
        // SceneValue Unknown: 0x74FBE677 Shaders: directionalshadow.shpk|planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // Category: 0xB1501184
        // SceneKey Unknown: 0xB1501184 Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_powder.shpk Defaults: 0x498050F7
        // SceneValue Unknown: 0x498050F7 Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_powder.shpk
        // Category: 0xB19772FF
        new("SubSurfaceBlurPass", 0xB19772FF, 0xB19772FF, NameItemCategory.SceneKey), // Shaders: subsurfaceblur.shpk Defaults: 0x0745C4E0
        // SceneValue Unknown: 0x0745C4E0 Shaders: subsurfaceblur.shpk
        // SceneValue Unknown: 0x7042F476 Shaders: subsurfaceblur.shpk
        // Category: 0xB3DC6CB8
        // MaterialKey Unknown: 0xB3DC6CB8 Shaders: hair.shpk Defaults: 0xE3D0D77E
        // MaterialValue Unknown: 0x0DDEB652 Shaders: hair.shpk
        // MaterialValue Unknown: 0xE3D0D77E Shaders: hair.shpk
        // Category: 0xBB592EBB
        new("OutputType_Table", 0xBB592EBB, 0xBB592EBB, NameItemCategory.SceneKey), // Shaders: apricot_decal_dummy.shpk|apricot_gbuffer_decal_dummy.shpk|apricot_model_dummy.shpk|apricot_powder_dummy.shpk|apricot_shape_dummy.shpk Defaults: OutputType_Debug
        new("OutputType_Debug", 0x21D5E65A, 0xBB592EBB, NameItemCategory.SceneValue), // Shaders: apricot_decal_dummy.shpk|apricot_gbuffer_decal_dummy.shpk|apricot_model_dummy.shpk|apricot_powder_dummy.shpk|apricot_shape_dummy.shpk
        new("OutputType_Release", 0xDF5D501C, 0xBB592EBB, NameItemCategory.SceneValue), // Shaders: apricot_decal_dummy.shpk|apricot_gbuffer_decal_dummy.shpk|apricot_model_dummy.shpk|apricot_powder_dummy.shpk|apricot_shape_dummy.shpk
        // Category: 0xBC5B7151
        new("TextureColor4_Decode_Table", 0xBC5B7151, 0xBC5B7151, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor4_Decode_Off
        new("TextureColor4_Decode_Off", 0x72811D46, 0xBC5B7151, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0xC75A104C
        new("EnablePassZ", 0xC75A104C, 0xC75A104C, NameItemCategory.SceneKey), // Shaders: grass.shpk Defaults: 0x7E5E1EEF
        new("EnablePassZ_Disable", 0x7E5E1EEF, 0xC75A104C, NameItemCategory.SceneValue), // Shaders: grass.shpk
        new("EnablePassZ_Enable", 0x8917E9BA, 0xC75A104C, NameItemCategory.SceneValue), // Shaders: grass.shpk
        // Category: 0xD7826DAA
        new("TransformType", 0xD7826DAA, 0xD7826DAA, NameItemCategory.SceneKey), // Shaders: shadowmask.shpk Defaults: TransformTypePlane
        new("TransformTypeBox", 0xC8AFF84F, 0xD7826DAA, NameItemCategory.SceneValue), // Shaders: shadowmask.shpk
        new("TransformTypePlane", 0x1943C146, 0xD7826DAA, NameItemCategory.SceneValue), // Shaders: shadowmask.shpk
        // Category: 0xD996DB91
        new("TextureColor3_Decode_Table", 0xD996DB91, 0xD996DB91, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor3_Decode_Off
        new("TextureColor3_Decode_Off", 0x0619F4C9, 0xD996DB91, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0xDC0ECEC4
        // SceneKey Unknown: 0xDC0ECEC4 Shaders: apricot_powder.shpk|apricot_shape.shpk Defaults: 0x06F2FE46
        // SceneValue Unknown: 0x06F2FE46 Shaders: apricot_powder.shpk|apricot_shape.shpk
        // Category: 0xDCE8D2A4
        new("UvCompute2_Table", 0xDCE8D2A4, 0xDCE8D2A4, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: UvCompute_ByParameter
        new("UvCompute_ByPixelPosition", 0x8F47B02C, 0xDCE8D2A4, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0xE9F712D7
        new("TextureColor2_CalculateColor_Table", 0xE9F712D7, 0xE9F712D7, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor2_CalculateColor_Mul
        new("TextureColor2_CalculateColor_Add", 0x5D20D70E, 0xE9F712D7, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor2_CalculateColor_Max", 0x3D4C8660, 0xE9F712D7, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor2_CalculateColor_Min", 0x0141B939, 0xE9F712D7, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor2_CalculateColor_Mul", 0x09388548, 0xE9F712D7, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor2_CalculateColor_None", 0x74E48F3A, 0xE9F712D7, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor2_CalculateColor_Sub", 0xF8382635, 0xE9F712D7, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_shape.shpk
        // Category: 0xEAC154EC
        new("GetUnderWaterLighting", 0xEAC154EC, 0xEAC154EC, NameItemCategory.SceneKey), // Shaders: directionallighting.shpk Defaults: GetUnderWaterLighting_Disable
        new("GetUnderWaterLighting_Disable", 0x2AA7A53C, 0xEAC154EC, NameItemCategory.SceneValue), // Shaders: directionallighting.shpk
        new("GetUnderWaterLighting_Enable", 0xAEFB134C, 0xEAC154EC, NameItemCategory.SceneValue), // Shaders: directionallighting.shpk
        // Category: 0xEB4CB240
        new("TextureColor3_ColorToAlpha_Table", 0xEB4CB240, 0xEB4CB240, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor3_ColorToAlpha_Off
        new("TextureColor3_ColorToAlpha_Off", 0x6EB8D20A, 0xEB4CB240, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor3_ColorToAlpha_On", 0xDD0F65C3, 0xEB4CB240, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0xED00C839
        new("UvCompute1_Table", 0xED00C839, 0xED00C839, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: UvCompute_ByParameter
        // Category: 0xF0AC7D17
        // SceneKey Unknown: 0xF0AC7D17 Shaders: vfxcloud_composite.shpk Defaults: 0xA297DD0F
        // SceneValue Unknown: 0xA297DD0F Shaders: vfxcloud_composite.shpk
        // SceneValue Unknown: 0xDC26A1B2 Shaders: vfxcloud_composite.shpk
        // Category: 0xF0E08E18
        new("PointLightType_Table", 0xF0E08E18, 0xF0E08E18, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: PointLightType_Lambert
        new("PointLightType_Ex", 0xAC3C2480, 0xF0E08E18, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk
        new("PointLightType_HalfLambert", 0x127BA6E5, 0xF0E08E18, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk
        new("PointLightType_Lambert", 0xF4C45A84, 0xF0E08E18, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0xF4DA16D6
        new("ApplyFog_Table", 0xF4DA16D6, 0xF4DA16D6, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: ApplyFog_None|ApplyFog_RGB
        new("ApplyFog_Alpha", 0xD213A0FA, 0xF4DA16D6, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("ApplyFog_None", 0xEEF0EA2A, 0xF4DA16D6, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("ApplyFog_RGB", 0x84329E3A, 0xF4DA16D6, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        // Category: 0xF66E4589
        new("ApplyMaskTexture", 0xF66E4589, 0xF66E4589, NameItemCategory.SceneKey), // Shaders: planelighting.shpk|pointlighting.shpk|spotlighting.shpk Defaults: ApplyMaskTexture_Disable
        new("ApplyMaskTexture_Disable", 0x3E5F4521, 0xF66E4589, NameItemCategory.SceneValue), // Shaders: planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        new("ApplyMaskTexture_Enable", 0x57474150, 0xF66E4589, NameItemCategory.SceneValue), // Shaders: planelighting.shpk|pointlighting.shpk|spotlighting.shpk
        // Category: 0xF9C71291
        new("GetShadow", 0xF9C71291, 0xF9C71291, NameItemCategory.SceneKey), // Shaders: weather.shpk Defaults: 0xCB306F71
        new("GetShadow_Enable", 0x37A94607, 0xF9C71291, NameItemCategory.SceneValue), // Shaders: weather.shpk
        new("GetShadow_Disable", 0xCB306F71, 0xF9C71291, NameItemCategory.SceneValue), // Shaders: weather.shpk
        // Category: 0xFAFCF387
        new("TextureReflection_Table", 0xFAFCF387, 0xFAFCF387, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureReflection_Disable
        new("TextureReflection_Disable", 0x85CE8863, 0xFAFCF387, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureReflection_Enable", 0xCDCD00E5, 0xFAFCF387, NameItemCategory.SceneValue), // Shaders: apricot_fogmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk
        // Category: 0xFCA87BEA
        new("TextureColor4_UvNo_Table", 0xFCA87BEA, 0xFCA87BEA, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor4_Uv_0
        new("TextureColor4_Uv_0", 0x09DD7AC8, 0xFCA87BEA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor4_Uv_1", 0x7EDA4A5E, 0xFCA87BEA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor4_Uv_2", 0xE7D31BE4, 0xFCA87BEA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor4_Uv_3", 0x90D42B72, 0xFCA87BEA, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0xFD7A2BE9
        new("UvSetCount_Table", 0xFD7A2BE9, 0xFD7A2BE9, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: UvSetCount_0|UvSetCount_1|UvSetCount_4
        new("UvSetCount_0", 0xF00A5F39, 0xFD7A2BE9, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("UvSetCount_1", 0x870D6FAF, 0xFD7A2BE9, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("UvSetCount_2", 0x1E043E15, 0xFD7A2BE9, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("UvSetCount_3", 0x69030E83, 0xFD7A2BE9, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("UvSetCount_4", 0xF7679B20, 0xFD7A2BE9, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        // Category: 0xFEDF7617
        new("TextureColor3_CalculateColor_Table", 0xFEDF7617, 0xFEDF7617, NameItemCategory.SceneKey), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk Defaults: TextureColor3_CalculateColor_Mul
        new("TextureColor3_CalculateColor_Add", 0x3847EC48, 0xFEDF7617, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor3_CalculateColor_Max", 0x582BBD26, 0xFEDF7617, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor3_CalculateColor_Min", 0x6426827F, 0xFEDF7617, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_shape.shpk
        new("TextureColor3_CalculateColor_Mul", 0x6C5FBE0E, 0xFEDF7617, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_gbuffer_decal.shpk|apricot_gbuffer_decal_ring.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_powder.shpk|apricot_shape.shpk
        new("TextureColor3_CalculateColor_None", 0xEB3E0CA4, 0xFEDF7617, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_model_morph.shpk|apricot_shape.shpk
        new("TextureColor3_CalculateColor_Sub", 0x9D5F1D73, 0xFEDF7617, NameItemCategory.SceneValue), // Shaders: apricot_decal.shpk|apricot_decal_ring.shpk|apricot_fogmodel.shpk|apricot_lightmodel.shpk|apricot_model.shpk|apricot_shape.shpk
    ];

    private static readonly IReadOnlyList<StubName> StubNames =
    [
        new("CategoryFlowMapType", 0x40D1481E), // Shaders: character.shpk|characterscroll.shpk Defaults: Standard
        new("CategorySpecularType", 0xC8BD1DEF), // Shaders: characterlegacy.shpk Defaults: Default
        new("CategoryVertexColorMode", 0xF52CCF05), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|characterstockings.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk|skin.shpk Defaults: ValVertexColorOff
        new("Default", 0x198D11CD), // Shaders: characterlegacy.shpk
        new("Flow", 0x71ADA939), // Shaders: character.shpk|characterscroll.shpk
        new("Mask", 0xA02F4828), // Shaders: characterlegacy.shpk
        new("Standard", 0x337C6BC4), // Shaders: character.shpk|characterscroll.shpk
        new("ValVertexColorEmissive", 0xA7D2FF60), // Shaders: character.shpk|characterinc.shpk|characterlegacy.shpk|characterscroll.shpk|characterstockings.shpk|charactertransparency.shpk|skin.shpk
        new("ValVertexColorOff", 0xDFE74BAC), // Shaders: character.shpk|characterglass.shpk|characterinc.shpk|characterlegacy.shpk|charactermultitile.shpk|characterocclusion.shpk|characterreflection.shpk|characterscroll.shpk|charactertattoo.shpk|charactertransparency.shpk|hair.shpk|iris.shpk
    ];
}
