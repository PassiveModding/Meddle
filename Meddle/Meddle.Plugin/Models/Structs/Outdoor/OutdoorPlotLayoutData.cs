using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;

namespace Meddle.Plugin.Models.Structs.Outdoor;

[StructLayout(LayoutKind.Explicit, Size = 0x1D0)]
public unsafe struct OutdoorPlotLayoutData
{
    [FieldOffset(0x0)] public FFXIVClientStructs.FFXIV.Client.LayoutEngine.OutdoorPlotLayoutData Native;
    [FieldOffset(0x0)] public SharedGroupLayoutInstance* PlotLayoutInstance;
}

[StructLayout(LayoutKind.Explicit, Size = 0x28)]
public struct OutdoorPlotFixtureData
{
    [FieldOffset(0x0)] public FFXIVClientStructs.FFXIV.Client.LayoutEngine.OutdoorPlotFixtureData Native;
    [FieldOffset(0x0)] public ushort FixtureId;
    [FieldOffset(0x2)] public byte StainId;
    [FieldOffset(0x8)] public unsafe UnkGroupHolder* UnkGroup;
}

[StructLayout(LayoutKind.Explicit, Size = 0x10)]
public struct UnkGroupHolder
{
    [FieldOffset(0x8)] public unsafe SharedGroupLayoutInstance* FixtureLayoutInstance;
}
