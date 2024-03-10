using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;

namespace Meddle.Plugin.Models;


public struct XivVertex : IVertexCustom
{
    public Vector4 FfxivColor;
    private const string FfxivColorName = "_FFXIV_COLOR";
    public Vector2? TexCoord0;
    private const string TexCoord0Name = "TEXCOORD_0";
    public Vector2? TexCoord1;
    private const string TexCoord1Name = "TEXCOORD_1";
    public int MaxColors => 1;
    public int MaxTextCoords { get; }

    IEnumerable<KeyValuePair<string, AttributeFormat>> IVertexReflection.GetEncodingAttributes()
    {
        yield return new KeyValuePair<string, AttributeFormat>(FfxivColorName, new AttributeFormat(DimensionType.VEC4, EncodingType.UNSIGNED_SHORT, false));

        if (MaxTextCoords > 0)
        {
            yield return new KeyValuePair<string, AttributeFormat>(TexCoord0Name, new AttributeFormat(DimensionType.VEC2, EncodingType.FLOAT));
        }
        
        if (MaxTextCoords > 1)
        {
            yield return new KeyValuePair<string, AttributeFormat>(TexCoord1Name, new AttributeFormat(DimensionType.VEC2, EncodingType.FLOAT));
        }
    }
    
    public XivVertex(Vector4 ffxivColor, Vector2? texCoord0 = null, Vector2? texCoord1 = null)
    {
        FfxivColor = ffxivColor;
        MaxTextCoords = 0;
        if (texCoord0 != null) MaxTextCoords++;
        if (texCoord1 != null) MaxTextCoords++;
        TexCoord0 = texCoord0;
        TexCoord1 = texCoord1;
    }
    
    public Vector4 GetColor(int index)
    {
        if (index == 0) return FfxivColor;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    public Vector2 GetTexCoord(int index)
    {
        return index switch
        {
            0 => TexCoord0 ?? Vector2.Zero,
            1 => TexCoord1 ?? Vector2.Zero,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    public void SetColor(int setIndex, Vector4 color)
    {
        if (setIndex == 0) FfxivColor = color;
        if (setIndex >= 1) throw new ArgumentOutOfRangeException(nameof(setIndex));
    }

    public void SetTexCoord(int setIndex, Vector2 coord)
    {
        if (MaxTextCoords < setIndex) throw new ArgumentOutOfRangeException(nameof(setIndex));
        
        if (setIndex == 0) TexCoord0 = coord;
        if (setIndex == 1) TexCoord1 = coord;
        if (setIndex >= 2) throw new ArgumentOutOfRangeException(nameof(setIndex));
    }

    public VertexMaterialDelta Subtract(IVertexMaterial baseValue)
    {
        return new VertexMaterialDelta(FfxivColor - baseValue.GetColor(0), Vector4.Zero, Vector2.Zero, Vector2.Zero);
    }

    public void Add(in VertexMaterialDelta delta)
    {
        FfxivColor += delta.Color0Delta;
    }

    public void Validate()
    {
        var components = new[] { FfxivColor.X, FfxivColor.Y, FfxivColor.Z, FfxivColor.W };
        if (components.Any(component => component < 0 || component > 1))
            throw new ArgumentOutOfRangeException(nameof(FfxivColor));
    }

    public bool TryGetCustomAttribute(string attributeName, [UnscopedRef] out object value)
    {
        switch (attributeName)
        {
            case FfxivColorName:
            {
                value = FfxivColor;
                return true;
            }
            case TexCoord0Name:
            {
                value = TexCoord0.Value;
                return true;
            }
            case TexCoord1Name:
            {
                value = TexCoord1.Value;
                return true;
            }
            default:
            {
                value = null;
                return false;
            }
        }
    }

    public void SetCustomAttribute(string attributeName, object value)
    {
        switch (attributeName)
        {
            case FfxivColorName:
            {
                FfxivColor = (Vector4) value;
                break;
            }
            case TexCoord0Name:
            {
                TexCoord0 = (Vector2) value;
                break;
            }
            case TexCoord1Name:
            {
                TexCoord1 = (Vector2) value;
                break;
            }
        }
    }

    public IEnumerable<string> CustomAttributes
    {
        get
        {
            if (FfxivColor != Vector4.Zero) yield return FfxivColorName;
            if (TexCoord0 != null) yield return TexCoord0Name;
            if (TexCoord1 != null) yield return TexCoord1Name;
        }
    }
}