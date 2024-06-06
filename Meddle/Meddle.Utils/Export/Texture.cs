using System.Text.Json.Serialization;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Meddle.Utils.Files;
using OtterTex;
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
    
    [JsonIgnore]
    public TextureResource Resource { get; }

    public TexMeta Meta { get; }

    public Texture(TexFile file, string path, uint? samplerFlags, uint? id)
    {
        SamplerFlags = samplerFlags;
        Id = id;
        HandlePath = path;
        var h = file.Header;
        var dimension = h.Type switch
        {
            TexFile.Attribute.TextureType1D => TexDimension.Tex1D,
            TexFile.Attribute.TextureType2D => TexDimension.Tex2D,
            TexFile.Attribute.TextureType3D => TexDimension.Tex3D,
            _ => TexDimension.Tex2D
        };
        D3DResourceMiscFlags flags = 0;
        if (h.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            flags |= D3DResourceMiscFlags.TextureCube;
        Resource = new TextureResource(h.Format.ToDXGIFormat(), h.Width, h.Height, h.MipLevels, h.ArraySize, dimension, flags, file.TextureBuffer);
        Meta = ImageUtils.GetTexMeta(file);
        
        if (path.Contains("_d")) Usage = TextureUsage.SamplerDiffuse;
        else if (path.Contains("_n")) Usage = TextureUsage.SamplerNormal;
        else if (path.Contains("_s")) Usage = TextureUsage.SamplerSpecular;
        else if (path.Contains("_m")) Usage = TextureUsage.SamplerMask;
        else
        {
            Console.WriteLine($"Unknown texture usage for {path}");
        }
    }
}
