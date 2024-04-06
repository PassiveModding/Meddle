using System.Numerics;
using FFXIVClientStructs.FFXIV.Shader;

namespace Meddle.Plugin.Models;

public class SkinShaderParameters
{
    public Vector4 SkinColor;
    public Vector3 MainColor;
    public Vector3 MeshColor;
    public Vector4 LipColor;
    public bool ApplyLipColor;
    public bool IsHrothgar;
        
    public static SkinShaderParameters? From(CustomizeParameters? parameters)
    {
        if (parameters == null)
            return null;

        return new SkinShaderParameters()
        {
            IsHrothgar = parameters.IsHrothgar,
            SkinColor = parameters.SkinColor,
            MainColor = parameters.MainColor,
            MeshColor = parameters.MeshColor,
            LipColor = parameters.LipColor
        };
    }
}

public class HairShaderParameters
{
    public Vector3 MainColor;
    public Vector3 MeshColor;
    public Vector3 OptionColor;
    
    public static HairShaderParameters? From(CustomizeParameters? parameters)
    {
        if (parameters == null)
            return null;

        return new HairShaderParameters()
        {
            MainColor = parameters.MainColor,
            MeshColor = parameters.MeshColor,
            OptionColor = parameters.OptionColor
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
    
    public CustomizeParameters(){}

    public CustomizeParameters Default()
    {
        return new CustomizeParameters()
        {
            SkinColor = new Vector4(1, 0.8745098f, 0.8627451f, 1),
            SkinFresnelValue0 = new Vector4(0.25f, 0.25f, 0.25f, 32),
            LipColor = new Vector4(0.47058824f, 0.27058825f, 0.40784314f, 0.6f),
            MainColor = new Vector3(0.89411765f, 0.89411765f, 0.89411765f),
            HairFresnelValue0 = new Vector3(0.8627451f, 0.8627451f, 0.8627451f),
            MeshColor = new Vector3(0.8666667f, 0.6745098f, 0.64705884f),
            LeftColor = new Vector4(0.80784315f, 0.49803922f, 0.5176471f, -1),
            RightColor = new Vector4(0.80784315f, 0.49803922f, 0.5176471f, 1),
            OptionColor = new Vector3(0.7764706f, 0.7764706f, 0.7764706f),
            IsHrothgar = false
        };
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
