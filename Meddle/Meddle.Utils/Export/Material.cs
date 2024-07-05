using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Models;

namespace Meddle.Utils.Export;


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
    public Material(MtrlFile file, string handlePath, ShpkFile shaderFile, IReadOnlyDictionary<string, TexFile> texFiles)
    {
        HandlePath = handlePath;
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

        var textures = new List<Texture>();
        var texturePaths = file.GetTexturePaths();
        for (int i = 0; i < file.Samplers.Length; i++)
        {
            var sampler = file.Samplers[i];
            var texture = file.TextureOffsets[sampler.TextureIndex];
            var path = texturePaths[texture.Offset];
            var texFile = texFiles[path];
            var texObj = new Texture(texFile, path, sampler.Flags, sampler.SamplerId, shaderFile);
            
            textures.Add(texObj);
        }
        
        Textures = textures;
        ColorTable = file.ColorTable;
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
    
    // https://github.com/Shaderlayan/Ouroboros
    public struct ShaderKey
    {
        public enum ShaderKeyCategory : uint
        {
            // Note:
            // CharacterGlass always Color
            // Hair always Color
            // Iris always Multi
            // Skin always Multi
            VertexColorModeMulti = 4113354501,
            SkinType = 940355280,
            HairType = 612525193,
            TextureMode = 3054951514,
            DecalMode = 3531043187,
            SpecularMapMode = 3367837167
        }

        public enum TextureMode : uint
        {
            Multi = 1556481461,
            
            // Diffuse Color: #D50000 Specular Color: #FFFFFF Specular Strength: 1.00 Gloss Strength: 100 Emissive Color: #8C0000
            // Ignores vertex colors and normal map (except for opacity)
            // Accepts no color table and no textures
            Simple = 581216959, 
            Compatibility = 1611594207 // Diffuse / Specular
        }
        
        public enum DecalMode : uint
        {
            None = 1111668802,
            Alpha = 1480746461, // Face paint
            Color = 4083110193 // FC crest
        }

        // This is a setting of the Compatibility (Diffuse / Specular) Texture Mode, and has no effect outside of it.
        public enum SpecularMapMode : uint
        {
            Color = 428675533,
            Multi = 2687453224 
        }

        public enum VertexColorModeMultiValue : uint
        {
            Color = 3756477356,
            Multi = 2815623008
        }

        public enum SkinTypeValue : uint
        {
            Face = 4117181732,
            Body = 735790577,
            BodyWithHair = 1476344676 // used notably on hrothgar
        }

        public enum HairTypeValue : uint
        {
            Hair = 4156069230,
            Face = 1851494160
        }
        
        public uint Category;
        public uint Value;
        
        public ShaderKeyCategory CategoryEnum => (ShaderKeyCategory)Category;
        public TextureMode TextureModeEnum => (TextureMode)Value;
        public DecalMode DecalModeEnum => (DecalMode)Value;
        public SpecularMapMode SpecularMapModeEnum => (SpecularMapMode)Value;
        public VertexColorModeMultiValue VertexColorModeMultiValueEnum => (VertexColorModeMultiValue)Value;
        public SkinTypeValue SkinTypeValueEnum => (SkinTypeValue)Value;
        public HairTypeValue HairTypeValueEnum => (HairTypeValue)Value;
    }
}
