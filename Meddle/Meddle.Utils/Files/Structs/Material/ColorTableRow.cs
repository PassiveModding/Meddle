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
    
    public Vector3 ToVector3()
    {
        return new Vector3(ToFloat(X), ToFloat(Y), ToFloat(Z));
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

    // 0,1,2
    public Vector3 Diffuse => _diffuse.ToVector3(); 

    // 3
    public float GlossStrength => ShortVec4.ToFloat(_diffuse.W);
    public float Scalar3 => ShortVec4.ToFloat(_diffuse.W);
    
    // 4,5,6
    public Vector3 Specular => _specular.ToVector3();
    
    // 7
    public float SpecularStrength => ShortVec4.ToFloat(_specular.W);
    public float Scalar7 => ShortVec4.ToFloat(_specular.W);

    // 8,9,10
    public Vector3 Emissive => _emissive.ToVector3();
    
    // 11
    public float Scalar11 => ShortVec4.ToFloat(_emissive.W);
    
    // 12,13,14
    public float SheenRate => ShortVec4.ToFloat(_sheen.X);
    
    public float SheenTint => ShortVec4.ToFloat(_sheen.Y);
    
    public float SheenAptitude => ShortVec4.ToFloat(_sheen.Z);
    
    // 15
    public float Scalar15 => ShortVec4.ToFloat(_sheen.W);
    
    // 16
    public float Roughness => ShortVec4.ToFloat(_r_u_m_a.X);
    
    // 17
    public float Scalar17 => ShortVec4.ToFloat(_r_u_m_a.Y);
    
    // 18
    public float Metalness => ShortVec4.ToFloat(_r_u_m_a.Z);
    
    // 19
    public float Anisotropy => ShortVec4.ToFloat(_r_u_m_a.W);
    
    // 20
    public float Scalar20 => ShortVec4.ToFloat(_unk2.X);
    
    // 21
    public float SphereMask => ShortVec4.ToFloat(_unk2.Y);
    
    // 22,23
    public float Scalar22 => ShortVec4.ToFloat(_unk2.Z);
    public float Scalar23 => ShortVec4.ToFloat(_unk2.W);
    
    // 24,25,26,27
    public ushort ShaderId => _idxData.X;
    public byte TileIndex => (byte)(ShortVec4.ToFloat(_idxData.Y) * 64f);
    public float TileAlpha => ShortVec4.ToFloat(_idxData.Z);
    public ushort SphereIndex => _idxData.W;
    
    public struct Matrix2x2
    {
        public float UU;
        public float UV;
        public float VU;
        public float VV;
    }

    // 28,29,30,31
    public Matrix2x2 TileMatrix => new()
    {
        UU = ShortVec4.ToFloat(_tile.X),
        UV = ShortVec4.ToFloat(_tile.Y),
        VU = ShortVec4.ToFloat(_tile.Z),
        VV = ShortVec4.ToFloat(_tile.W)
    };
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
    public const int Size = 0x20;
    [FieldOffset(0x0)] public ShortVec4 _diffuse;
    [FieldOffset(0x8)] public ShortVec4 _specular;
    [FieldOffset(0x10)] public ShortVec4 _emissive;
    [FieldOffset(0x18)] public ShortVec4 _tile;

    public Vector3 Diffuse => _diffuse.ToVector3();
    public float SpecularStrength => ShortVec4.ToFloat(_diffuse.W);

    public Vector3 Specular => _specular.ToVector3();
    
    public float GlossStrength => ShortVec4.ToFloat(_specular.W);

    public Vector3 Emissive => _emissive.ToVector3();
    
    public ushort TileIndex => _emissive.W;

    public Vector2 MaterialRepeat => new (ShortVec4.ToFloat(_tile.X), ShortVec4.ToFloat(_tile.Y));
    
    public Vector2 MaterialSkew => new (ShortVec4.ToFloat(_tile.Z), ShortVec4.ToFloat(_tile.W));
}
