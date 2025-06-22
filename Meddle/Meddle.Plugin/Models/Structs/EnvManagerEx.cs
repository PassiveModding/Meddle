using System.Numerics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0x910)]
public struct EnvManagerEx {
    [FieldOffset(0x000)] public EnvManager _base;
	
    [FieldOffset(0x058)] public EnvState EnvState;
    
    public static unsafe EnvManagerEx* Instance() => (EnvManagerEx*)EnvManager.Instance();
}

// https://github.com/ktisis-tools/Ktisis/blob/21f82d8f7b5f08446d18e811d16d1f3a5a2593d6/Ktisis/Structs/Env/EnvState.cs
[StructLayout(LayoutKind.Explicit, Size = 0x2F8)]
public struct EnvState {
    [FieldOffset(0x020)] public EnvLighting Lighting;
}

[StructLayout(LayoutKind.Explicit, Size = 0x40)]
public struct EnvLighting {
    [FieldOffset(0x00)] public ColorRgbHdr SunLightColor;
    [FieldOffset(0x0C)] public ColorRgbHdr MoonLightColor;
    [FieldOffset(0x18)] public ColorRgbHdr Ambient;
    [FieldOffset(0x24)] public float _unk1;
    [FieldOffset(0x28)] public float AmbientSaturation;
    [FieldOffset(0x2C)] public float Temperature;
    [FieldOffset(0x30)] public float _unk2;
    [FieldOffset(0x34)] public float _unk3;
    [FieldOffset(0x38)] public float _unk4;
}

[StructLayout(LayoutKind.Explicit, Size = sizeof(float) * 3)]
public struct ColorRgbHdr {
    [FieldOffset(0x00)] public Vector3 _vec3;
	
    [FieldOffset(0x00)] public float Red;
    [FieldOffset(0x04)] public float Green;
    [FieldOffset(0x08)] public float Blue;

    public Vector3 Rgb => HdrToRgb(_vec3);
    
    public float HdrIntensity => _vec3.Length();
    
    private static Vector3 HdrToRgb(Vector3 hdr)
    {
        return Vector3.Clamp(new Vector3(
                                 Reinhard(hdr.X),
                                 Reinhard(hdr.Y),
                                 Reinhard(hdr.Z)
                             ), Vector3.Zero, Vector3.One);
        float Reinhard(float x) => x / (1.0f + x);
    }
}
