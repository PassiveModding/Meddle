using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.Structs.Model;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x260)]
public unsafe struct ModelResourceHandleExt
{
    [FieldOffset(0x0)] public ModelResourceHandle Base;

    [FieldOffset(0xC8)] public byte* StringTable;
    
    [FieldOffset(0x208)]
    public MaterialResourceHandle** MaterialResourceHandles;
}

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
