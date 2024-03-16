using System.Numerics;
using FFXIVClientStructs.FFXIV.Shader;

namespace Meddle.Plugin.Models;

public class CustomizeParameters
{
    public bool IsHrothgar;
    
    /// <summary>
    /// XYZ : Skin diffuse color
    /// W : Muscle tone.
    /// </summary>
    public Vector4 SkinColor;
    /// <summary>
    /// XYZ : Skin specular color
    /// </summary>
    public Vector4 SkinFresnelValue0;

    /// <summary>
    /// XYZ : Lip diffuse color
    /// W : Lip opacity.
    /// </summary>
    public Vector4 LipColor;
    public bool ApplyLipColor;

    /// <summary>
    /// XYZ : Hair primary color
    /// </summary>
    public Vector3 MainColor;
    /// <summary>
    /// XYZ : Hair specular color
    /// </summary>
    public Vector3 HairFresnelValue0;
    /// <summary>
    /// XYZ : Hair highlight color
    /// </summary>
    public Vector3 MeshColor;

    /// <summary>
    /// XYZ : Left eye color
    /// W : Face paint (UV2) U multiplier.
    /// </summary>
    public Vector4 LeftColor;
    /// <summary>
    /// XYZ : Right eye color
    /// W : Face paint (UV2) U offset.
    /// </summary>
    public Vector4 RightColor;

    /// <summary>
    /// XYZ : Race feature color
    /// </summary>
    public Vector3 OptionColor;

    public CustomizeParameters(CustomizeParameter customizeParameter, bool isHrothgar)
    {
        SkinColor = FromSquaredRgb(customizeParameter.SkinColor);
        SkinFresnelValue0 = FromSquaredRgb(customizeParameter.SkinFresnelValue0);
        LipColor = FromSquaredRgb(customizeParameter.LipColor);
        MainColor = FromSquaredRgb(customizeParameter.MainColor);
        HairFresnelValue0 = FromSquaredRgb(customizeParameter.HairFresnelValue0);
        MeshColor = FromSquaredRgb(customizeParameter.MeshColor);
        LeftColor = FromSquaredRgb(customizeParameter.LeftColor);
        RightColor = FromSquaredRgb(customizeParameter.RightColor);
        OptionColor = FromSquaredRgb(customizeParameter.OptionColor);
        IsHrothgar = isHrothgar;
        ApplyLipColor = customizeParameter.LipColor.W > 0;
    }
    
    private static Vector4 FromSquaredRgb(Vector4 input)
    {
        return new(Root(input.X), Root(input.Y), Root(input.Z), input.W);
    }
    
    private static Vector3 FromSquaredRgb(Vector3 input)
    {
        return new(Root(input.X), Root(input.Y), Root(input.Z));
    }
    
    
    private static float Root(float x)
        => x < 0 ? -(float)Math.Sqrt(-x) : (float)Math.Sqrt(x);
}
