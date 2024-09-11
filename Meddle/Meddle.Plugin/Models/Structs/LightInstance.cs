using System.Numerics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using SharpGLTF.Scenes;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x50)]
public unsafe struct LightLayoutInstance
{
    [FieldOffset(0x08)] public LayerManager* LayerManager;
    [FieldOffset(0x10)] public LayoutManager* LayoutManager;
    [FieldOffset(0x30)] public Light* LightPtr;
}

[StructLayout(LayoutKind.Explicit, Size = 0x98)]
public unsafe struct Light
{
    [FieldOffset(0x00)] public DrawObject DrawObject;
    [FieldOffset(0x90)] public RenderLight* LightItem;
}

[StructLayout(LayoutKind.Explicit, Size = sizeof(float) * 10)]
public struct LightBounds
{
    // hard limits, relative to the light's position
    [FieldOffset(0x00)] public float _pad0;
    [FieldOffset(0x04)] public float _pad1;
    [FieldOffset(0x08)] public float West;
    [FieldOffset(0x0C)] public float Down;
    [FieldOffset(0x10)] public float North;
    [FieldOffset(0x14)] public float _pad2;
    [FieldOffset(0x18)] public float East;
    [FieldOffset(0x1C)] public float Up;
    [FieldOffset(0x20)] public float South;
    [FieldOffset(0x24)] public float _pad3;
    
    public Vector3 Min => new Vector3(West, Down, North);
    public Vector3 Max => new Vector3(East, Up, South);
}

// https://github.com/ktisis-tools/Ktisis/blob/57391bf9eeb432b296d6ea22956df7868a37d069/Ktisis/Structs/Lights/RenderLight.cs
[Flags]
public enum LightFlags : uint {
    Reflection = 0x01,
    Dynamic = 0x02,
    CharaShadow = 0x04,
    ObjectShadow = 0x08
}

public enum LightType : uint {
    Directional = 1,
    PointLight = 2,
    SpotLight = 3,
    AreaLight = 4,
    CapsuleLight = 5
}

public enum FalloffType : uint {
    Linear = 0,
    Quadratic = 1,
    Cubic = 2
}

[StructLayout(LayoutKind.Explicit, Size = 0xA0)]
public struct RenderLight {
    [FieldOffset(0x18)] public LightFlags Flags;
    [FieldOffset(0x1C)] public LightType LightType;
    [FieldOffset(0x20)] public unsafe Transform* Transform;
    [FieldOffset(0x28)] public ColorHdr Color;
    [FieldOffset(0x38)] public LightBounds Bounds;
    [FieldOffset(0x60)] public float ShadowNear;
    [FieldOffset(0x64)] public float ShadowFar;
    [FieldOffset(0x68)] public FalloffType FalloffType;
    [FieldOffset(0x70)] public Vector2 AreaAngle;
    //[FieldOffset(0x78)] public float _unk0;
    [FieldOffset(0x80)] public float Falloff;
    [FieldOffset(0x84)] public float LightAngle;   // 0-90deg
    [FieldOffset(0x88)] public float FalloffAngle; // 0-90deg
    [FieldOffset(0x8C)] public float Range;
    [FieldOffset(0x90)] public float CharaShadowRange;
}

[StructLayout(LayoutKind.Explicit, Size = sizeof(float) * 4)]
public struct ColorHdr {
    [FieldOffset(0x00)] public Vector3 _vec3;
	
    [FieldOffset(0x00)] public float Red;
    [FieldOffset(0x04)] public float Green;
    [FieldOffset(0x08)] public float Blue;
    [FieldOffset(0x0C)] public float Intensity;

    public Vector3 Rgb => HdrToRgb(_vec3);

    public float HdrIntensity => Intensity * _vec3.Length();
    
    private static Vector3 HdrToRgb(Vector3 hdr) {
        var len = hdr.Length();
        return hdr / (1.0f + len);
    }
}
