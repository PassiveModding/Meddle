using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Models;

namespace Meddle.Plugin.Models.Composer;

public class MaterialSet
{
    public readonly string MtrlPath;
    public readonly MtrlFile File;
    public readonly ShpkFile Shpk;
    public readonly string ShpkName;
    public readonly ShaderPackage Package;
    public readonly ShaderKey[] ShaderKeys;
    public readonly Dictionary<MaterialConstant, float[]> MaterialConstantDict;
    public readonly Dictionary<TextureUsage, string> TextureUsageDict;

    public Dictionary<MaterialConstant, float[]> GetConstants()
    {
        var dict = new Dictionary<MaterialConstant, float[]>();
        foreach (var (key, value) in Package.MaterialConstants)
        {
            var values = new float[value.Length];
            value.CopyTo(values, 0);
            dict[key] = values;
        }
        
        foreach (var (key, value) in MaterialConstantDict)
        {
            var values = new float[value.Length];
            value.CopyTo(values, 0);
            dict[key] = values;
        }
        
        return dict;
    }
    
    public bool TryGetConstant(MaterialConstant id, out float[] value)
    {
        if (MaterialConstantDict.TryGetValue(id, out var values))
        {
            value = values;
            return true;
        }

        if (Package.MaterialConstants.TryGetValue(id, out var constant))
        {
            value = constant;
            return true;
        }

        value = [];
        return false;
    }

    public bool TryGetConstant(MaterialConstant id, out float value)
    {
        if (MaterialConstantDict.TryGetValue(id, out var values))
        {
            value = values[0];
            return true;
        }

        if (Package.MaterialConstants.TryGetValue(id, out var constant))
        {
            value = constant[0];
            return true;
        }

        value = 0;
        return false;
    }

    public bool TryGetConstant(MaterialConstant id, out Vector2 value)
    {
        if (MaterialConstantDict.TryGetValue(id, out var values))
        {
            value = new Vector2(values[0], values[1]);
            return true;
        }

        if (Package.MaterialConstants.TryGetValue(id, out var constant))
        {
            value = new Vector2(constant[0], constant[1]);
            return true;
        }

        value = Vector2.Zero;
        return false;
    }

    public bool TryGetConstant(MaterialConstant id, out Vector3 value)
    {
        if (MaterialConstantDict.TryGetValue(id, out var values))
        {
            value = new Vector3(values[0], values[1], values[2]);
            return true;
        }

        if (Package.MaterialConstants.TryGetValue(id, out var constant))
        {
            value = new Vector3(constant[0], constant[1], constant[2]);
            return true;
        }

        value = Vector3.Zero;
        return false;
    }

    public bool TryGetConstant(MaterialConstant id, out Vector4 value)
    {
        if (MaterialConstantDict.TryGetValue(id, out var values))
        {
            value = new Vector4(values[0], values[1], values[2], values[3]);
            return true;
        }

        if (Package.MaterialConstants.TryGetValue(id, out var constant))
        {
            value = new Vector4(constant[0], constant[1], constant[2], constant[3]);
            return true;
        }

        value = Vector4.Zero;
        return false;
    }

    public float GetConstantOrDefault(MaterialConstant id, float @default)
    {
        return MaterialConstantDict.TryGetValue(id, out var values) ? values[0] : @default;
    }

    public Vector2 GetConstantOrDefault(MaterialConstant id, Vector2 @default)
    {
        return MaterialConstantDict.TryGetValue(id, out var values) ? new Vector2(values[0], values[1]) : @default;
    }

    public Vector3 GetConstantOrDefault(MaterialConstant id, Vector3 @default)
    {
        return MaterialConstantDict.TryGetValue(id, out var values)
                   ? new Vector3(values[0], values[1], values[2])
                   : @default;
    }

    public Vector4 GetConstantOrDefault(MaterialConstant id, Vector4 @default)
    {
        return MaterialConstantDict.TryGetValue(id, out var values)
                   ? new Vector4(values[0], values[1], values[2], values[3])
                   : @default;
    }

    public MaterialSet(MtrlFile file, string mtrlPath, ShpkFile shpk, string shpkName)
    {
        this.MtrlPath = mtrlPath;
        this.File = file;
        this.Shpk = shpk;
        this.ShpkName = shpkName;
        this.Package = new ShaderPackage(shpk, shpkName);

        ShaderKeys = new ShaderKey[file.ShaderKeys.Length];
        for (var i = 0; i < file.ShaderKeys.Length; i++)
        {
            ShaderKeys[i] = new ShaderKey
            {
                Category = file.ShaderKeys[i].Category,
                Value = file.ShaderKeys[i].Value
            };
        }

        MaterialConstantDict = new Dictionary<MaterialConstant, float[]>();
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
            MaterialConstantDict[id] = values;
        }

        TextureUsageDict = new Dictionary<TextureUsage, string>();
        var texturePaths = file.GetTexturePaths();
        foreach (var sampler in file.Samplers)
        {
            if (sampler.TextureIndex == byte.MaxValue) continue;
            var textureInfo = file.TextureOffsets[sampler.TextureIndex];
            var texturePath = texturePaths[textureInfo.Offset];
            if (!Package.TextureLookup.TryGetValue(sampler.SamplerId, out var usage))
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

        return extrasDict;

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
            foreach (var (constant, value) in GetConstants())
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
