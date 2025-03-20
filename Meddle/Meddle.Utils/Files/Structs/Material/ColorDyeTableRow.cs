using System.Runtime.InteropServices;

namespace Meddle.Utils.Files.Structs.Material;

// https://github.com/Ottermandias/Penumbra.GameData/blob/757aaa39ac4aa988d0b8597ff088641a0f4f49fd/Files/MaterialStructs/ColorDyeTableRow.cs
[StructLayout(LayoutKind.Explicit, Size = 4)]
public struct ColorDyeTableRow
{
    [FieldOffset(0)]
    public uint Data;
    
    public ushort Template
    {
        readonly get => (ushort)((Data >> 16) & 0x7FF);
        set => Data = (Data & ~0x7FF0000u) | ((uint)(value & 0x7FF) << 16);
    }
    
    public byte Channel
    {
        readonly get => (byte)((Data >> 27) & 0x3);
        set => Data = (Data & ~0x18000000u) | ((uint)(value & 0x3) << 27);
    }

    public bool DiffuseColor
    {
        readonly get => (Data & 0x0001) != 0;
        set => Data = value ? Data | 0x0001u : Data & ~0x0001u;
    }

    public bool SpecularColor
    {
        readonly get => (Data & 0x0002) != 0;
        set => Data = value ? Data | 0x0002u : Data & ~0x0002u;
    }

    public bool EmissiveColor
    {
        readonly get => (Data & 0x0004) != 0;
        set => Data = value ? Data | 0x0004u : Data & ~0x0004u;
    }

    public bool Scalar3
    {
        readonly get => (Data & 0x0008) != 0;
        set => Data = value ? Data | 0x0008u : Data & ~0x0008u;
    }

    public bool Metalness
    {
        readonly get => (Data & 0x0010) != 0;
        set => Data = value ? Data | 0x0010u : Data & ~0x0010u;
    }

    public bool Roughness
    {
        readonly get => (Data & 0x0020) != 0;
        set => Data = value ? Data | 0x0020u : Data & ~0x0020u;
    }

    public bool SheenRate
    {
        readonly get => (Data & 0x0040) != 0;
        set => Data = value ? Data | 0x0040u : Data & ~0x0040u;
    }

    public bool SheenTintRate
    {
        readonly get => (Data & 0x0080) != 0;
        set => Data = value ? Data | 0x0080u : Data & ~0x0080u;
    }

    public bool SheenAperture
    {
        readonly get => (Data & 0x0100) != 0;
        set => Data = value ? Data | 0x0100u : Data & ~0x0100u;
    }

    public bool Anisotropy
    {
        readonly get => (Data & 0x0200) != 0;
        set => Data = value ? Data | 0x0200u : Data & ~0x0200u;
    }

    public bool SphereMapIndex
    {
        readonly get => (Data & 0x0400) != 0;
        set => Data = value ? Data | 0x0400u : Data & ~0x0400u;
    }

    public bool SphereMapMask
    {
        readonly get => (Data & 0x0800) != 0;
        set => Data = value ? Data | 0x0800u : Data & ~0x0200u;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 2)]
public struct LegacyColorDyeTableRow
{
    [FieldOffset(0)]
    public ushort Data;
    
    public ushort Template
    {
        get => (ushort)(Data >> 5);
        set => Data = (ushort)((Data & 0x1F) | (value << 5));
    }
    
    public bool DiffuseColor
    {
        get => (Data & 0x01) != 0;
        set => Data = (ushort)(value ? Data | 0x01 : Data & 0xFFFE);
    }
    
    public bool SpecularColor
    {
        get => (Data & 0x02) != 0;
        set => Data = (ushort)(value ? Data | 0x02 : Data & 0xFFFD);
    }
    
    public bool EmissiveColor
    {
        get => (Data & 0x04) != 0;
        set => Data = (ushort)(value ? Data | 0x04 : Data & 0xFFFB);
    }
    
    public bool Shininess
    {
        get => (Data & 0x08) != 0;
        set => Data = (ushort)(value ? Data | 0x08 : Data & 0xFFF7);
    }
    
    public bool SpecularMask
    {
        get => (Data & 0x10) != 0;
        set => Data = (ushort)(value ? Data | 0x10 : Data & 0xFFEF);
    }
}
