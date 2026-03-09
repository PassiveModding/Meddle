using System.Runtime.InteropServices;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.Structs.Model;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x2A0)]
public unsafe struct MeddleModelResourceHandle
{
    [FieldOffset(0x0)] public FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.ModelResourceHandle* Base;
    [FieldOffset(0xC8)] public byte* ModelData; // StringTable, ModelHeader ...
    public ModelResourceHandleData GetData() => new(Base->ModelData);
} 

[StructLayout(LayoutKind.Explicit, Size = 0x158)]
public unsafe struct MeddleModel
{
    [FieldOffset(0x0)] public FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model* Base;
    [FieldOffset(0x28)] public uint Flags; // bit 0: body visible, bit 1: attributes dirty, bit 2: shapes dirty
    
    public bool BodyVisible => (Flags & 1) != 0;
} 


public readonly struct ModelResourceHandleData
{
    public readonly uint StringCount;
    public readonly uint StringTableSize;
    public readonly byte[] StringTableData;
    public readonly ModelHeader ModelHeader;
        
    public ModelResourceHandleData(Pointer<byte> modelDataPtr)
    {
        var offset = 0;
        StringCount = modelDataPtr.Read<uint>(ref offset);
        StringTableSize = modelDataPtr.Read<uint>(ref offset);
        StringTableData = modelDataPtr.ReadSpan<byte>((int)StringTableSize, ref offset).ToArray();
        ModelHeader = modelDataPtr.Read<ModelHeader>(ref offset);
    }
} 
