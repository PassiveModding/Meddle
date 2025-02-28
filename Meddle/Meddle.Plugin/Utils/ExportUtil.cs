using System.Numerics;
using Meddle.Plugin.Models;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using SkiaSharp;

namespace Meddle.Plugin.Utils;

public static class ExportUtil
{
    private static readonly WriteSettings WriteSettings = new WriteSettings
    {
        Validation = ValidationMode.TryFix,
        JsonIndented = false,
    };
    
    public static void SaveAsType(this ModelRoot? gltf, ExportType typeFlags, string path, string name)
    {
        if (typeFlags.HasFlag(ExportType.GLTF))
        {
            gltf?.SaveGLTF(Path.Combine(path, name + ".gltf"), WriteSettings);
        }
        
        if (typeFlags.HasFlag(ExportType.GLB))
        {
            gltf?.SaveGLB(Path.Combine(path, name + ".glb"), WriteSettings);
        }
        
        if (typeFlags.HasFlag(ExportType.OBJ))
        {
            gltf?.SaveAsWavefront(Path.Combine(path, name + ".obj"));
        }
    }
    
    public static Vector4 ToVector4(this SKColor color) => new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, color.Alpha / 255f);
    public static SKColor ToSkColor(this Vector4 color)
    {
        var c = color.Clamp(0, 1);
        return new SKColor((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), (byte)(c.W * 255));
    }
    public static Vector4 Clamp(this Vector4 v, float min, float max)
    {
        return new Vector4(
            Math.Clamp(v.X, min, max),
            Math.Clamp(v.Y, min, max),
            Math.Clamp(v.Z, min, max),
            Math.Clamp(v.W, min, max)
        );
    }
    public static float[] AsFloatArray(this Vector4 v) => new[] { v.X, v.Y, v.Z, v.W };
    public static float[] AsFloatArray(this Vector3 v) => new[] { v.X, v.Y, v.Z };
    public static float[] AsFloatArray(this Vector2 v) => new[] { v.X, v.Y };
}
