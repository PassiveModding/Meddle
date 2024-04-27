using System.Numerics;
using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Utils.Files;

public class MtrlFile
{
    public MaterialFileHeader FileHeader;
    public TextureOffset[] TextureOffsets;
    public UvColorSet[] UvColorSets;
    public ColorSet[] ColorSets;
    public byte[] Strings;
    public byte[] AdditionalData;
    
    public bool LargeColorTable => AdditionalData.Length > 1 && AdditionalData[1] == 0x05 && (AdditionalData[0] & 0x30) == 0x30;
    public bool HasTable => AdditionalData.Length > 0 && (AdditionalData[0] & 0x4) != 0;
    public bool HasDyeTable => AdditionalData.Length > 0 && (AdditionalData[0] & 0x8) != 0;
    
    public ColorTable ColorTable;
    public ColorDyeTable ColorDyeTable;
    
    public MaterialHeader MaterialHeader;
    
    public ShaderKey[] ShaderKeys;
    public Constant[] Constants;
    public Sampler[] Samplers;
    public uint[] ShaderValues;
    
    public readonly byte[] RawData;
    public readonly int RemainingOffset;
    public ReadOnlySpan<byte> RemainingData => RawData.AsSpan(RemainingOffset);
    
    public MtrlFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }
    public MtrlFile(ReadOnlySpan<byte> data)
    {
        var reader = new SpanBinaryReader(data);
        FileHeader = reader.Read<MaterialFileHeader>();
        TextureOffsets = new TextureOffset[FileHeader.TextureCount];

        var offsets = reader.Read<uint>(FileHeader.TextureCount);
        for (int i = 0; i < offsets.Length; i++)
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
                if (HasTable)
                    ColorTable = dataSetReader.Read<ColorTable>();
                else
                    ColorTable.SetDefault();
                if (HasDyeTable)
                    ColorDyeTable = dataSetReader.Read<ColorDyeTable>();
            }
            else
            {
                if (HasTable)
                    ColorTable = new ColorTable(dataSetReader.Read<LegacyColorTable>());
                else
                    ColorTable.SetDefault();
                if (HasDyeTable)
                    ColorDyeTable = new ColorDyeTable(dataSetReader.Read<LegacyColorDyeTable>());
            }
        }
        
        MaterialHeader = reader.Read<MaterialHeader>();

        ShaderKeys = reader.Read<ShaderKey>(MaterialHeader.ShaderKeyCount).ToArray();
        Constants = reader.Read<Constant>(MaterialHeader.ConstantCount).ToArray();
        Samplers = reader.Read<Sampler>(MaterialHeader.SamplerCount).ToArray();
        
        ShaderValues = reader.Read<uint>(MaterialHeader.ShaderValueListSize / 4).ToArray();
        
        RawData = data.ToArray();
        RemainingOffset = reader.Position;
    }
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
    
public struct MaterialHeader
{
    public ushort ShaderValueListSize;
    public ushort ShaderKeyCount;
    public ushort ConstantCount;
    public ushort SamplerCount;
    public ushort Unknown1;
    public ushort Unknown2;
}
