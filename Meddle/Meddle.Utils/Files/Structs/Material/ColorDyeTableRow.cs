using System.Runtime.InteropServices;

namespace Meddle.Utils.Files.Structs.Material;

[StructLayout(LayoutKind.Explicit, Size = 4)]
public struct ColorDyeTableRow
{
    [FieldOffset(0)]
    public uint Data;
    
    public ushort Template
    {
        get => (ushort)(Data >> 5);
        set => Data = (Data & 0x1Fu) | ((uint)value << 5);
    }
    
    public bool Diffuse
    {
        get => (Data & 0x01) != 0;
        set => Data = value ? Data | 0x01u : Data & 0xFFFEu;
    }
    
    public bool Specular
    {
        get => (Data & 0x02) != 0;
        set => Data = value ? Data | 0x02u : Data & 0xFFFDu;
    }
    
    public bool Emissive
    {
        get => (Data & 0x04) != 0;
        set => Data = value ? Data | 0x04u : Data & 0xFFFBu;
    }
    
    public bool Gloss
    {
        get => (Data & 0x08) != 0;
        set => Data = value ? Data | 0x08u : Data & 0xFFF7u;
    }
    
    public bool SpecularStrength
    {
        get => (Data & 0x10) != 0;
        set => Data = value ? Data | 0x10u : Data & 0xFFEFu;
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
    
    public bool Diffuse
    {
        get => (Data & 0x01) != 0;
        set => Data = (ushort)(value ? Data | 0x01 : Data & 0xFFFE);
    }
    
    public bool Specular
    {
        get => (Data & 0x02) != 0;
        set => Data = (ushort)(value ? Data | 0x02 : Data & 0xFFFD);
    }
    
    public bool Emissive
    {
        get => (Data & 0x04) != 0;
        set => Data = (ushort)(value ? Data | 0x04 : Data & 0xFFFB);
    }
    
    public bool Gloss
    {
        get => (Data & 0x08) != 0;
        set => Data = (ushort)(value ? Data | 0x08 : Data & 0xFFF7);
    }
    
    public bool SpecularStrength
    {
        get => (Data & 0x10) != 0;
        set => Data = (ushort)(value ? Data | 0x10 : Data & 0xFFEF);
    }
}
