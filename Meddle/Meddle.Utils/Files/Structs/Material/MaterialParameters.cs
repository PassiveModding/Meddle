using System.Numerics;

namespace Meddle.Utils.Files.Structs.Material;

public struct MaterialParameters
{
    public static readonly IReadOnlyList<string> ValidShaders = new []
    {
        "hair.shpk",
        "iris.shpk",
        "skin.shpk",
        "character.shpk",
        "characterglass.shpk",
    };
    
    public MaterialParameters(ReadOnlySpan<Vector4> m)
    {
        if (m.Length != 6) return;

        DiffuseColor = Normalize(new Vector3(m[0].X, m[0].Y, m[0].Z));
        AlphaThreshold = m[0].W == 0 ? 0.5f : m[0].W;
        FresnelValue0 = Normalize(new Vector3(m[1].X, m[1].Y, m[1].Z));
        SpecularMask = m[1].W;
        LipFresnelValue0 = Normalize(new Vector3(m[2].X, m[2].Y, m[2].Z));
        Shininess = m[2].W / 255f;
        EmissiveColor = Normalize(new Vector3(m[3].X, m[3].Y, m[3].Z));
        LipShininess = m[3].W / 255f;
        TileScale = new Vector2(m[4].X, m[4].Y);
        AmbientOcclusionMask = m[4].Z;
        TileIndex = m[4].W;
        ScatteringLevel = m[5].X;
        UNK_15B70E35 = m[5].Y;
        NormalScale = m[5].Z;
    }
    
    public Vector3 DiffuseColor;
    public float AlphaThreshold;
    public Vector3 FresnelValue0;
    public float SpecularMask;
    public Vector3 LipFresnelValue0;
    public float Shininess;
    public Vector3 EmissiveColor;
    public float LipShininess;
    public Vector2 TileScale;
    public float AmbientOcclusionMask;
    public float TileIndex;
    public float ScatteringLevel;
    public float UNK_15B70E35;
    public float NormalScale;
    
    private static Vector3 Normalize(Vector3 v)
    {
        var len = v.Length();
        if (len == 0)
            return Vector3.Zero;
        return v / len;
    }
}
