using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

// ReSharper disable InconsistentNaming
namespace Meddle.Utils.Files.Structs.Material;

// https://github.com/Ottermandias/Penumbra.GameData/blob/main/Files/MaterialStructs/ColorTable.cs
/// <summary>
/// The number after the parameter is the bit in the dye flags.
/// <code>
/// #       |    X (+0)    |    |    Y (+1)    |    |    Z (+2)   |    |   W (+3)    |
/// --------------------------------------------------------------------------------------
/// 0 (+ 0) |    Diffuse.R |  0 |    Diffuse.G |  0 |   Diffuse.B |  0 |    (L)Gloss |  
/// 1 (+ 4) |   Specular.R |  1 |   Specular.G |  1 |  Specular.B |  1 | (L)Spec Str |
/// 2 (+ 8) |   Emissive.R |  2 |   Emissive.G |  2 |  Emissive.B |  2 |         Unk |  3
/// 3 (+12) |   Sheen Rate |  6 |   Sheen Tint |  7 |  Sheen Apt. |  8 |         Unk |
/// 4 (+16) |   Rougnhess? |  5 |              |    |  Metalness? |  4 |  Anisotropy |  9
/// 5 (+20) |          Unk |    |  Sphere Mask | 11 |         Unk |    |         Unk |   
/// 6 (+24) |   Shader Idx |    |   Tile Index |    |  Tile Alpha |    |  Sphere Idx | 10
/// 7 (+28) |   Tile XF UU |    |   Tile XF UV |    |  Tile XF VU |    |  Tile XF VV |
/// </code>
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct ShortVec4
{
    public const int Size = 0x8;
    [FieldOffset(0x0)] public ushort X;
    [FieldOffset(0x2)] public ushort Y;
    [FieldOffset(0x4)] public ushort Z;
    [FieldOffset(0x6)] public ushort W;
    
    public static float ToFloat(ushort value)
        => (float)BitConverter.UInt16BitsToHalf(value);

    public static ushort FromFloat(float value)
        => BitConverter.HalfToUInt16Bits((Half)value);

    public Vector4 ToVector4()
    {
        return new Vector4(ToFloat(X), ToFloat(Y), ToFloat(Z), ToFloat(W));
    }
    
    private static Vector4 Clamp(Vector4 v, float min, float max)
    {
        return new Vector4(
            Math.Clamp(v.X, min, max),
            Math.Clamp(v.Y, min, max),
            Math.Clamp(v.Z, min, max),
            Math.Clamp(v.W, min, max)
        );
    }
    
    public SKColor ToSkColor()
    {
        var color = ToVector4();
        var c = Clamp(color, 0, 1);
        return new SKColor((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), (byte)(c.W * 255));
    }
}

[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct ColorTableRow
{
    public const int Size = 0x40;
    [FieldOffset(0x0)] public unsafe fixed ushort Data[Size/2];
    [FieldOffset(0x0)] public ShortVec4 _diffuse;
    [FieldOffset(0x8)] public ShortVec4 _specular;
    [FieldOffset(0x10)] public ShortVec4 _emissive;
    [FieldOffset(0x18)] public ShortVec4 _sheen;
    [FieldOffset(0x20)] public ShortVec4 _r_u_m_a;
    [FieldOffset(0x28)] public ShortVec4 _unk2;
    [FieldOffset(0x30)] public ShortVec4 _idxData;
    [FieldOffset(0x38)] public ShortVec4 _tile;

    public Vector3 Diffuse
    {
        readonly get => new Vector3(ShortVec4.ToFloat(_diffuse.X), ShortVec4.ToFloat(_diffuse.Y), ShortVec4.ToFloat(_diffuse.Z));
        set
        {
            _diffuse.X = ShortVec4.FromFloat(value.X);
            _diffuse.Y = ShortVec4.FromFloat(value.Y);
            _diffuse.Z = ShortVec4.FromFloat(value.Z);
        }
    }

    public float GlossStrength
    {
        readonly get => ShortVec4.ToFloat(_diffuse.W);
        set => _diffuse.W = ShortVec4.FromFloat(value);
    }
    
    public Vector3 Specular
    {
        readonly get => new Vector3(ShortVec4.ToFloat(_specular.X), ShortVec4.ToFloat(_specular.Y), ShortVec4.ToFloat(_specular.Z));
        set
        {
            _specular.X = ShortVec4.FromFloat(value.X);
            _specular.Y = ShortVec4.FromFloat(value.Y);
            _specular.Z = ShortVec4.FromFloat(value.Z);
        }
    }
    
    public float SpecularStrength
    {
        readonly get => ShortVec4.ToFloat(_specular.W);
        set => _specular.W = ShortVec4.FromFloat(value);
    }

    public Vector3 Emissive
    {
        readonly get => new Vector3(ShortVec4.ToFloat(_emissive.X), ShortVec4.ToFloat(_emissive.Y), ShortVec4.ToFloat(_emissive.Z));
        set
        {
            _emissive.X = ShortVec4.FromFloat(value.X);
            _emissive.Y = ShortVec4.FromFloat(value.Y);
            _emissive.Z = ShortVec4.FromFloat(value.Z);
        }
    }
    
    public float SheenRate
    {
        readonly get => ShortVec4.ToFloat(_sheen.X);
        set => _sheen.X = ShortVec4.FromFloat(value);
    }
    
    public float SheenTint
    {
        readonly get => ShortVec4.ToFloat(_sheen.Y);
        set => _sheen.Y = ShortVec4.FromFloat(value);
    }
    
    public float SheenAptitude
    {
        readonly get => ShortVec4.ToFloat(_sheen.Z);
        set => _sheen.Z = ShortVec4.FromFloat(value);
    }
    
    public float Roughness
    {
        readonly get => ShortVec4.ToFloat(_r_u_m_a.X);
        set => _r_u_m_a.X = ShortVec4.FromFloat(value);
    }
    
    public float Metalness
    {
        readonly get => ShortVec4.ToFloat(_r_u_m_a.Z);
        set => _r_u_m_a.Z = ShortVec4.FromFloat(value);
    }
    
    public float Anisotropy
    {
        readonly get => ShortVec4.ToFloat(_r_u_m_a.W);
        set => _r_u_m_a.W = ShortVec4.FromFloat(value);
    }
    
    // public float SpecularStrength
    // {
    //     readonly get => ShortVec4.ToFloat(_specular.W);
    //     set => _specular.W = ShortVec4.FromFloat(value);
    // }
    //
    // public float GlossStrength
    // {
    //     readonly get => ShortVec4.ToFloat(_emissive.W);
    //     set => _emissive.W = ShortVec4.FromFloat(value);
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
            UU = ShortVec4.ToFloat(_tile.X),
            UV = ShortVec4.ToFloat(_tile.Y),
            VU = ShortVec4.ToFloat(_tile.Z),
            VV = ShortVec4.ToFloat(_tile.W)
        };
        set
        {
            _tile.X = ShortVec4.FromFloat(value.UU);
            _tile.Y = ShortVec4.FromFloat(value.UV);
            _tile.Z = ShortVec4.FromFloat(value.VU);
            _tile.W = ShortVec4.FromFloat(value.VV);
        }
    }
    
    public float SphereMask
    {
        readonly get => ShortVec4.ToFloat(_unk2.Y);
        set => _unk2.Y = ShortVec4.FromFloat(value);
    }
    
    public ushort ShaderId
    {
        readonly get => _idxData.X;
        set => _idxData.X = value;
    }
    
    public byte TileIndex
    {
        readonly get => (byte)(ShortVec4.ToFloat(_idxData.Y) * 64f);
        set => _idxData.Y = ShortVec4.FromFloat((value + 0.5f) / 64f);
    }

    
    public float TileAlpha
    {
        readonly get => ShortVec4.ToFloat(_idxData.Z);
        set => _idxData.Z = ShortVec4.FromFloat(value);
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
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct LegacyColorTableRow
{
    // [StructLayout(LayoutKind.Explicit, Size = 0x8)]
    // public struct ShortVec4
    // {
    //     [FieldOffset(0x0)] public ushort X;
    //     [FieldOffset(0x2)] public ushort Y;
    //     [FieldOffset(0x4)] public ushort Z;
    //     [FieldOffset(0x6)] public ushort W;
    // }
    public const int Size = 0x20;
    [FieldOffset(0x0)] public ShortVec4 _diffuse;
    [FieldOffset(0x8)] public ShortVec4 _specular;
    [FieldOffset(0x10)] public ShortVec4 _emissive;
    [FieldOffset(0x18)] public ShortVec4 _tile;

    public Vector3 Diffuse
    {
        readonly get => new Vector3(ShortVec4.ToFloat(_diffuse.X), ShortVec4.ToFloat(_diffuse.Y), ShortVec4.ToFloat(_diffuse.Z));
        set
        {
            _diffuse.X = ShortVec4.FromFloat(value.X);
            _diffuse.Y = ShortVec4.FromFloat(value.Y);
            _diffuse.Z = ShortVec4.FromFloat(value.Z);
        }
    }
    
    public Vector3 Specular
    {
        readonly get => new Vector3(ShortVec4.ToFloat(_specular.X), ShortVec4.ToFloat(_specular.Y), ShortVec4.ToFloat(_specular.Z));
        set
        {
            _specular.X = ShortVec4.FromFloat(value.X);
            _specular.Y = ShortVec4.FromFloat(value.Y);
            _specular.Z = ShortVec4.FromFloat(value.Z);
        }
    }

    public Vector3 Emissive
    {
        readonly get => new Vector3(ShortVec4.ToFloat(_emissive.X), ShortVec4.ToFloat(_emissive.Y), ShortVec4.ToFloat(_emissive.Z));
        set
        {
            _emissive.X = ShortVec4.FromFloat(value.X);
            _emissive.Y = ShortVec4.FromFloat(value.Y);
            _emissive.Z = ShortVec4.FromFloat(value.Z);
        }
    }
    
    public float SpecularStrength
    {
        readonly get => ShortVec4.ToFloat(_diffuse.W);
        set => _specular.W = ShortVec4.FromFloat(value);
    }
    
    public float GlossStrength
    {
        readonly get => ShortVec4.ToFloat(_specular.W);
        set => _emissive.W = ShortVec4.FromFloat(value);
    }
    
    public Vector2 MaterialRepeat
    {
        readonly get => new Vector2(ShortVec4.ToFloat(_tile.X), ShortVec4.ToFloat(_tile.Y));
        set
        {
            _tile.X = ShortVec4.FromFloat(value.X);
            _tile.Y = ShortVec4.FromFloat(value.Y);
        }
    }
    
    public Vector2 MaterialSkew
    {
        readonly get => new Vector2(ShortVec4.ToFloat(_tile.Z), ShortVec4.ToFloat(_tile.W));
        set
        {
            _tile.Z = ShortVec4.FromFloat(value.X);
            _tile.W = ShortVec4.FromFloat(value.Y);
        }
    }
    
    public ushort TileIndex
    {
        readonly get => _emissive.Y;
        set => _emissive.Y = value;
    }
}
