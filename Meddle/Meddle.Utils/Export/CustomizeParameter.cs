using System.Numerics;

namespace Meddle.Utils.Export;

public class CustomizeParameter {
    /// <summary>
    /// XYZ : Skin diffuse color, as squared RGB.
    /// W : Muscle tone.
    /// </summary>
    public Vector4 SkinColor;
    /// <summary>
    /// XYZ : Skin specular color, as squared RGB.
    /// </summary>
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
    /// <summary>
    /// XYZ : Hair specular color, as squared RGB.
    /// </summary>
    public Vector3 HairFresnelValue0;
    /// <summary>
    /// XYZ : Hair highlight color, as squared RGB.
    /// </summary>
    public Vector3 MeshColor;

    /// <summary>
    /// XYZ : Left eye color, as squared RGB.
    /// W : Face paint (UV2) U multiplier.
    /// </summary>
    public Vector4 LeftColor;
    /// <summary>
    /// XYZ : Right eye color, as squared RGB.
    /// W : Face paint (UV2) U offset.
    /// </summary>
    public Vector4 RightColor;

    /// <summary>
    /// XYZ : Race feature color, as squared RGB.
    /// </summary>
    public Vector3 OptionColor;

    public CustomizeParameter()
    {
    }
    
    public CustomizeParameter(FFXIVClientStructs.FFXIV.Shader.CustomizeParameter parameter)
    {
        SkinColor = parameter.SkinColor;
        SkinFresnelValue0 = parameter.SkinFresnelValue0;
        LipColor = parameter.LipColor;
        MainColor = parameter.MainColor;
        HairFresnelValue0 = parameter.HairFresnelValue0;
        MeshColor = parameter.MeshColor;
        LeftColor = parameter.LeftColor;
        RightColor = parameter.RightColor;
        OptionColor = parameter.OptionColor;
    }
}
