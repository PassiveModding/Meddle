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
        var chunk = SgbData->SceneChunkHeader;
        var housingOffset = chunk.HousingOffset;
        
        if (housingOffset == 0) return null;
        const int layerGroupOffset = 0x08;
        const int sceneChunkHeader = 0x0C;
        
        var ptr = (byte*)SgbData
                  + sceneChunkHeader
                  + layerGroupOffset;
        var housingSettings = (HousingSettings*)(ptr + housingOffset);
        return housingSettings;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 0x10)]
public unsafe struct SgbData
{
    [FieldOffset(0x00)] public fixed byte FileId[4];
    [FieldOffset(0x04)] public uint FileSize;
    [FieldOffset(0x08)] public uint TotalChunkCount;
    [FieldOffset(0x0C)] public SceneChunkHeader SceneChunkHeader;
    // ... rest of the layer data
}

[StructLayout(LayoutKind.Explicit, Size = 0x48)]
public unsafe struct SceneChunkHeader
{
    [FieldOffset(0x00)] public fixed byte ChunkId[4];
    [FieldOffset(0x04)] public uint ChunkSize;
    [FieldOffset(0x08)] public uint LayerGroupOffset;
    [FieldOffset(0x0C)] public uint LayerGroupCount;
    
    [FieldOffset(0x34)] public uint HousingOffset;
}

[StructLayout(LayoutKind.Explicit, Size = 0x42)]
public unsafe struct HousingSettings
{
    [FieldOffset(0x00)] public ushort DefaultColorId;
}
