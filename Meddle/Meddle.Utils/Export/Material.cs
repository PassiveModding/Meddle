using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Models;

namespace Meddle.Utils.Export;

public enum ShaderCategory : uint
{
    CategorySkinType = 0x380CAED0,
    CategoryHairType = 0x24826489,
    CategoryTextureType = 0xB616DC5A,  // DEFAULT, COMPATIBILITY, SIMPLE
    CategorySpecularType = 0xC8BD1DEF, // MASK, DEFAULT
    CategoryFlowMapType = 0x40D1481E   // STANDARD, FLOW
}

public enum SkinType : uint
{
    Body = 0x2BDB45F1,
    Face = 0xF5673524,
    Hrothgar = 0x57FF3B64,
    Default = 0
}

public enum HairType : uint
{
    Face = 0x6E5B8F10,
    Hair = 0xF7B8956E
}

public enum FlowType : uint
{
    Standard = 0x337C6BC4, // No flow?
    Flow = 0x71ADA939
}

public enum TextureMode : uint
{
    Default = 0x5CC605B5,       // Default mask texture
    Compatibility = 0x600EF9DF, // Used to enable diffuse texture
    Simple = 0x22A4AABF         // meh
}

public enum SpecularMode : uint
{
    Mask = 0xA02F4828,   // Use mask sampler for specular
    Default = 0x198D11CD // Use spec sampler for specular
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum MaterialConstant : uint
{
    g_AlphaThreshold = 0x29AC0223,
    unk_NormalMapScale = 0x5351646E, // skin, might be used to adjust normal strength
    g_ShaderID = 0x59BDA0B1,
    g_DiffuseColor = 0x2C2A34DD,
    g_SpecularColor = 0x141722D5,
    g_SpecularColorMask = 0xCB0338DC,
    g_LipRoughnessScale = 0x3632401A,
    g_WhiteEyeColor = 0x11C90091,
    g_SphereMapIndex = 0x074953E9,
    g_EmissiveColor = 0x38A64362,
    g_SSAOMask = 0xB7FA33E2,
    g_TileIndex = 0x4255F2F4,
    g_TileScale = 0x2E60B071,
    g_TileAlpha = 0x12C6AC9F,
    g_NormalScale = 0xB5545FBB,
    g_SheenRate = 0x800EE35F,
    g_SheenTintRate = 0x1F264897,
    g_SheenAperture = 0xF490F76E,
    g_IrisRingColor = 0x50E36D56,
    g_IrisRingEmissiveIntensity = 0x7DABA471,
    g_IrisThickness = 0x66C93D3E,
    g_IrisOptionColorRate = 0x29253809,
    g_AlphaAperture = 0xD62BF368,
    g_AlphaOffset = 0xD07A6A65,
    g_GlassIOR = 0x7801E004,
    g_GlassThicknessMax = 0xC4647F37,
    g_TextureMipBias = 0x39551220,
    g_OutlineColor = 0x623CC4FE,
    g_OutlineWidth = 0x8870C938
}

public unsafe class Material
{
    public string HandlePath { get; }
    public uint ShaderFlags { get; }
    public IReadOnlyList<ShaderKey> ShaderKeys { get; }
    public record Constant(MaterialConstant Id, float[] Values);
    public Dictionary<MaterialConstant, float[]> MtrlConstants { get; }
    
    public string ShaderPackageName { get; }
    //public ShaderPackage ShaderPackage { get; }
    public IReadOnlyList<Texture> Textures { get; }
    //public MaterialParameters MaterialParameters { get; }
    
    public bool TryGetTexture(TextureUsage usage, out Texture texture)
    {
        var match = Textures.FirstOrDefault(x => x.Usage == usage);
        if (match == null)
        {
            texture = null!;
            return false;
        }

        texture = match;
        return true;
    }
    
    public Texture GetTexture(TextureUsage usage)
    {
        if (!TryGetTexture(usage, out var texture))
            throw new ArgumentException($"No texture for {usage}");
        return texture!;
    }
    
    [JsonIgnore]
    public ColorTable ColorTable { get; }

    public record MtrlGroup(string Path, MtrlFile MtrlFile, string ShpkPath, ShpkFile ShpkFile, Texture.TexGroup[] TexFiles);

    public Material(MtrlGroup mtrlGroup)
    {
        HandlePath = mtrlGroup.Path;
        ShaderFlags = mtrlGroup.MtrlFile.ShaderHeader.Flags;
        ShaderPackageName = mtrlGroup.MtrlFile.GetShaderPackageName();
        
        var shaderKeys = new ShaderKey[mtrlGroup.MtrlFile.ShaderKeys.Length];
        for (var i = 0; i < mtrlGroup.MtrlFile.ShaderKeys.Length; i++)
        {
            shaderKeys[i] = new ShaderKey
            {
                Category = mtrlGroup.MtrlFile.ShaderKeys[i].Category,
                Value = mtrlGroup.MtrlFile.ShaderKeys[i].Value
            };
        }
        
        ShaderKeys = shaderKeys;
        
        var textures = new List<Texture>();
        var texturePaths = mtrlGroup.MtrlFile.GetTexturePaths();
        for (int i = 0; i < mtrlGroup.MtrlFile.Samplers.Length; i++)
        {
            var sampler = mtrlGroup.MtrlFile.Samplers[i];
            var texture = mtrlGroup.MtrlFile.TextureOffsets[sampler.TextureIndex];
            var path = texturePaths[texture.Offset];
            var texFile = mtrlGroup.TexFiles.FirstOrDefault(x => x.Path == path)?.TexFile;
            if (texFile == null)
                throw new ArgumentException($"Texture {path} not found");
            var texObj = new Texture(texFile, path, sampler.Flags, sampler.SamplerId, mtrlGroup.ShpkFile);
            
            textures.Add(texObj);
        }
        
        Textures = textures;

        var constants = new List<Constant>();
        foreach (var constant in mtrlGroup.MtrlFile.Constants)
        {
            var index = constant.ValueOffset / 4;
            var count = constant.ValueSize / 4;
            var buf = new List<byte>(128);
            for (var j = 0; j < count; j++)
            {
                var value = mtrlGroup.MtrlFile.ShaderValues[index + j];
                var bytes = BitConverter.GetBytes(value);
                buf.AddRange(bytes);
            }

            var floats = MemoryMarshal.Cast<byte, float>(buf.ToArray());
            var values = new float[count];
            for (var j = 0; j < count; j++)
            {
                values[j] = floats[j];
            }
            
            constants.Add(new Constant((MaterialConstant)constant.ConstantId, values));
        }
        
        MtrlConstants = constants.ToDictionary(x => x.Id, x => x.Values);

        ColorTable = mtrlGroup.MtrlFile.ColorTable;
    }
    
    public float GetConstantOrDefault(MaterialConstant id, float @default)
    {
        return MtrlConstants.TryGetValue(id, out var values) ? values[0] : @default;
    }
    
    public Vector3 GetConstantOrDefault(MaterialConstant id, Vector3 @default)
    {
        return MtrlConstants.TryGetValue(id, out var values) ? new Vector3(values[0], values[1], values[2]) : @default;
    }
    
    public Vector4 GetConstantOrDefault(MaterialConstant id, Vector4 @default)
    {
        return MtrlConstants.TryGetValue(id, out var values) ? new Vector4(values[0], values[1], values[2], values[3]) : @default;
    }
}
