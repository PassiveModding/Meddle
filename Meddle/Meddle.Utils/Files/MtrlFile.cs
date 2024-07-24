using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Utils.Files;

public class MtrlFile
{
    public const uint MtrlMagic = 0x1030000;

    private readonly byte[] _data;
    public byte[] AdditionalData;
    public ColorDyeTable ColorDyeTable;
    public ColorSet[] ColorSets;

    public ColorTable ColorTable;
    public Constant[] Constants;

    public MaterialFileHeader FileHeader;
    public Sampler[] Samplers;

    public MaterialShaderHeader ShaderHeader;

    public ShaderKey[] ShaderKeys;
    public uint[] ShaderValues;
    public byte[] Strings;
    public TextureOffset[] TextureOffsets;
    public UvColorSet[] UvColorSets;

    public MtrlFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public MtrlFile(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
        var reader = new SpanBinaryReader(data);
        FileHeader = reader.Read<MaterialFileHeader>();
        TextureOffsets = new TextureOffset[FileHeader.TextureCount];

        var offsets = reader.Read<uint>(FileHeader.TextureCount);
        for (var i = 0; i < offsets.Length; i++)
        {
            TextureOffsets[i].Offset = (ushort)offsets[i];
            TextureOffsets[i].Flags = (ushort)(offsets[i] >> 16);
        }

        UvColorSets = reader.Read<UvColorSet>(FileHeader.UvSetCount).ToArray();
        ColorSets = reader.Read<ColorSet>(FileHeader.ColorSetCount).ToArray();

        Strings = reader.Read<byte>(FileHeader.StringTableSize).ToArray();
        AdditionalData = reader.Read<byte>(FileHeader.AdditionalDataSize).ToArray();

        if (FileHeader.DataSetSize > 0)
        {
            var dataSet = reader.Read<byte>(FileHeader.DataSetSize).ToArray();
            var dataSetReader = new SpanBinaryReader(dataSet);
            if (LargeColorTable)
            {
                ColorTable = HasTable ? ColorTable.Load(ref dataSetReader) : ColorTable.Default();
                if (HasDyeTable)
                    ColorDyeTable = dataSetReader.Read<ColorDyeTable>();
            }
            else
            {
                ColorTable = HasTable ? ColorTable.LoadLegacy(ref dataSetReader) : ColorTable.DefaultLegacy();
                if (HasDyeTable)
                    ColorDyeTable = new ColorDyeTable(dataSetReader.Read<LegacyColorDyeTable>());
            }
        }
        else
        {
            ColorTable = ColorTable.Default();
        }

        ShaderHeader = reader.Read<MaterialShaderHeader>();

        ShaderKeys = reader.Read<ShaderKey>(ShaderHeader.ShaderKeyCount).ToArray();
        Constants = reader.Read<Constant>(ShaderHeader.ConstantCount).ToArray();
        Samplers = reader.Read<Sampler>(ShaderHeader.SamplerCount).ToArray();

        ShaderValues = reader.Read<uint>(ShaderHeader.ShaderValueListSize / 4).ToArray();
    }

    public bool LargeColorTable =>
        AdditionalData.Length > 1 && AdditionalData[1] == 0x05 && (AdditionalData[0] & 0x30) == 0x30;

    public bool HasTable => AdditionalData.Length > 0 && (AdditionalData[0] & 0x4) != 0;
    public bool HasDyeTable => AdditionalData.Length > 0 && (AdditionalData[0] & 0x8) != 0;
    public ReadOnlySpan<byte> RawData => _data;
}

public struct Constant
{
    public uint ConstantId;
    public ushort ValueOffset;
    public ushort ValueSize;
}

public unsafe struct Sampler
{
    public uint SamplerId;
    public uint Flags; // Bitfield; values unknown
    public byte TextureIndex;
    private fixed byte padding[3];
}

public struct ShaderKey
{
    public uint Category;
    public uint Value;
}

public struct ColorSet
{
    public ushort NameOffset;
    public byte Index;
    public byte Unknown1;
}

public struct UvColorSet
{
    public ushort NameOffset;
    public byte Index;
    public byte Unknown1;
}

public struct TextureOffset
{
    public ushort Offset;
    public ushort Flags;
}

public struct MaterialFileHeader
{
    public uint Version;
    public ushort FileSize;
    public ushort DataSetSize;
    public ushort StringTableSize;
    public ushort ShaderPackageNameOffset;
    public byte TextureCount;
    public byte UvSetCount;
    public byte ColorSetCount;
    public byte AdditionalDataSize;
}

public struct MaterialShaderHeader
{
    public ushort ShaderValueListSize;
    public ushort ShaderKeyCount;
    public ushort ConstantCount;
    public ushort SamplerCount;
    public uint Flags;
}
