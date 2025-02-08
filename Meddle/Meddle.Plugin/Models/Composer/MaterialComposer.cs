﻿using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meddle.Plugin.Models.Composer.Materials;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using Exception = System.Exception;

namespace Meddle.Plugin.Models.Composer;

public class MaterialComposer
{
    private readonly MtrlFile mtrlFile;
    private readonly string mtrlPath;
    private readonly ShaderPackage shaderPackage;
    public readonly Dictionary<MaterialConstant, float[]> ShpkConstants = new();
    public readonly Dictionary<MaterialConstant, float[]> MtrlConstants = new();
    public readonly Dictionary<ShaderCategory, uint> ShaderKeyDict = new();
    private readonly Dictionary<string, object> additionalProperties = new();
    public JsonNode ExtrasNode => JsonNode.Parse(JsonSerializer.Serialize(additionalProperties, JsonOptions))!;
    public readonly Dictionary<TextureUsage, HandleString> TextureUsageDict = new();
    public bool RenderBackfaces => (mtrlFile.ShaderHeader.Flags & (uint)ShaderFlags.HideBackfaces) == 0;
    public bool IsTransparent => (mtrlFile.ShaderHeader.Flags & (uint)ShaderFlags.EnableTranslucency) != 0;

    private void SetShaderKeys()
    {
        foreach (var (key, value) in shaderPackage.DefaultKeyValues)
        {
            ShaderKeyDict[(ShaderCategory)key] = value;
        }

        foreach (var key in mtrlFile.ShaderKeys)
        {
            ShaderKeyDict[(ShaderCategory)key.Category] = key.Value;
        }
    }

    private void SetConstants()
    {
        foreach (var constant in mtrlFile.Constants)
        {
            var id = (MaterialConstant)constant.ConstantId;
            var index = constant.ValueOffset / 4;
            var count = constant.ValueSize / 4;
            var buf = new List<uint>(128);
            var logOutOfBounds = false;
            for (var j = 0; j < count; j++)
            {
                if (mtrlFile.ShaderValues.Length <= index + j)
                {
                    logOutOfBounds = true;
                    break;
                }

                var value = mtrlFile.ShaderValues[index + j];
                buf.Add(value);
            }

            // even if duplicate, last probably takes precedence
            MtrlConstants[id] = MemoryMarshal.Cast<uint, float>(buf.ToArray()).ToArray();
            if (logOutOfBounds)
            {
                Plugin.Logger?.LogWarning("Material constant {id} out of bounds for {mtrlPath}, {indexAndCount} greater than {shaderValuesLength}, [{values}]",
                                          id, mtrlPath, (index, count), mtrlFile.ShaderValues.Length, string.Join(", ", buf));
            }
        }
    }

    private void SetSamplers()
    {
        var texturePaths = mtrlFile.GetTexturePaths();
        foreach (var sampler in mtrlFile.Samplers)
        {
            if (sampler.TextureIndex == byte.MaxValue) continue;
            if (mtrlFile.TextureOffsets.Length <= sampler.TextureIndex)
            {
                throw new Exception($"Texture index {sampler.TextureIndex} out of bounds for {mtrlPath}");
            }
            var textureInfo = mtrlFile.TextureOffsets[sampler.TextureIndex];
            if (!texturePaths.TryGetValue(textureInfo.Offset, out var gamePath))
            {
                throw new Exception($"Texture offset {textureInfo.Offset} not found in {mtrlPath}");
            }

            if (!shaderPackage.TextureLookup.TryGetValue(sampler.SamplerId, out var usage))
            {
                continue;
            }

            TextureUsageDict[usage] = gamePath;
        }

    }

    public void SetPropertiesFromCharacterInfo(ParsedCharacterInfo characterInfo)
    {
        var customizeData = characterInfo.CustomizeData;
        var customizeParameter = characterInfo.CustomizeParameter;
        
        SetProperty("Highlights", customizeData.Highlights);
        SetProperty("LipStick", customizeData.LipStick);
        SetProperty("CustomizeData", JsonNode.Parse(JsonSerializer.Serialize(customizeData, JsonOptions))!);
        
        SetProperty("LeftIrisColor", customizeParameter.LeftColor.AsFloatArray());
        SetProperty("RightIrisColor", customizeParameter.RightColor.AsFloatArray());
        SetProperty("MainColor", customizeParameter.MainColor.AsFloatArray());
        SetProperty("SkinColor", customizeParameter.SkinColor.AsFloatArray());
        SetProperty("MeshColor", customizeParameter.MeshColor.AsFloatArray());
        SetProperty("LipColor", customizeParameter.LipColor.AsFloatArray());
        SetProperty("FacePaintUVOffset", customizeParameter.FacePaintUVOffset);
        SetProperty("FacePaintUVMultiplier", customizeParameter.FacePaintUVMultiplier);
        SetProperty("MuscleTone", customizeParameter.MuscleTone);
        SetProperty("OptionColor", customizeParameter.OptionColor.AsFloatArray());
        SetProperty("CustomizeParameters", JsonNode.Parse(JsonSerializer.Serialize(customizeParameter, JsonOptions))!);
    }

    public void SetPropertiesFromInstance(ParsedInstance instance)
    {
        if (instance is IStainableInstance {StainColor: not null} stainInstance)
        {
            SetProperty("StainColor", stainInstance.StainColor.Value.AsFloatArray());
        }
    }
    
    public void SetPropertiesFromColorTable(IColorTableSet colorTableSet)
    {
        if (colorTableSet is ColorTableSet colorTable)
        {
            SetProperty("ColorTable", JsonNode.Parse(JsonSerializer.Serialize(colorTable.ToObject(), JsonOptions))!);
        }
        else if (colorTableSet is LegacyColorTableSet legacyColorTable)
        {
            SetProperty("LegacyColorTable", JsonNode.Parse(JsonSerializer.Serialize(legacyColorTable.ToObject(), JsonOptions))!);
        }
    }
    
    public MaterialComposer(MtrlFile mtrlFile, string mtrlPath, ShaderPackage shaderPackage)
    {
        this.mtrlFile = mtrlFile;
        this.mtrlPath = mtrlPath;
        this.shaderPackage = shaderPackage;
        SetShaderKeys();
        SetConstants();
        SetSamplers();

        SetProperty("ShaderPackage", shaderPackage.Name);
        SetProperty("Material", mtrlPath);
        SetProperty("RenderBackfaces", RenderBackfaces);
        SetProperty("IsTransparent", IsTransparent);

        string IsDefinedOrHex<TEnum>(TEnum value) where TEnum : Enum
        {
            return Enum.IsDefined(typeof(TEnum), value) ? value.ToString() : $"0x{Convert.ToUInt32(value):X8}";
        }

        foreach (var key in ShaderKeyDict)
        {
            var category = (uint)key.Key;
            var value = key.Value;
            if (Enum.IsDefined(typeof(ShaderCategory), category))
            {
                var valStr = key.Key switch
                {
                    ShaderCategory.CategoryHairType => IsDefinedOrHex((HairType)value),
                    ShaderCategory.CategorySkinType => IsDefinedOrHex((SkinType)value),
                    ShaderCategory.CategoryDiffuseAlpha => IsDefinedOrHex((DiffuseAlpha)value),
                    ShaderCategory.CategorySpecularType => IsDefinedOrHex((SpecularMode)value),
                    ShaderCategory.GetValuesTextureType => IsDefinedOrHex((Meddle.Utils.Constants.TextureMode)value),
                    ShaderCategory.CategoryFlowMapType => IsDefinedOrHex((FlowType)value),
                    ShaderCategory.CategoryBgVertexPaint => IsDefinedOrHex((BgVertexPaint)value),
                    _ => $"0x{value:X8}"
                };

                SetProperty(key.Key.ToString(), valStr);
            }
            else
            {
                SetProperty($"0x{category:X8}", $"0x{value:X8}");
            }
        }

        foreach (var (usage, path) in TextureUsageDict)
        {
            var usageStr = usage.ToString();
            SetProperty(usageStr, path.GamePath);
            SetProperty($"{usageStr}_FullPath", path.FullPath);
        }

        var all = new Dictionary<MaterialConstant, float[]>(ShpkConstants);
        foreach (var (key, value) in MtrlConstants)
        {
            all[key] = value;
        }
        foreach (var (constant, value) in all)
        {
            if (Enum.IsDefined(typeof(MaterialConstant), constant))
            {
                SetProperty(constant.ToString(), value);
            }
            else
            {
                var key = $"0x{(uint)constant:X8}";
                SetProperty(key, value);
            }
        }
    }

    public void SetProperty(string key, object value)
    {
        additionalProperties[key] = value;
    }

    public static JsonSerializerOptions JsonOptions => new()
    {
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
}
