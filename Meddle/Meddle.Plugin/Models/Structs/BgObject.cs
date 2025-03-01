using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0xD0)]
public struct BgObject
{
    [FieldOffset(0x00)] public DrawObject DrawObject;
    
    // if (DrawObjectFlags2 & 0xF) == 3,
    [FieldOffset(0x89)] public byte DrawObjectFlags2; // indicates model relations?
    [FieldOffset(0x90)] public unsafe FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.ModelResourceHandle* ModelResourceHandle;
    
    // meshCount         -> 0x98 |= 1u, unkCount = 1
    // waterMeshCount    -> 0x98 |= 2 << (4 * unkCount), unkCount++
    // lods[2].meshCount -> 0x98 |= 3 << (4 * unkCount), unkCount++
    // terrainShadowMesh -> 0x98 |= 5 << (4 * unkCount), unkCount++
    [FieldOffset(0x98)] public uint UnkFlags;
    

    // vf26(this, float a2) -> 0xBE = a2 * 255.0;
    // vf27(this) -> return 0xBE / 255.0;
    [FieldOffset(0xBE)] public byte UnkFlags1; 
    [FieldOffset(0xC9)] public byte UnkFlags2;

    // BGObject_UpdateMaterials
    // 1. iterate each lod of each mesh type, check if >= meshCount, if so, ignore
    // 2. if not >= meshcount, set unkflags and increment unkcount
}
