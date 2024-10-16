using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x1D0)]
public struct MeddleHousingObject {
    [FieldOffset(0x00)] public HousingObject HousingObject;
    [FieldOffset(0x1B0)] public byte ColorId;
}

[StructLayout(LayoutKind.Explicit, Size = 0x1E0)]
public unsafe struct MeddleSharedGroupLayoutInstance
{
    [FieldOffset(0x00)] public SharedGroupLayoutInstance SharedGroupLayoutInstance;
    [FieldOffset(0x1C)] public byte ColorId;
    [FieldOffset(0x1F)] public byte HasHousingObject;
    [FieldOffset(0x20)] public byte ColorId0;
    [FieldOffset(0x21)] public byte ColorId1;
}
