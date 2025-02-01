using System.Numerics;
using System.Runtime.InteropServices;
namespace Meddle.Utils.Files.Structs.Material;

// https://github.com/Ottermandias/Penumbra.GameData/blob/main/Files/MaterialStructs/ColorTable.cs
/// <summary>
/// The number after the parameter is the bit in the dye flags.
/// <code>
/// #       |    X (+0)    |    |    Y (+1)    |    |    Z (+2)   |    |   W (+3)    |
/// --------------------------------------------------------------------------------------
/// 0 (+ 0) |    Diffuse.R |  0 |    Diffuse.G |  0 |   Diffuse.B |  0 |         Unk |  
/// 1 (+ 4) |   Specular.R |  1 |   Specular.G |  1 |  Specular.B |  1 |         Unk |
/// 2 (+ 8) |   Emissive.R |  2 |   Emissive.G |  2 |  Emissive.B |  2 |         Unk |  3
/// 3 (+12) |   Sheen Rate |  6 |   Sheen Tint |  7 |  Sheen Apt. |  8 |         Unk |
/// 4 (+16) |   Rougnhess? |  5 |              |    |  Metalness? |  4 |  Anisotropy |  9
/// 5 (+20) |          Unk |    |  Sphere Mask | 11 |         Unk |    |         Unk |   
/// 6 (+24) |   Shader Idx |    |   Tile Index |    |  Tile Alpha |    |  Sphere Idx | 10
/// 7 (+28) |   Tile XF UU |    |   Tile XF UV |    |  Tile XF VU |    |  Tile XF VV |
/// </code>
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x40)]
public struct ColorTableRow
{
    [StructLayout(LayoutKind.Explicit, Size = 0x8)]
    public struct ShortVec4
    {
        [FieldOffset(0x0)] public ushort X;
        [FieldOffset(0x2)] public ushort Y;
        [FieldOffset(0x4)] public ushort Z;
        [FieldOffset(0x6)] public ushort W;
    }
    
    [FieldOffset(0x0)] public ShortVec4 _diffuse;
    [FieldOffset(0x8)] public ShortVec4 _specular;
    [FieldOffset(0x10)] public ShortVec4 _emissive;
    [FieldOffset(0x18)] public ShortVec4 _sheen;
    [FieldOffset(0x20)] public ShortVec4 _r_u_m_a;
    [FieldOffset(0x28)] public ShortVec4 _unk2;
    [FieldOffset(0x30)] public ShortVec4 _idxData;
    [FieldOffset(0x38)] public ShortVec4 _tile;

    private static float ToFloat(ushort value)
        => (float)BitConverter.UInt16BitsToHalf(value);

    private static ushort FromFloat(float value)
        => BitConverter.HalfToUInt16Bits((Half)value);

    public Vector3 Diffuse
    {
        readonly get => new Vector3(ToFloat(_diffuse.X), ToFloat(_diffuse.Y), ToFloat(_diffuse.Z));
        set
        {
            _diffuse.X = FromFloat(value.X);
            _diffuse.Y = FromFloat(value.Y);
            _diffuse.Z = FromFloat(value.Z);
        }
    }
    
    public Vector3 Specular
    {
        readonly get => new Vector3(ToFloat(_specular.X), ToFloat(_specular.Y), ToFloat(_specular.Z));
        set
        {
            _specular.X = FromFloat(value.X);
            _specular.Y = FromFloat(value.Y);
            _specular.Z = FromFloat(value.Z);
        }
    }

    public Vector3 Emissive
    {
        readonly get => new Vector3(ToFloat(_emissive.X), ToFloat(_emissive.Y), ToFloat(_emissive.Z));
        set
        {
            _emissive.X = FromFloat(value.X);
            _emissive.Y = FromFloat(value.Y);
            _emissive.Z = FromFloat(value.Z);
        }
    }
    
    public float SheenRate
    {
        readonly get => ToFloat(_sheen.X);
        set => _sheen.X = FromFloat(value);
    }
    
    public float SheenTint
    {
        readonly get => ToFloat(_sheen.Y);
        set => _sheen.Y = FromFloat(value);
    }
    
    public float SheenAptitude
    {
        readonly get => ToFloat(_sheen.Z);
        set => _sheen.Z = FromFloat(value);
    }
    
    public float Roughness
    {
        readonly get => ToFloat(_r_u_m_a.X);
        set => _r_u_m_a.X = FromFloat(value);
    }
    
    public float Metalness
    {
        readonly get => ToFloat(_r_u_m_a.Z);
        set => _r_u_m_a.Z = FromFloat(value);
    }
    
    public float Anisotropy
    {
        readonly get => ToFloat(_r_u_m_a.W);
        set => _r_u_m_a.W = FromFloat(value);
    }
    
    // public float SpecularStrength
    // {
    //     readonly get => ToFloat(_specular.W);
    //     set => _specular.W = FromFloat(value);
    // }
    //
    // public float GlossStrength
    // {
    //     readonly get => ToFloat(_emissive.W);
    //     set => _emissive.W = FromFloat(value);
    // }
    
    public struct Matrix2x2
    {
        public float UU;
        public float UV;
        public float VU;
        public float VV;
    }
    
    public Matrix2x2 TileMatrix
    {
        readonly get => new()
        {
            UU = ToFloat(_tile.X),
            UV = ToFloat(_tile.Y),
            VU = ToFloat(_tile.Z),
            VV = ToFloat(_tile.W)
        };
        set
        {
            _tile.X = FromFloat(value.UU);
            _tile.Y = FromFloat(value.UV);
            _tile.Z = FromFloat(value.VU);
            _tile.W = FromFloat(value.VV);
        }
    }
    
    public float SphereMask
    {
        readonly get => ToFloat(_unk2.Y);
        set => _unk2.Y = FromFloat(value);
    }
    
    public ushort ShaderId
    {
        readonly get => _idxData.X;
        set => _idxData.X = value;
    }
    
    public byte TileIndex
    {
        readonly get => (byte)(ToFloat(_idxData.Y) * 64f);
        set => _idxData.Y = FromFloat((value + 0.5f) / 64f);
    }

    
    public float TileAlpha
    {
        readonly get => ToFloat(_idxData.Z);
        set => _idxData.Z = FromFloat(value);
    }
    
    public ushort SphereIndex
    {
        readonly get => _idxData.W;
        set => _idxData.W = value;
    }
}

/// <summary>
/// <code>
/// #       |    X (+0)    |    Y (+1)    |    Z (+2)   |     W (+3)    |
/// --------------------------------------------------------------------------------------
/// 0 (+ 0) |    Diffuse.R |    Diffuse.G |   Diffuse.B | Spec Strength |  
/// 1 (+ 4) |   Specular.R |   Specular.G |  Specular.B | Glos Strength |
/// 2 (+ 8) |   Emissive.R |   Emissive.G |  Emissive.B | TileIndexW    | 
/// 3 (+12) |  TileScaleUU |  TileScaleUV | TileScaleVU | TileScaleVV   |
/// </code>
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x20)]
public struct LegacyColorTableRow
{
    [StructLayout(LayoutKind.Explicit, Size = 0x8)]
    public struct ShortVec4
    {
        [FieldOffset(0x0)] public ushort X;
        [FieldOffset(0x2)] public ushort Y;
        [FieldOffset(0x4)] public ushort Z;
        [FieldOffset(0x6)] public ushort W;
    }
    
    [FieldOffset(0x0)] public ShortVec4 _diffuse;
    [FieldOffset(0x8)] public ShortVec4 _specular;
    [FieldOffset(0x10)] public ShortVec4 _emissive;
    [FieldOffset(0x18)] public ShortVec4 _tile;

    private static float ToFloat(ushort value)
        => (float)BitConverter.UInt16BitsToHalf(value);

    private static ushort FromFloat(float value)
        => BitConverter.HalfToUInt16Bits((Half)value);

    public Vector3 Diffuse
    {
        readonly get => new Vector3(ToFloat(_diffuse.X), ToFloat(_diffuse.Y), ToFloat(_diffuse.Z));
        set
        {
            _diffuse.X = FromFloat(value.X);
            _diffuse.Y = FromFloat(value.Y);
            _diffuse.Z = FromFloat(value.Z);
        }
    }
    
    public Vector3 Specular
    {
        readonly get => new Vector3(ToFloat(_specular.X), ToFloat(_specular.Y), ToFloat(_specular.Z));
        set
        {
            _specular.X = FromFloat(value.X);
            _specular.Y = FromFloat(value.Y);
            _specular.Z = FromFloat(value.Z);
        }
    }

    public Vector3 Emissive
    {
        readonly get => new Vector3(ToFloat(_emissive.X), ToFloat(_emissive.Y), ToFloat(_emissive.Z));
        set
        {
            _emissive.X = FromFloat(value.X);
            _emissive.Y = FromFloat(value.Y);
            _emissive.Z = FromFloat(value.Z);
        }
    }
    
    public float SpecularStrength
    {
        readonly get => ToFloat(_diffuse.W);
        set => _specular.W = FromFloat(value);
    }
    
    public float GlossStrength
    {
        readonly get => ToFloat(_specular.W);
        set => _emissive.W = FromFloat(value);
    }
    
    public Vector2 MaterialRepeat
    {
        readonly get => new Vector2(ToFloat(_tile.X), ToFloat(_tile.Y));
        set
        {
            _tile.X = FromFloat(value.X);
            _tile.Y = FromFloat(value.Y);
        }
    }
    
    public Vector2 MaterialSkew
    {
        readonly get => new Vector2(ToFloat(_tile.Z), ToFloat(_tile.W));
        set
        {
            _tile.Z = FromFloat(value.X);
            _tile.W = FromFloat(value.Y);
        }
    }
    
    public ushort TileIndex
    {
        readonly get => _emissive.Y;
        set => _emissive.Y = value;
    }
}
