using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x38)]
public unsafe struct DecalLayoutInstance
{
    [FieldOffset(0x08)] public LayerManager* LayerManager;
    [FieldOffset(0x10)] public LayoutManager* LayoutManager;
    [FieldOffset(0x30)] public Decal* DecalPtr;
}

[StructLayout(LayoutKind.Explicit, Size = 0x98)]
public unsafe struct Decal
{
    [FieldOffset(0x00)] public DrawObject DrawObject;
    [FieldOffset(0x90)] public DecalItem* DecalItem;
}

[StructLayout(LayoutKind.Explicit, Size = 0x70)]
public unsafe struct DecalItem
{
    [FieldOffset(0x28)] public TextureResourceHandle* TexDiffuse;
    [FieldOffset(0x30)] public TextureResourceHandle* TexNormal;
    [FieldOffset(0x38)] public TextureResourceHandle* TexSpecular;
    [FieldOffset(0x40)] public ushort OffsetX;
}
