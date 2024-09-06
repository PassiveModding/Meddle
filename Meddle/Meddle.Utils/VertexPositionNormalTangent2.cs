using System.Numerics;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;

namespace Meddle.Utils;

// --------- CHARACTER ---------
// VertexColor1 when VertexColorMode is MASK
// R = Specular Mask
// G = Roughness
// B = Diffuse Mask
// A = Opacity
// VertexColor1 when VertexColorMode is COLOR
// RGBA = Color
// other modes unknown

// VertexColor2
// R = Faux-Wind influence
// G = Faux-Wind Multplier
// BA = unknown

// UV1
// UV

// UV2
// OFF = No Decals
// COLOR = Color Decal Placement
// ALPHA = Alpha Decal Placement

// --------- CHARACTERLEGACY ---------
// VertexColor1 when VertexColorMode is MASK
// R = Specular Mask
// G = Roughness
// B = Diffuse Mask
// A = Opacity
// VertexColor1 when VertexColorMode is COLOR
// RGBA = Color
// other modes unknown

// UV1
// UV

// UV2 (FC Crests etc.)
// OFF = No Decals
// COLOR = Color Decal Placement
// ALPHA = Alpha Decal Placement

// --------- SKIN ---------
// VertexColor1 when VertexColorMode is MASK
// R = Muscle slider influence
// G = Unused
// B = ??
// A = Shadow casting on/off
// VertexColor1 when VertexColorMode is COLOR
// RGBA = Color
// other modes unknown

// VertexColor2
// R = Faux-Wind influence
// G = Faux-Wind Multplier
// BA = unknown

// UV1
// UV

// UV2 (Legacy Mark)
// OFF = No Decals
// COLOR = Color Decal Placement
// ALPHA = Alpha Decal Placement

// --------- HAIR ---------
// VertexColor1
// RGB = unknown
// A = Shadow casting on/off

// VertexColor2
// R = Faux-Wind influence
// G = Faux-Wind Multplier
// BA = unknown

// VertexColor3
// R = Tangent Space Anisotropic Flow U
// G = Tangent Space Anisotropic Flow V
// BA = unknown

// UV1
// UV

// UV2
// Opacity mapping for miqote?

// --------- IRIS ---------
// VertexColor1
// R = Eye left/right selection
// G = Eye left/right selection
// BA = unknown

// UV1
// UV

// --------- CHARACTERTATTOO ---------
// UV1
// UV

// UV2
// OFF = No Decals
// COLOR = Color Decal Placement
// ALPHA = Alpha Decal Placement

// --------- CHARACTEROCCLUSION ---------
// VertexColor1
// R = Standard Tangent Space Normal Map
// G = Standard Tangent Space Normal Map
// B = unknown
// A = unused? maybe mask/color stuff

// UV1
// UV

// UV2
// OFF = No Decals
// COLOR = Color Decal Placement
// ALPHA = Alpha Decal Placement

// --------- CHARACTERGLASS ---------
// VertexColor1
// R = Specular Mask
// G = Roughness
// B = Diffuse Mask
// A = Opacity

// VertexColor2
// R = Faux-Wind influence
// G = Faux-Wind Multplier
// BA = unknown

// UV1
// UV

// UV2
// OFF = No Decals
// COLOR = Color Decal Placement
// ALPHA = Alpha Decal Placement

// --------- BG/BGColorChange ---------
// VertexColor1 when BGVertexPaint is set and value is also set
// RGBA = Color
// VertexColor1 otherwise
// A = Map0/Map1 blend

// UV1
// UV


/*public struct VertexPositionNormalTangent2 : IVertexGeometry, IEquatable<VertexPositionNormalTangent2>
{
    public VertexPositionNormalTangent2(in Vector3 p, in Vector3 n, in Vector4 t, in Vector4 t2)
    {
        this.Position = p;
        this.Normal = n;
        this.Tangent = t;
        this.Tangent2 = t2;
    }

    public static implicit operator VertexPositionNormalTangent2(in (Vector3 Pos, Vector3 Nrm, Vector4 Tgt, Vector4 Tgt2) tuple)
    {
        return new VertexPositionNormalTangent2(tuple.Pos, tuple.Nrm, tuple.Tgt, tuple.Tgt2);
    }

    #region data
    
    public Vector3 Position;        
    public Vector3 Normal;
    public Vector4 Tangent;
    public Vector4 Tangent2;

    IEnumerable<KeyValuePair<string, AttributeFormat>> IVertexReflection.GetEncodingAttributes()
    {
        yield return new KeyValuePair<string, AttributeFormat>("POSITION", new AttributeFormat(DimensionType.VEC3));
        yield return new KeyValuePair<string, AttributeFormat>("NORMAL", new AttributeFormat(DimensionType.VEC3));
        yield return new KeyValuePair<string, AttributeFormat>("TANGENT", new AttributeFormat(DimensionType.VEC4));
        yield return new KeyValuePair<string, AttributeFormat>("TANGENT2", new AttributeFormat(DimensionType.VEC4));
    }

    public override readonly int GetHashCode() { return Position.GetHashCode(); }

    /// <inheritdoc/>
    public override readonly bool Equals(object obj) { return obj is VertexPositionNormalTangent2 other && AreEqual(this, other); }

    /// <inheritdoc/>
    public readonly bool Equals(VertexPositionNormalTangent2 other) { return AreEqual(this, other); }
    public static bool operator ==(in VertexPositionNormalTangent2 a, in VertexPositionNormalTangent2 b) { return AreEqual(a, b); }
    public static bool operator !=(in VertexPositionNormalTangent2 a, in VertexPositionNormalTangent2 b) { return !AreEqual(a, b); }
    public static bool AreEqual(in VertexPositionNormalTangent2 a, in VertexPositionNormalTangent2 b)
    {
        return a.Position == b.Position && a.Normal == b.Normal && a.Tangent == b.Tangent && a.Tangent2 == b.Tangent2;
    }        

    #endregion

    #region API

    void IVertexGeometry.SetPosition(in Vector3 position) { this.Position = position; }

    void IVertexGeometry.SetNormal(in Vector3 normal) { this.Normal = normal; }

    void IVertexGeometry.SetTangent(in Vector4 tangent) { this.Tangent = tangent; }
    
    void SetTangent2(in Vector4 tangent2) { this.Tangent2 = tangent2; }

    /// <inheritdoc/>
    public readonly VertexGeometryDelta Subtract(IVertexGeometry baseValue)
    {
        var baseVertex = (VertexPositionNormalTangent2)baseValue;
        var tangentDelta = this.Tangent - baseVertex.Tangent;

        return new VertexGeometryDelta(
            this.Position - baseVertex.Position,
            this.Normal - baseVertex.Normal,
            new Vector3(tangentDelta.X, tangentDelta.Y, tangentDelta.Z));
    }

    public void Add(in VertexGeometryDelta delta)
    {
        this.Position += delta.PositionDelta;
        this.Normal += delta.NormalDelta;
        this.Tangent += new Vector4(delta.TangentDelta, 0);
    }

    public readonly Vector3 GetPosition() { return this.Position; }
    public readonly bool TryGetNormal(out Vector3 normal) { normal = this.Normal; return true; }
    public readonly bool TryGetTangent(out Vector4 tangent) { tangent = this.Tangent; return true; }
    public readonly bool TryGetTangent2(out Vector4 tangent2) { tangent2 = this.Tangent2; return true; }

    /// <inheritdoc/>
    public void ApplyTransform(in Matrix4x4 xform)
    {
        Position = Vector3.Transform(Position, xform);
        Normal = Vector3.Normalize(Vector3.TransformNormal(Normal, xform));

        var txyz = Vector3.Normalize(Vector3.TransformNormal(new Vector3(Tangent.X, Tangent.Y, Tangent.Z), xform));
        Tangent = new Vector4(txyz, Tangent.W);
        
        var t2xyz = Vector3.Normalize(Vector3.TransformNormal(new Vector3(Tangent2.X, Tangent2.Y, Tangent2.Z), xform));
        Tangent2 = new Vector4(t2xyz, Tangent2.W);
    }

    #endregion
}*/
