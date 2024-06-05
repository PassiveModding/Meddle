using System.Text.Json.Serialization;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Models;
using CSTextureEntry = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material.TextureEntry;

namespace Meddle.Utils.Export;

/**
 * These values are actually CRC values used by SE in order to
 * coordinate mappings to shaders. Textures do not actually store
 * whether they are diffuse, specular, etc. They store the shader
 * that this texture is input for, in CRC form.
 *
 * That was my long way of explaining "these are linked manually."
 */
public enum TextureUsage : uint
{
    Sampler = 0x88408C04,
    Sampler0 = 0x213CB439,
    Sampler1 = 0x563B84AF,
    SamplerCatchlight = 0xFEA0F3D2,
    SamplerColorMap0 = 0x1E6FEF9C,
    SamplerColorMap1 = 0x6968DF0A,
    SamplerDiffuse = 0x115306BE,
    SamplerEnvMap = 0xF8D7957A,
    SamplerMask = 0x8A4E82B6,
    SamplerNormal = 0x0C5EC1F1,
    SamplerNormalMap0 = 0xAAB4D9E9,
    SamplerNormalMap1 = 0xDDB3E97F,
    SamplerReflection = 0x87F6474D,
    SamplerSpecular = 0x2B99E025,
    SamplerSpecularMap0 = 0x1BBC2F12,
    SamplerSpecularMap1 = 0x6CBB1F84,
    SamplerWaveMap = 0xE6321AFC,
    SamplerWaveletMap0 = 0x574E22D6,
    SamplerWaveletMap1 = 0x20491240,
    SamplerWhitecapMap = 0x95E1F64D
}

public unsafe class Texture
{
    public string HandlePath { get; }
    public TextureUsage? Usage { get; }
    public uint? Id { get; }
    public uint? SamplerFlags { get; }
    
    //[JsonIgnore]
    //public TextureHelper.TextureResource Resource { get; }
    
    public Texture(Pointer<CSTextureEntry> matEntry, Pointer<byte> matHndStrings, 
                   Pointer<MaterialResourceHandle.TextureEntry> hndEntry, ShaderPackage shader) : 
        this(matEntry.Value, matHndStrings.Value, hndEntry.Value, shader)
    {

    }

    public Texture(CSTextureEntry* matEntry, byte* matHndStrings, MaterialResourceHandle.TextureEntry* hndEntry, ShaderPackage shader)
    {
        //HandlePath = MemoryHelper.ReadStringNullTerminated((nint)matHndStrings + hndEntry->PathOffset);

        if (matEntry != null)
        {
            Id = matEntry->Id;
            SamplerFlags = matEntry->SamplerFlags;
            if (shader.TextureLookup.TryGetValue(Id.Value, out var usage))
                Usage = usage;
        }

        //Resource = DXHelper.ExportTextureResource(hndEntry->TextureResourceHandle->Texture);
    }
}
