using System.Runtime.InteropServices;
using System.Text;

namespace Meddle.Utils.Files;

public class MtrlFile
{
    public MaterialFileHeader FileHeader;
    public TextureOffset[] TextureOffsets;
    public UvColorSet[] UvColorSets;
    public ColorSet[] ColorSets;
    public byte[] Strings;
    public byte[] AdditionalData;
    
    public ColorSetInfo ColorSetInfo;
    public ColorSetDyeInfo ColorSetDyeInfo;
    
    public MaterialHeader MaterialHeader;
    
    public ShaderKey[] ShaderKeys;
    public Constant[] Constants;
    public Sampler[] Samplers;
    public float[] ShaderValues;
    
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
            ColorSetInfo = dataSetReader.Read<ColorSetInfo>();
            if (FileHeader.DataSetSize > 512)
                ColorSetDyeInfo = dataSetReader.Read<ColorSetDyeInfo>();
        }
        
        MaterialHeader = reader.Read<MaterialHeader>();

        ShaderKeys = reader.Read<ShaderKey>(MaterialHeader.ShaderKeyCount).ToArray();
        Constants = reader.Read<Constant>(MaterialHeader.ConstantCount).ToArray();
        Samplers = reader.Read<Sampler>(MaterialHeader.SamplerCount).ToArray();
        
        ShaderValues = reader.Read<float>(MaterialHeader.ShaderValueListSize / 4).ToArray();
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

public unsafe struct ColorSetInfo
{
    public fixed ushort Data[256];
}

public unsafe struct ColorSetDyeInfo
{
    public fixed ushort Data[16];
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
