using System.Numerics;

namespace Meddle.Utils.Export;

public class CustomizeData
{
    public bool LipStick;
    public bool Highlights;
    public bool FacePaintReversed;
}

public class CustomizeParameter {
    /// <summary>
    /// XYZ : Skin diffuse color, as squared RGB.
    /// </summary>
    public Vector3 SkinColor;
    
    public float MuscleTone;

    public Vector4 SkinFresnelValue0;

    /// <summary>
    /// XYZ : Lip diffuse color, as squared RGB.
    /// W : Lip opacity.
    /// </summary>
    public Vector4 LipColor;

    /// <summary>
    /// XYZ : Hair primary color, as squared RGB.
    /// </summary>
    public Vector3 MainColor;
    public float FacePaintUvMultiplier;

    public Vector3 HairFresnelValue0;
    
    /// <summary>
    /// XYZ : Hair highlight color, as squared RGB.
    /// </summary>
    public Vector3 MeshColor;
    public float FacePaintUvOffset;

    /// <summary>
    /// XYZ : Left eye color, as squared RGB.
    /// W : Left Eye Limbal Ring Intensity
    /// </summary>
    public Vector4 LeftColor;
    /// <summary>
    /// XYZ : Right eye color, as squared RGB.
    /// W : Right Eye Limbal Ring Intensity
    /// </summary>
    public Vector4 RightColor;

    /// <summary>
    /// XYZ : Race feature color, as squared RGB.
    /// </summary>
    public Vector3 OptionColor;

    public Vector4 DecalColor;
}
