using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x1F0)]
public unsafe struct Terrain
{
    [FieldOffset(0x00)] public FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Terrain* Base;
    [FieldOffset(0x90)] public ResourceHandle* TerrainResourceHandle;
    [FieldOffset(0x98)] public ModelResourceHandle** ModelResourceHandles; // Pointer to an array of ModelResourceHandle pointers
    [FieldOffset(0xA0)] public uint ModelResourceHandleCount; // Number of ModelResourceHandles in the array
    
    public Span<Pointer<ModelResourceHandle>> ModelResourceHandlesSpan => new(ModelResourceHandles, (int)ModelResourceHandleCount);
}
