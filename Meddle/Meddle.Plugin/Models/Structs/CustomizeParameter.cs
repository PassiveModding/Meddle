using System.Numerics;
using System.Runtime.InteropServices;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x70)]
public struct CustomizeParameter
{
    /// <summary>
    ///     XYZ : Skin diffuse color, as squared RGB.
    /// </summary>
    [FieldOffset(0x0)]
    public Vector3 SkinColor;

    [FieldOffset(0xC)]
    public float MuscleTone;

    /// <summary>
    ///     XYZ : Lip diffuse color, as squared RGB.
    ///     W : Lip opacity.
    /// </summary>
    [FieldOffset(0x10)]
    public Vector4 LipColor;

    /// <summary>
    ///     XYZ : Hair primary color, as squared RGB.
    /// </summary>
    [FieldOffset(0x20)]
    public Vector3 MainColor;

    [FieldOffset(0x2C)]
    public float FacePaintUVMultiplier;

    /// <summary>
    ///     XYZ : Hair highlight color, as squared RGB.
    /// </summary>
    [FieldOffset(0x30)]
    public Vector3 MeshColor;

    [FieldOffset(0x3C)]
    public float FacePaintUVOffset;

    /// <summary>
    ///     XYZ : Left eye color, as squared RGB.
    ///     W : Left Eye Limbal Ring Intensity
    /// </summary>
    [FieldOffset(0x40)]
    public Vector4 LeftColor;

    /// <summary>
    ///     XYZ : Right eye color, as squared RGB.
    ///     W : Right Eye Limbal Ring Intensity
    /// </summary>
    [FieldOffset(0x50)]
    public Vector4 RightColor;

    /// <summary>
    ///     XYZ : Race feature color, as squared RGB.
    /// </summary>
    [FieldOffset(0x60)]
    public Vector3 OptionColor;
    
    [FieldOffset(0x6C)]
    public float Unk1;
}
