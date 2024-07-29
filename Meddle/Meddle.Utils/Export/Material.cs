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
    CategoryFlowMapType = 0x40D1481E,  // STANDARD, FLOW
    CategoryDiffuseAlpha = 0xA9A3EE25
}

public enum DiffuseAlpha : uint
{
    Default = 0x0,
    UseDiffuseAlphaAsOpacity = 0x72AAA9AE
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

public class Material
{
    public Material(string path, MtrlFile file, Dictionary<string, TextureResource> texFiles, ShpkFile shpkFile)
    {
        HandlePath = path;
        InitFromFile(file);
        InitTextures(file, texFiles, shpkFile);
    }
    
    public Material(string path, MtrlFile file, Dictionary<string, TexFile> texFiles, ShpkFile shpkFile)
    {
        HandlePath = path;
        InitFromFile(file);
        InitTextures(file, texFiles.ToDictionary(x => x.Key, x => Texture.GetResource(x.Value)), shpkFile);
    }
    
    private void InitTextures(MtrlFile file, Dictionary<string, TextureResource> texFiles, ShpkFile shpkFile)
    {        
        var textures = new List<Texture>();
        var texturePaths = file.GetTexturePaths();
        for (var i = 0; i < file.Samplers.Length; i++)
        {
            var sampler = file.Samplers[i];
            if (sampler.TextureIndex != byte.MaxValue)
            {
                var texture = file.TextureOffsets[sampler.TextureIndex];
                var path = texturePaths[texture.Offset];
                if (!texFiles.TryGetValue(path, out var textureResource))
                    throw new ArgumentException($"Texture {path} not found");
                var texObj = new Texture(textureResource, path, sampler.Flags, sampler.SamplerId, shpkFile);

                textures.Add(texObj);
            }
        }

        Textures = textures;
    }
    
    private void InitFromFile(MtrlFile file)
    {
        ShaderFlags = file.ShaderHeader.Flags;
        ShaderPackageName = file.GetShaderPackageName();

        var shaderKeys = new ShaderKey[file.ShaderKeys.Length];
        for (var i = 0; i < file.ShaderKeys.Length; i++)
        {
            shaderKeys[i] = new ShaderKey
            {
                Category = file.ShaderKeys[i].Category,
                Value = file.ShaderKeys[i].Value
            };
        }

        ShaderKeys = shaderKeys;
        
        var materialConstantDict = new Dictionary<MaterialConstant, float[]>();
        foreach (var constant in file.Constants)
        {
            var index = constant.ValueOffset / 4;
            var count = constant.ValueSize / 4;
            var buf = new List<byte>(128);
            for (var j = 0; j < count; j++)
            {
                var value = file.ShaderValues[index + j];
                var bytes = BitConverter.GetBytes(value);
                buf.AddRange(bytes);
            }

            var floats = MemoryMarshal.Cast<byte, float>(buf.ToArray());
            var values = new float[count];
            for (var j = 0; j < count; j++)
            {
                values[j] = floats[j];
            }

            // even if duplicate, last probably takes precedence
            var id = (MaterialConstant)constant.ConstantId;
            materialConstantDict[id] = values;
        }
        
        MtrlConstants = materialConstantDict;
        ColorTable = file.ColorTable;
    }

    public string HandlePath { get; private set; }
    public uint ShaderFlags { get; private set; }

    public bool RenderBackfaces => (ShaderFlags & (uint)Files.ShaderFlags.HideBackfaces) == 0;
    public bool IsTransparent => (ShaderFlags & (uint)Files.ShaderFlags.EnableTranslucency) != 0;
    public float ComputeAlpha(float alpha)
    {
        if (IsTransparent)
        {
            return alpha;
        }

        if (alpha < 1.0f)
        {
            return 0.0f;
        }

        return 1.0f;
    }


    
    public IReadOnlyList<ShaderKey> ShaderKeys { get; private set; }
    public Dictionary<MaterialConstant, float[]> MtrlConstants { get; private set; }
    public string ShaderPackageName { get; private set; }
    public IReadOnlyList<Texture> Textures { get; private set; }

    [JsonIgnore]
    public ColorTable ColorTable { get; private set; }

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

    public float GetConstantOrDefault(MaterialConstant id, float @default)
    {
        return MtrlConstants.TryGetValue(id, out var values) ? values[0] : @default;
    }
    
    public Vector2 GetConstantOrDefault(MaterialConstant id, Vector2 @default)
    {
        return MtrlConstants.TryGetValue(id, out var values) ? new Vector2(values[0], values[1]) : @default;
    }
    
    public Vector3 GetConstantOrDefault(MaterialConstant id, Vector3 @default)
    {
        return MtrlConstants.TryGetValue(id, out var values) ? new Vector3(values[0], values[1], values[2]) : @default;
    }

    public Vector4 GetConstantOrDefault(MaterialConstant id, Vector4 @default)
    {
        return MtrlConstants.TryGetValue(id, out var values)
                   ? new Vector4(values[0], values[1], values[2], values[3])
                   : @default;
    }

    public record Constant(MaterialConstant Id, float[] Values);
}
