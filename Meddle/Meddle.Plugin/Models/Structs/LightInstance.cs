using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x50)]
public unsafe struct LightLayoutInstance
{
    [FieldOffset(0x08)] public LayerManager* LayerManager;
    [FieldOffset(0x10)] public LayoutManager* LayoutManager;
    [FieldOffset(0x30)] public Light* LightPtr;
}

[StructLayout(LayoutKind.Explicit, Size = 0x98)]
public unsafe struct Light
{
    [FieldOffset(0x00)] public DrawObject DrawObject;
    [FieldOffset(0x90)] public LightItem* LightItem;
}

// at least 0x90, now sure if this is correct
[StructLayout(LayoutKind.Explicit, Size = 0x90)]
public unsafe struct LightItem
{
    [FieldOffset(0x20)] public Transform* Transform;
    [FieldOffset(0x8C)] public float UnkFloat;
}
