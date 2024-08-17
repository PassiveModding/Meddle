using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0xD0)]
public struct BgObject
{
    [FieldOffset(0x90)] public unsafe ResourceHandle* ResourceHandle;
    [FieldOffset(0x90)] public unsafe ModelResourceHandle* ModelResourceHandle;
}
