using System.Numerics;
using FFXIVClientStructs.FFXIV.Shader;

namespace Meddle.Plugin.Models;

public class SkinShaderParameters
{
    public Vector4 SkinColor;
    public Vector3 MainColor;
    public Vector3 MeshColor;
    public Vector4 LipColor;
    public Vector3 SkinFresnelValue0;
    public float SkinFresnelValue0W;
    public Vector3 HairFresnelValue0;
    public bool IsHrothgar;
        
    public static SkinShaderParameters From(CustomizeParameters parameters)
    {
        var sfv0 = parameters.SkinFresnelValue0;
        var skinFresnelValue0W = sfv0.W;
        var skinFresnelValue0Xyz = new Vector3(sfv0.X, sfv0.Y, sfv0.Z);
        
        return new SkinShaderParameters
        {
            IsHrothgar = parameters.IsHrothgar,
            SkinColor = parameters.SkinColor,
            MainColor = parameters.MainColor,
            MeshColor = parameters.MeshColor,
            LipColor = parameters.LipColor,
            SkinFresnelValue0 = skinFresnelValue0Xyz,
            SkinFresnelValue0W = skinFresnelValue0W,
            HairFresnelValue0 = parameters.HairFresnelValue0
        };
    }
}

public class HairShaderParameters
{
    public Vector3 MainColor;
    public Vector3 MeshColor;
    public Vector3 OptionColor;
    public Vector3 HairFresnelValue0;
    
    public static HairShaderParameters From(CustomizeParameters parameters)
    {
        return new HairShaderParameters
        {
            MainColor = parameters.MainColor,
            MeshColor = parameters.MeshColor,
            OptionColor = parameters.OptionColor,
            HairFresnelValue0 = parameters.HairFresnelValue0
        };
    }
}

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
