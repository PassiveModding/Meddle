using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;

namespace Meddle.Plugin.Models.Skeletons;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct ShaderPackageKeys
{
    [FieldOffset(0x8C)] public ushort SceneKeyCount;
    [FieldOffset(0x90)] public ushort MaterialKeyCount;
    
    [FieldOffset(0xC8)] public uint* SceneKeys;
    [FieldOffset(0xD8)] public uint* MaterialKeys;
    [FieldOffset(0xE0)] public uint* SceneValues;
    [FieldOffset(0xF0)] public uint* MaterialValues;
    [FieldOffset(0xF8)] public uint SubviewValue1;
    [FieldOffset(0xFC)] public uint SubviewValue2;
    
    public Span<uint> SceneKeysSpan => new Span<uint>(SceneKeys, SceneKeyCount);
    public Span<uint> MaterialKeysSpan => new Span<uint>(MaterialKeys, MaterialKeyCount);
    public Span<uint> SceneValuesSpan => new Span<uint>(SceneValues, SceneKeyCount);
    public Span<uint> MaterialValuesSpan => new Span<uint>(MaterialValues, MaterialKeyCount);
    
    public static ShaderPackageKeys* FromShaderPackage(ShaderPackage* shaderPackage)
    {
        return (ShaderPackageKeys*)shaderPackage;
    }
}
