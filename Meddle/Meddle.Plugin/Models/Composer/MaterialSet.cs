using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Plugin.Models.Layout;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using Meddle.Utils.Materials;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using ShaderPackage = Meddle.Utils.Export.ShaderPackage;

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
    public readonly Dictionary<TextureUsage, HandleString> TextureUsageDict;
    private readonly Func<HandleString, TextureResource?>? textureLoader;
    
    public bool TryGetTextureStrict(DataProvider provider, TextureUsage usage, out TextureResource texture)
    {
        if (!TextureUsageDict.TryGetValue(usage, out var path))
        {
            throw new Exception($"Texture usage {usage} not found in material set");
        }
        
        if (textureLoader != null)
        {
            if (textureLoader(path) is { } tex)
            {
                texture = tex;
                return true;
            }
            
            texture = default;
            return false;
        }
        
        var data = provider.LookupData(path.FullPath);
        if (data == null)
        {
            texture = default;
            return false;
        }
        
        texture = new TexFile(data).ToResource();
        return true;
    }

    public ImageBuilder GetImageBuilderStrict(DataProvider provider, TextureUsage usage)
    {
        if (!TextureUsageDict.TryGetValue(usage, out var path))
        {
            throw new Exception($"Texture usage {usage} not found in material set");
        }
        
        var builder = provider.LookupTexture(path.FullPath) ?? throw new Exception($"Texture {path.FullPath} not found");
        return builder;
    }
    
    public bool TryGetImageBuilder(DataProvider provider, TextureUsage usage, out ImageBuilder? builder)
    {
        if (!TextureUsageDict.TryGetValue(usage, out var path))
        {
            builder = null;
            return false;
        }
        
        builder = provider.LookupTexture(path.FullPath);
        return builder != null;
    }
    
    public bool TryGetTexture(DataProvider provider, TextureUsage usage, out TextureResource texture)
    {
        if (!TextureUsageDict.TryGetValue(usage, out var path))
        {
            texture = default;
            return false;
        }
        
        if (textureLoader != null)
        {
            if (textureLoader(path) is { } tex)
            {
                texture = tex;
                return true;
            }
            
            texture = default;
            return false;
        }
        
        var data = provider.LookupData(path.FullPath);
        if (data == null)
        {
            texture = default;
            return false;
        }
        
        texture = new TexFile(data).ToResource();
        return true;
    }

    private readonly uint shaderFlagData;
    public bool RenderBackfaces => (shaderFlagData & (uint)ShaderFlags.HideBackfaces) == 0;
    public bool IsTransparent => (shaderFlagData & (uint)ShaderFlags.EnableTranslucency) != 0;
    
    
    private IColorTableSet? colorTable;
    public void SetColorTable(IColorTableSet? table)
    {
        this.colorTable = table;
    }
    
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
    
    public T GetConstantOrThrow<T>(MaterialConstant id) where T : struct
    {
        if (!TryGetConstant(id, out float[] value))
        {
            throw new InvalidOperationException($"Missing constant {id}");
        }
        
        switch (typeof(T).Name)
        {
            case "Single":
                return (T)(object)value[0];
            case "Vector2":
                return (T)(object)new Vector2(value[0], value[1]);
            case "Vector3":
                return (T)(object)new Vector3(value[0], value[1], value[2]);
            case "Vector4":
                return (T)(object)new Vector4(value[0], value[1], value[2], value[3]);
            default:
                throw new InvalidOperationException($"Unsupported type {typeof(T).Name}");
        }
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

    public MaterialSet(MtrlFile file, string mtrlPath, ShpkFile shpk, string shpkName, HandleString[]? texturePathOverride, Func<HandleString, TextureResource?>? textureLoader)
    {
        this.MtrlPath = mtrlPath;
        this.ShpkName = shpkName;
        this.textureLoader = textureLoader;
        shaderFlagData = file.ShaderHeader.Flags;
        var package = new ShaderPackage(shpk, shpkName);
        colorTable = file.GetColorTable();
        ShaderKeys = file.ShaderKeys;
        Constants = package.MaterialConstants;
        
        // override with material constants
        foreach (var constant in file.Constants)
        {
            var id = (MaterialConstant)constant.ConstantId;
            var index = constant.ValueOffset / 4;
            var count = constant.ValueSize / 4;
            var buf = new List<uint>(128);
            for (var j = 0; j < count; j++)
            {
                if (file.ShaderValues.Length <= index + j)
                {
                    Plugin.Logger?.LogWarning("Material {mtrlPath} has invalid constant {id} at index {index} " +
                                              "(max {file.ShaderValues.Length}, count {count}, j {j})", 
                                              mtrlPath, id, index, file.ShaderValues.Length, count, j);
                    break;
                }
                
                var value = file.ShaderValues[index + j];
                buf.Add(value);
            }

            // even if duplicate, last probably takes precedence
            Constants[id] = MemoryMarshal.Cast<uint, float>(buf.ToArray()).ToArray();
        }

        TextureUsageDict = new Dictionary<TextureUsage, HandleString>();
        var texturePaths = file.GetTexturePaths();
        foreach (var sampler in file.Samplers)
        {
            if (sampler.TextureIndex == byte.MaxValue) continue;
            if (file.TextureOffsets.Length <= sampler.TextureIndex)
            {
                throw new Exception($"Texture index {sampler.TextureIndex} out of bounds for {mtrlPath}");
            }
            var textureInfo = file.TextureOffsets[sampler.TextureIndex];
            if (!texturePaths.TryGetValue(textureInfo.Offset, out var gamePath))
            {
                throw new Exception($"Texture offset {textureInfo.Offset} not found in {mtrlPath}");
            }
            if (!package.TextureLookup.TryGetValue(sampler.SamplerId, out var usage))
            {
                continue;
            }

            var path = texturePathOverride?[sampler.TextureIndex] ?? gamePath;
            TextureUsageDict[usage] = path;
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
            case "bguvscroll.shpk":
                return new BgMaterialBuilder(mtrlName, new BgParams(), this, dataProvider);
            case "bgcolorchange.shpk":
                return new BgMaterialBuilder(mtrlName, new BgColorChangeParams(stainColor), this, dataProvider);
            case "lightshaft.shpk":
                return new LightshaftMaterialBuilder(mtrlName, this, dataProvider);
            case "character.shpk":
            case "characterlegacy.shpk":
                return new CharacterMaterialBuilder(mtrlName, this, dataProvider, colorTable);
            case "charactertattoo.shpk":
                return new CharacterTattooMaterialBuilder(mtrlName, this, dataProvider, customizeParameters ?? new CustomizeParameter());
            case "characterocclusion.shpk":
                return new CharacterOcclusionMaterialBuilder(mtrlName, this, dataProvider);
            case "skin.shpk":
                return new SkinMaterialBuilder(mtrlName, this, dataProvider, customizeParameters ?? new CustomizeParameter(), customizeData ?? new CustomizeData());
            case "hair.shpk":
                return new HairMaterialBuilder(mtrlName, this, dataProvider, customizeParameters ?? new CustomizeParameter());
            case "iris.shpk":
                return new IrisMaterialBuilder(mtrlName, this, dataProvider, customizeParameters ?? new CustomizeParameter());
            default:
                return new GenericMaterialBuilder(mtrlName, this, dataProvider);
        }
    }
    
    public MaterialBuilder Compose(DataProvider dataProvider)
    {
        var builder = GetMaterialBuilder(dataProvider);
        return builder.Apply();
    }
    
    public JsonNode ComposeExtrasNode(params (string key, object value)[]? additionalExtras)
    {
        var extrasDict = ComposeExtras();
        if (additionalExtras != null)
        {
            foreach (var (key, value) in additionalExtras)
            {
                extrasDict[key] = value;
            }
        }
        
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
        AddStainColor();
        AddColorTable();

        return extrasDict;

        void AddCustomizeParameters()
        {
            if (customizeParameters == null) return;
            extrasDict["CustomizeParameters"] = JsonNode.Parse(JsonSerializer.Serialize(customizeParameters, JsonOptions))!;
        }

        void AddColorTable()
        {
            if (colorTable == null) return;
            extrasDict["ColorTable"] = JsonNode.Parse(JsonSerializer.Serialize(colorTable, JsonOptions))!;
        }
        
        void AddStainColor()
        {
            if (stainColor == null) return;
            extrasDict["StainColor"] = stainColor.Value.AsFloatArray();
        }
        
        void AddCustomizeData()
        {
            if (customizeData == null) return;
            extrasDict["CustomizeData"] = JsonNode.Parse(JsonSerializer.Serialize(customizeData, JsonOptions))!;
        }
        
        string IsDefinedOrHex<TEnum>(TEnum value) where TEnum : Enum
        {
            return Enum.IsDefined(typeof(TEnum), value) ? value.ToString() : $"0x{Convert.ToUInt32(value):X8}";
        }
        
        void AddShaderKeys()
        {
            foreach (var key in ShaderKeys)
            {
                var category = key.Category;
                var value = key.Value;
                if (Enum.IsDefined(typeof(ShaderCategory), category))
                {
                    var keyCat = (ShaderCategory)category;
                    var valStr = keyCat switch
                    {
                        ShaderCategory.CategoryHairType => IsDefinedOrHex((HairType)value),
                        ShaderCategory.CategorySkinType => IsDefinedOrHex((SkinType)value),
                        ShaderCategory.CategoryDiffuseAlpha => IsDefinedOrHex((DiffuseAlpha)value),
                        ShaderCategory.CategorySpecularType => IsDefinedOrHex((SpecularMode)value),
                        ShaderCategory.GetValuesTextureType => IsDefinedOrHex((TextureMode)value),
                        ShaderCategory.CategoryFlowMapType => IsDefinedOrHex((FlowType)value),
                        ShaderCategory.CategoryBgVertexPaint => IsDefinedOrHex((BgVertexPaint)value),
                        _ => $"0x{value:X8}"
                    };
                    
                    extrasDict[keyCat.ToString()] = valStr;
                }
                else
                {
                    var keyStr = $"0x{category:X8}";
                    extrasDict[keyStr] = $"0x{value:X8}";
                }
            }
        }
        
        void AddSamplers()
        {
            foreach (var (usage, path) in TextureUsageDict)
            {
                var usageStr = usage.ToString();
                extrasDict[usageStr] = path.GamePath;
                if (path.FullPath != path.GamePath)
                {
                    extrasDict[$"{usageStr}_FullPath"] = path.FullPath;
                }
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
