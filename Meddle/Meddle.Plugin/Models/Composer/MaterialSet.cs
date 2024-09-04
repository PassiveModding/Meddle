using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models.Composer;

public class MaterialSet
{
    /// <summary>
    /// Should be appended to all computed textures, so we can skip recomputing them
    /// </summary>
    public int Uid()
    { 
        // hoping this is enough
        var hash = new HashCode();
        hash.Add(MtrlPath);
        hash.Add(ShpkName);
        hash.Add(stainColor);
        hash.Add(customizeParameters?.GetHashCode());
        hash.Add(customizeData?.GetHashCode());
        foreach (var (key, value) in Constants)
        {
            hash.Add(key);
            foreach (var v in value)
            {
                hash.Add(v);
            }
        }
        foreach (var (key, value) in TextureUsageDict)
        {
            hash.Add(key);
            hash.Add(value);
        }
        foreach (var key in ShaderKeys)
        {
            hash.Add(key.Category);
            hash.Add(key.Value);
        }
        return hash.ToHashCode();
    }

    private static string GetHashSha1(byte[] data)
    {
        var hash = SHA1.HashData(data);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("X2"));
        }

        return sb.ToString();
    }

    public string ComputedTextureName(string name)
    {
        return $"{Path.GetFileNameWithoutExtension(MtrlPath)}_" +
               $"{Path.GetFileNameWithoutExtension(ShpkName)}_" +
               $"{name}_{HashStr()}";
    }

    public string HashStr()
    {
        var hash = Uid();
        var hashStr = hash < 0 ? $"n{hash:X8}" : $"{hash:X8}";
        return hashStr;
    }
    
    //private readonly MtrlFile file;
    //private readonly ShpkFile shpk;
    //private readonly ShaderPackage package;
    public readonly string MtrlPath;
    public readonly string ShpkName;
    public readonly ShaderKey[] ShaderKeys;
    public readonly Dictionary<MaterialConstant, float[]> Constants;
    public readonly Dictionary<TextureUsage, string> TextureUsageDict;
    private Dictionary<string, string>? texturePathMappings;
    public string MapTexturePath(string path)
    {
        if (texturePathMappings != null)
        {
            if (texturePathMappings.TryGetValue(path, out var mappedPath))
            {
                return mappedPath;
            }
        }
        
        return path;
    }
    
    public TexFile GetTexture(DataProvider provider, TextureUsage usage)
    {
        var path = GetTexturePath(usage);
        var data = provider.LookupData(MapTexturePath(path));
        if (data == null)
        {
            throw new Exception($"Failed to load texture for {usage}");
        }
        
        return new TexFile(data);
    }
    
    public byte[] LookupData(DataProvider provider, string path)
    {
        var data = provider.LookupData(MapTexturePath(path));
        if (data == null)
        {
            throw new Exception($"Failed to load data for {path}");
        }

        return data;
    }
    
    public string GetTexturePath(TextureUsage usage)
    {
        var path = TextureUsageDict[usage];
        if (texturePathMappings != null && texturePathMappings.TryGetValue(usage.ToString(), out var mappedPath))
        {
            return mappedPath;
        }
        
        return path;
    }
    
    public void SetTexturePathMappings(Dictionary<string, string> mappings)
    {
        texturePathMappings = mappings;
    }

    private uint ShaderFlagData;
    public bool RenderBackfaces => (ShaderFlagData & (uint)ShaderFlags.HideBackfaces) == 0;
    public bool IsTransparent => (ShaderFlagData & (uint)ShaderFlags.EnableTranslucency) != 0;
    
    
    public readonly ColorTable? ColorTable;
    
    // BgColorChange
    private Vector4? stainColor;
    public void SetStainColor(Vector4? color)
    {
        stainColor = color;
    }
    
    // Character
    private CustomizeParameter? customizeParameters;
    public void SetCustomizeParameters(CustomizeParameter parameters)
    {
        customizeParameters = parameters;
    }
    
    private CustomizeData? customizeData;
    public void SetCustomizeData(CustomizeData data)
    {
        customizeData = data;
    }
    
    public bool TryGetConstant(MaterialConstant id, out float[] value)
    {
        if (Constants.TryGetValue(id, out var values))
        {
            value = values;
            return true;
        }

        value = [];
        return false;
    }

    public bool TryGetConstant(MaterialConstant id, out float value)
    {
        if (Constants.TryGetValue(id, out var values))
        {
            value = values[0];
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryGetConstant(MaterialConstant id, out Vector2 value)
    {
        if (Constants.TryGetValue(id, out var values))
        {
            value = new Vector2(values[0], values[1]);
            return true;
        }

        value = Vector2.Zero;
        return false;
    }

    public bool TryGetConstant(MaterialConstant id, out Vector3 value)
    {
        if (Constants.TryGetValue(id, out var values))
        {
            value = new Vector3(values[0], values[1], values[2]);
            return true;
        }

        value = Vector3.Zero;
        return false;
    }

    public bool TryGetConstant(MaterialConstant id, out Vector4 value)
    {
        if (Constants.TryGetValue(id, out var values))
        {
            value = new Vector4(values[0], values[1], values[2], values[3]);
            return true;
        }

        value = Vector4.Zero;
        return false;
    }

    public float GetConstantOrDefault(MaterialConstant id, float @default)
    {
        return Constants.TryGetValue(id, out var values) ? values[0] : @default;
    }

    public Vector2 GetConstantOrDefault(MaterialConstant id, Vector2 @default)
    {
        return Constants.TryGetValue(id, out var values) ? new Vector2(values[0], values[1]) : @default;
    }

    public Vector3 GetConstantOrDefault(MaterialConstant id, Vector3 @default)
    {
        return Constants.TryGetValue(id, out var values)
                   ? new Vector3(values[0], values[1], values[2])
                   : @default;
    }

    public Vector4 GetConstantOrDefault(MaterialConstant id, Vector4 @default)
    {
        return Constants.TryGetValue(id, out var values)
                   ? new Vector4(values[0], values[1], values[2], values[3])
                   : @default;
    }
    
    public TValue GetShaderKeyOrDefault<TCategory, TValue>(TCategory category, TValue @default) where TCategory : Enum where TValue : Enum
    {
        var cat = Convert.ToUInt32(category);
        var value = GetShaderKeyOrDefault(cat, Convert.ToUInt32(@default));
        return (TValue)Enum.ToObject(typeof(TValue), value);
    }
    
    public TValue GetShaderKeyOrDefault<TCategory, TValue>(TCategory category, uint @default) where TCategory : Enum where TValue : Enum
    {
        var cat = Convert.ToUInt32(category);
        var value = GetShaderKeyOrDefault(cat, @default);
        return (TValue)Enum.ToObject(typeof(TValue), value);
    }
    
    public uint GetShaderKeyOrDefault(uint category, uint @default)
    {
        foreach (var key in ShaderKeys)
        {
            if (key.Category == category)
            {
                return key.Value;
            }
        }

        return @default;
    }

    public MaterialSet(MtrlFile file, string mtrlPath, ShpkFile shpk, string shpkName)
    {
        this.MtrlPath = mtrlPath;
        this.ShpkName = shpkName;
        ShaderFlagData = file.ShaderHeader.Flags;
        var package = new ShaderPackage(shpk, shpkName);
        ColorTable = file.ColorTable;
        
        ShaderKeys = new ShaderKey[file.ShaderKeys.Length];
        for (var i = 0; i < file.ShaderKeys.Length; i++)
        {
            ShaderKeys[i] = new ShaderKey
            {
                Category = file.ShaderKeys[i].Category,
                Value = file.ShaderKeys[i].Value
            };
        }

        Constants = new Dictionary<MaterialConstant, float[]>();
        // pre-fill with shader constants
        foreach (var constant in package.MaterialConstants)
        {
            Constants[constant.Key] = constant.Value;
        }
        
        // override with material constants
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
            Constants[id] = values;
        }

        TextureUsageDict = new Dictionary<TextureUsage, string>();
        var texturePaths = file.GetTexturePaths();
        foreach (var sampler in file.Samplers)
        {
            if (sampler.TextureIndex == byte.MaxValue) continue;
            var textureInfo = file.TextureOffsets[sampler.TextureIndex];
            var texturePath = texturePaths[textureInfo.Offset];
            if (!package.TextureLookup.TryGetValue(sampler.SamplerId, out var usage))
            {
                continue;
            }

            TextureUsageDict[usage] = texturePath;
        }
    }
    
    public static JsonSerializerOptions JsonOptions => new()
    {
        IncludeFields = true
    };
    
    private MeddleMaterialBuilder GetMaterialBuilder(DataProvider dataProvider)
    {
        var mtrlName = $"{Path.GetFileNameWithoutExtension(MtrlPath)}_{Path.GetFileNameWithoutExtension(ShpkName)}_{Uid()}";
        switch (ShpkName)
        {
            case "bg.shpk":
                return new BgMaterialBuilder(mtrlName, new BgParams(), this, dataProvider);
            case "bgcolorchange.shpk":
                return new BgMaterialBuilder(mtrlName, new BgColorChangeParams(stainColor), this, dataProvider);
            case "lightshaft.shpk":
                return new LightshaftMaterialBuilder(mtrlName, this, dataProvider);
            case "character.shpk":
                return new CharacterMaterialBuilder(mtrlName, this, dataProvider);
            case "charactertattoo.shpk":
                return new CharacterTattooMaterialBuilder(mtrlName, this, dataProvider, customizeParameters ?? new CustomizeParameter());
            case "characterocclusion.shpk":
                return new CharacterOcclusionMaterialBuilder(mtrlName, this, dataProvider);
            case "skin.shpk":
                return new SkinMaterialBuilder(mtrlName, this, dataProvider, customizeParameters ?? new CustomizeParameter(), customizeData ?? new CustomizeData());
            default:
                return new GenericMaterialBuilder(mtrlName, this, dataProvider);
        }
    }
    
    public MaterialBuilder Compose(DataProvider dataProvider)
    {
        var builder = GetMaterialBuilder(dataProvider);
        return builder.Apply();
    }
    
    public JsonNode ComposeExtrasNode()
    {
        var extrasDict = ComposeExtras();
        return JsonNode.Parse(JsonSerializer.Serialize(extrasDict, JsonOptions))!;
    }
    
    public Dictionary<string, object> ComposeExtras()
    {
        var extrasDict = new Dictionary<string, object>
        {
            {"ShaderPackage", ShpkName},
            {"Material", MtrlPath}
        };
        AddConstants();
        AddSamplers();
        AddShaderKeys();
        AddCustomizeParameters();
        AddCustomizeData();
        AddColorTable();
        
        if (stainColor.HasValue)
        {
            extrasDict["stainColor"] = stainColor.Value.AsFloatArray();
        }

        return extrasDict;

        void AddCustomizeParameters()
        {
            if (customizeParameters == null) return;
            extrasDict["CustomizeParameters"] = JsonNode.Parse(JsonSerializer.Serialize(customizeParameters, JsonOptions))!;
        }

        void AddColorTable()
        {
            if (ColorTable == null) return;
            extrasDict["ColorTable"] = JsonNode.Parse(JsonSerializer.Serialize(ColorTable, JsonOptions))!;
        }
        
        void AddCustomizeData()
        {
            if (customizeData == null) return;
            extrasDict["CustomizeData"] = JsonNode.Parse(JsonSerializer.Serialize(customizeData, JsonOptions))!;
        }
        
        void AddShaderKeys()
        {
            foreach (var key in ShaderKeys)
            {
                var category = key.Category;
                var value = key.Value;
                extrasDict[$"0x{category:X8}"] = $"0x{value:X8}";
            }
        }
        
        void AddSamplers()
        {
            foreach (var (usage, path) in TextureUsageDict)
            {
                extrasDict[usage.ToString()] = path;
            }
        }

        void AddConstants()
        {
            foreach (var (constant, value) in Constants)
            {
                if (Enum.IsDefined(typeof(MaterialConstant), constant))
                {
                    extrasDict[constant.ToString()] = value;
                }
                else
                {
                    var key = $"0x{(uint)constant:X8}";
                    extrasDict[key] = value;
                }
            }
        }
    }
}
