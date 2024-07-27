using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Meddle.Utils.Files;
using OtterTex;

namespace Meddle.Utils.Export;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum TextureUsage : uint
{
    g_SamplerNormal = 0x0C5EC1F1,
    g_SamplerMask = 0x8A4E82B6,
    g_SamplerIndex = 0x565F8FD8,
    g_SamplerDiffuse = 0x115306BE,
    g_SamplerFlow = 0xA7E197F6,
    g_SamplerWaveMap = 0xE6321AFC,
    g_SamplerWaveMap1 = 0xE5338C17,
    g_SamplerWhitecapMap = 0x95E1F64D,
    g_SamplerWaveletMap0 = 0x574E22D6,
    g_SamplerWaveletMap1 = 0x20491240,
    g_SamplerColorMap0 = 0x1E6FEF9C,
    g_SamplerNormalMap0 = 0xAAB4D9E9,
    g_SamplerSpecularMap0 = 0x1BBC2F12,
    g_SamplerColorMap1 = 0x6968DF0A,
    g_SamplerNormalMap1 = 0xDDB3E97F,
    g_SamplerSpecularMap1 = 0x6CBB1F84,
    g_SamplerSpecular = 0x2B99E025,
    g_SamplerColorMap = 0x6E1DF4A2,
    g_SamplerNormalMap = 0xBE95B65E,
    g_SamplerSpecularMap = 0xBD8A6965,
    g_SamplerEnvMap = 0xF8D7957A,
    g_SamplerSphareMapCustum = 0xD7837FCE,
    g_Sampler0 = 0x213CB439,
    g_Sampler1 = 0x563B84AF,
    g_SamplerCatchlight = 0xFEA0F3D2,
    g_Sampler = 0x88408C04,
    g_SamplerGradationMap = 0x5F726C11,
    g_SamplerNormal2 = 0x0261CDCB,
    g_SamplerWrinklesMask = 0xB3F13975,
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
    
    public Texture(TextureResource resource, string path, uint? samplerFlags, uint? id, ShpkFile? shpkFile)
    {
        Resource = resource;
        HandlePath = path;
        SamplerFlags = samplerFlags;
        Id = id;
        Meta = ImageUtils.GetTexMeta(resource);
        
        if (shpkFile != null)
        {
            var shaderPackage = new ShaderPackage(shpkFile, null!);
            if (Id.HasValue && shaderPackage.TextureLookup.TryGetValue(Id.Value, out var usage))
                Usage = usage;
        }
    }
    
    
    public static TextureResource GetResource(TexFile file)
    {
        var h = file.Header;
        D3DResourceMiscFlags flags = 0;
        if (h.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            flags |= D3DResourceMiscFlags.TextureCube;
        return new TextureResource(
            TexFile.GetDxgiFormatFromTextureFormat(h.Format), 
               h.Width, 
               h.Height, 
               h.CalculatedMips, 
               h.CalculatedArraySize, 
               TexFile.GetTexDimensionFromAttribute(h.Type), 
               flags, 
               file.TextureBuffer);
    }
}
