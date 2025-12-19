using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.Utils;

public static class ObjectUtil
{
    public static AffineTransform ToAffine(this FFXIVClientStructs.FFXIV.Client.LayoutEngine.Transform transform)
    {
        return new AffineTransform(transform.Scale, transform.Rotation, transform.Translation);
    }
    
    public static string ToFormatted(this Vector3 vector)
    {
        return $"<X: {vector.X:0.00} Y: {vector.Y:0.00} Z: {vector.Z:0.00}>";
    }

    public static string ToFormatted(this Quaternion vector)
    {
        return $"<X: {vector.X:0.00} Y: {vector.Y:0.00} Z: {vector.Z:0.00} W: {vector.W:0.00}>";
    }
}
