using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0xB8)]
public unsafe struct SharedGroupResourceHandle
{
    [FieldOffset(0x00)] public ResourceHandle* ResourceHandle;
    [FieldOffset(0xB0)] public SgbFile* SceneChunk;
}

[StructLayout(LayoutKind.Explicit, Size = 0x1E0)]
public unsafe struct SgbFile
{
    [FieldOffset(0x10)] public SgbData* SgbData;
    
    public Pointer<HousingSettings> GetHousingSettings()
    {
        var chunk = SgbData->SceneChunkDefinition;
        var housingOffset = chunk.HousingOffset;
        
        if (housingOffset == 0) return null;

        var housingPosition = (nint)SgbData + 
                              Unsafe.SizeOf<SgbFileHeader>() + 
                              Unsafe.SizeOf<SceneChunkHeader>() + 
                              housingOffset;
        var housingSettings = (HousingSettings*)housingPosition;
        return housingSettings;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 0x10)]
public struct SgbData
{
    [FieldOffset(0x00)] public SgbFileHeader FileHeader;
    [FieldOffset(0x0C)] public SceneChunkDefinition SceneChunkDefinition;
    // ... rest of the layer data
}

[StructLayout(LayoutKind.Explicit, Size = 0x0C)]
public unsafe struct SgbFileHeader
{
    
    [FieldOffset(0x00)] public fixed byte FileId[4];
    [FieldOffset(0x04)] public uint FileSize;
    [FieldOffset(0x08)] public uint TotalChunkCount;
}

[StructLayout(LayoutKind.Explicit, Size = 0x08)]
public unsafe struct SceneChunkHeader
{
    [FieldOffset(0x00)] public fixed byte ChunkId[4];
    [FieldOffset(0x04)] public uint ChunkSize;
}

[StructLayout(LayoutKind.Explicit, Size = 0x48)]
public struct SceneChunkDefinition
{
    [FieldOffset(0x00)] public SceneChunkHeader Header;
    [FieldOffset(0x08)] public uint LayerGroupOffset;
    [FieldOffset(0x0C)] public uint LayerGroupCount;
    
    [FieldOffset(0x34)] public uint HousingOffset;
}

[StructLayout(LayoutKind.Explicit, Size = 0x42)]
public struct HousingSettings
{
    [FieldOffset(0x00)] public ushort DefaultColorId;
}
