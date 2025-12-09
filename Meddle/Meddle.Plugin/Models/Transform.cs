using System.Numerics;
using System.Text.Json.Serialization;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using FFXIVClientStructs.Havok.Common.Base.Math.Quaternion;
using FFXIVClientStructs.Havok.Common.Base.Math.Vector;
using Microsoft.Extensions.Logging;
using SharpGLTF.Transforms;
using CSTransform = FFXIVClientStructs.FFXIV.Client.Graphics.Transform;

namespace Meddle.Plugin.Models;

public readonly record struct Transform
{
    public Transform(FFXIVClientStructs.FFXIV.Client.LayoutEngine.Transform transform)
    {
        Translation = transform.Translation;
        Rotation = transform.Rotation;
        Scale = transform.Scale;
    }
    
    public Transform(hkQsTransformf hkTransform)
    {
        Translation = AsVector(hkTransform.Translation);
        Rotation = AsQuaternion(hkTransform.Rotation);
        Scale = AsVector(hkTransform.Scale);
    }
    
    public Transform(Vector3 translation, Quaternion rotation, Vector3 scale)
    {
        Translation = translation;
        Rotation = rotation;
        Scale = scale;
    }

    public Transform(CSTransform transform)
    {
        Translation = transform.Position;
        Rotation = transform.Rotation;
        Scale = transform.Scale;
    }

    public Transform(AffineTransform transform)
    {
        transform = transform.GetDecomposed();
        Translation = transform.Translation;
        Rotation = transform.Rotation;
        Scale = transform.Scale;
    }

    public Transform(Matrix4x4 transform) : this(new AffineTransform(transform)) { }

    [JsonIgnore]
    public Vector3 Translation { get; init; }

    [JsonIgnore]
    public Quaternion Rotation { get; init; }

    [JsonIgnore]
    public Vector3 Scale { get; init; }

    public AffineTransform AffineTransform => GetAffine();

    private bool IsFinite(Vector3 value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
    }
    
    private bool IsFinite(Quaternion value)
    {
        return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);
    }

    private bool IsFinite(float value)
    {
        return !(float.IsNaN(value) || float.IsInfinity(value));
    }
    
    private bool HasZeroOrNearZeroScale(Vector3 scale)
    {
        const float epsilon = 1e-6f;
        return Math.Abs(scale.X) < epsilon || Math.Abs(scale.Y) < epsilon || Math.Abs(scale.Z) < epsilon;
    }
    
    private AffineTransform GetAffine()
    {
        try
        {
            Vector3 scale;
            if (!IsFinite(Scale))
            {
                Plugin.Logger.LogWarning("Transform contains non-finite scale, using default: {Scale} -> {DefaultValue}", Scale, Vector3.One);
                scale = Vector3.One;
            }
            else if (HasZeroOrNearZeroScale(Scale))
            {
                Plugin.Logger.LogWarning("Transform contains zero or near-zero scale, using default: {Scale} -> {DefaultValue}", Scale, Vector3.One);
                scale = Vector3.One;
            }
            else
            {
                scale = Scale;
            }
            
            Vector3 translation;
            if (!IsFinite(Translation))
            {
                Plugin.Logger.LogWarning("Transform contains non-finite translation, using default: {Translation} -> {DefaultValue}", Translation, Vector3.Zero);
                translation = Vector3.Zero;
            }
            else
            {
                translation = Translation;
            }
            
            Quaternion rotation;
            if (!IsFinite(Rotation))
            {
                Plugin.Logger.LogWarning("Transform contains non-finite rotation, using default: {Rotation} -> {DefaultValue}", Rotation, Quaternion.Identity);
                rotation = Quaternion.Identity;
            }
            else
            {
                rotation = Rotation;
            }
            
            return new AffineTransform(scale, rotation, translation);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError(ex, "Failed to create AffineTransform from Transform, pos: {Position}, rot: {Rotation}, scale: {Scale}",
                Translation, Rotation, Scale);
            throw new InvalidOperationException("Failed to create AffineTransform from Transform", ex);
        }
    }

    public override string ToString()
    {
        return $"{Translation:0.00} {new EulerAngles(Rotation).Angles:0.00} {Scale:0.00}";
    }

    private static Vector3 AsVector(hkVector4f hkVector)
    {
        return new Vector3(hkVector.X, hkVector.Y, hkVector.Z);
    }

    // private static Vector4 AsVector(hkQuaternionf hkVector)
    // {
    //     return new Vector4(hkVector.X, hkVector.Y, hkVector.Z, hkVector.W);
    // }

    private static Quaternion AsQuaternion(hkQuaternionf hkQuaternion)
    {
        return new Quaternion(hkQuaternion.X, hkQuaternion.Y, hkQuaternion.Z, hkQuaternion.W);
    }
}

public readonly record struct EulerAngles
{
    // https://stackoverflow.com/a/70462919
    public EulerAngles(Quaternion q)
    {
        Vector3 angles = new();

        // roll / x
        double sinrCosp = 2 * ((q.W * q.X) + (q.Y * q.Z));
        double cosrCosp = 1 - (2 * ((q.X * q.X) + (q.Y * q.Y)));
        angles.X = (float)Math.Atan2(sinrCosp, cosrCosp);

        // pitch / y
        double sinp = 2 * ((q.W * q.Y) - (q.Z * q.X));
        if (Math.Abs(sinp) >= 1)
        {
            angles.Y = (float)Math.CopySign(Math.PI / 2, sinp);
        }
        else
        {
            angles.Y = (float)Math.Asin(sinp);
        }

        // yaw / z
        double sinyCosp = 2 * ((q.W * q.Z) + (q.X * q.Y));
        double cosyCosp = 1 - (2 * ((q.Y * q.Y) + (q.Z * q.Z)));
        angles.Z = (float)Math.Atan2(sinyCosp, cosyCosp);

        Angles = angles * 180 / MathF.PI;
    }

    public Vector3 Angles { get; init; }

    public static implicit operator Vector3(EulerAngles e)
    {
        return e.Angles;
    }

    public override string ToString()
    {
        return Angles.ToString();
    }

    public readonly string ToString(string? format)
    {
        return Angles.ToString(format);
    }

    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        return Angles.ToString(format, formatProvider);
    }
}
