using FFXIVClientStructs.Interop;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.Structs.Model;

namespace Meddle.Plugin.Models.Structs;

public readonly struct ModelResourceHandleData
{
    public readonly uint StringCount;
    public readonly uint StringTableSize;
    public readonly byte[] StringTableData;
    public readonly ModelHeader ModelHeader;
        
    public ModelResourceHandleData(Pointer<byte> stringTablePtr)
    {
        var offset = 0;
        StringCount = stringTablePtr.Read<uint>(ref offset);
        StringTableSize = stringTablePtr.Read<uint>(ref offset);
        StringTableData = stringTablePtr.ReadSpan<byte>((int)StringTableSize, ref offset).ToArray();
        ModelHeader = stringTablePtr.Read<ModelHeader>(ref offset);
    }
} 
