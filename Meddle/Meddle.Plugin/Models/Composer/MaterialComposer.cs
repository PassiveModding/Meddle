using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
    private readonly Dictionary<uint, float[]> mtrlConstants = new();
    public readonly IReadOnlyDictionary<ShaderCategory, uint> ShaderKeyDict;
    private readonly Dictionary<string, object> additionalProperties = new();
    public JsonNode ExtrasNode => JsonNode.Parse(JsonSerializer.Serialize(additionalProperties, JsonOptions))!;
    public readonly Dictionary<string, HandleString> TextureUsageDict = new();
    public bool RenderBackfaces => (mtrlFile.ShaderHeader.Flags & (uint)ShaderFlags.HideBackfaces) == 0;
    public bool IsTransparent => (mtrlFile.ShaderHeader.Flags & (uint)ShaderFlags.EnableTranslucency) != 0;

    public void SetPropertiesFromCharacterInfo(ParsedCharacterInfo characterInfo)
    {
        var customizeData = characterInfo.CustomizeData;
        var customizeParameter = characterInfo.CustomizeParameter;

        if (customizeData != null)
        {
            SetProperty("Highlights", customizeData.Highlights);
            SetProperty("LipStick", customizeData.LipStick);
            SetProperty("FacePaintReversed", customizeData.FacePaintReversed);
            SetProperty("LegacyBodyDecalPath", customizeData.LegacyBodyDecalPath ?? "");
            SetProperty("DecalPath", customizeData.DecalPath ?? "");
            SetProperty("CustomizeData", JsonNode.Parse(JsonSerializer.Serialize(customizeData, JsonOptions))!);
        }

        if (customizeParameter != null)
        {
            SetProperty("LeftIrisColor", customizeParameter.LeftColor.AsFloatArray());
            SetProperty("RightIrisColor", customizeParameter.RightColor.AsFloatArray());
            SetProperty("MainColor", customizeParameter.MainColor.AsFloatArray());
            SetProperty("SkinColor", customizeParameter.SkinColor.AsFloatArray());
            SetProperty("MeshColor", customizeParameter.MeshColor.AsFloatArray());
            SetProperty("LipColor", customizeParameter.LipColor.AsFloatArray());
            SetProperty("FacePaintUVOffset", customizeParameter.FacePaintUvOffset);
            SetProperty("FacePaintUVMultiplier", customizeParameter.FacePaintUvMultiplier);
            SetProperty("MuscleTone", customizeParameter.MuscleTone);
            SetProperty("OptionColor", customizeParameter.OptionColor.AsFloatArray());
            SetProperty("DecalColor", customizeParameter.DecalColor.AsFloatArray());
            SetProperty("CustomizeParameters", JsonNode.Parse(JsonSerializer.Serialize(customizeParameter, JsonOptions))!);
        }
    }

    public void SetPropertiesFromInstance(IStainableInstance instance)
    {
        if (instance is {Stain: not null} stainInstance && stainInstance.Stain.RowId != 0)
        {
            SetProperty("StainColor", stainInstance.Stain.Color.AsFloatArray());
            SetProperty("StainId", stainInstance.Stain.RowId);
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
    
    public static readonly HashSet<string> FailedConstants = new();
    
    public MaterialComposer(MtrlFile mtrlFile, string mtrlPath, ShaderPackage shaderPackage)
    {
        this.mtrlFile = mtrlFile;
        var shaderKeys = new Dictionary<ShaderCategory, uint>();
        this.ShaderKeyDict = shaderKeys;
        SetShaderKeys();
        SetConstants();
        SetSamplers();

        SetProperty("ShaderPackage", shaderPackage.Name);
        SetProperty("Material", mtrlPath);
        SetProperty("RenderBackfaces", RenderBackfaces);
        SetProperty("IsTransparent", IsTransparent);
        
        var constants = Names.GetConstants();
        foreach (var key in shaderKeys)
        {
            var category = (uint)key.Key;
            var value = key.Value;
            var keyMatch = constants.GetValueOrDefault(category);
            var valMatch = constants.GetValueOrDefault(value);
            SetProperty(keyMatch != null ? keyMatch.Value : $"0x{category:X8}", 
                        valMatch != null ? valMatch.Value : $"0x{value:X8}");
            if (keyMatch is null or Names.StubName)
            {
                FailedConstants.Add($"0x{category:X8}");
            }
            if (valMatch is null or Names.StubName)
            {
                FailedConstants.Add($"0x{value:X8}");
            }
        }
        
        foreach (var (constant, value) in mtrlConstants)
        {
            var keyMatch = constants.GetValueOrDefault(constant);
            SetProperty(keyMatch != null ? keyMatch.Value : $"0x{constant:X8}", value);
        }
        
        foreach (var (usage, path) in TextureUsageDict)
        {
            SetProperty(usage, path.GamePath);
            SetProperty($"{usage}_FullPath", path.FullPath);
        }

        return;
        
        void SetShaderKeys()
        {
            foreach (var (key, value) in shaderPackage.DefaultKeyValues)
            {
                shaderKeys[(ShaderCategory)key] = value;
            }

            foreach (var key in mtrlFile.ShaderKeys)
            {
                shaderKeys[(ShaderCategory)key.Category] = key.Value;
            }
        }
        
        void SetConstants()
        {
            foreach (var constant in mtrlFile.Constants)
            {
                var id = constant.ConstantId;
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
                mtrlConstants[id] = MemoryMarshal.Cast<uint, float>(buf.ToArray()).ToArray();
                if (logOutOfBounds)
                {
                    Plugin.Logger.LogWarning("Material constant {id} out of bounds for {mtrlPath}, {indexAndCount} greater than {shaderValuesLength}, [{values}]",
                                              id, mtrlPath, (index, count), mtrlFile.ShaderValues.Length, string.Join(", ", buf));
                }
            }
        }
        
        void SetSamplers()
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

                if (!shaderPackage.ResourceKeys.TryGetValue(sampler.SamplerId, out var usage))
                {
                    Plugin.Logger.LogWarning("Texture sampler usage {samplerId} not found in {mtrlPath}, {gamePath}", sampler.SamplerId, mtrlPath, gamePath);
                    continue;
                }

                TextureUsageDict[usage] = gamePath;
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
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = true,
        Converters = { new IntPtrSerializer(), new TransformSerializer() },
    };

    public void SetPropertiesFromMaterialInfo(ParsedMaterialInfo materialInfo)
    {
        if (materialInfo.Stain0 != null)
        {
            SetProperty("Stain0Id", materialInfo.Stain0!.RowId);
            SetProperty("Stain0Name", materialInfo.Stain0.Name);
        }
        
        if (materialInfo.Stain1 != null)
        {
            SetProperty("Stain1Id", materialInfo.Stain1.RowId);
            SetProperty("Stain1Name", materialInfo.Stain1.Name);
        }
    }
}
