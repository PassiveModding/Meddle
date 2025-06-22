using System.Runtime.InteropServices;

namespace Meddle.Plugin.Models.Structs;


[StructLayout(LayoutKind.Explicit, Size = 560)]
public struct PartialSkeletonEx
{
    [FieldOffset(0x08)]
    public uint PartialSkeletonFlags;
    
    public uint BoneCount => (PartialSkeletonFlags >> 5) & 0xFFFu;
}
