using System.Numerics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Services;
using Meddle.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using CustomizeData = Meddle.Utils.Export.CustomizeData;

namespace Meddle.Plugin.Utils;

    
public record struct ParsedHumanInfo
{
    public Meddle.Utils.Export.CustomizeParameter? CustomizeParameter;
    public CustomizeData? CustomizeData;
    public GenderRace GenderRace;
    public IReadOnlyList<EquipmentModelId> EquipmentModelIds;
}

public static class ParseMaterialUtil
{
    public static unsafe ParsedCharacterInfo? ParseDrawObject(Pointer<DrawObject> drawObjectPtr, PbdHooks pbdHooks)
    {
        if (drawObjectPtr == null || drawObjectPtr.Value == null)
        {
            return null;
        }
        
        var drawObject = drawObjectPtr.Value;
        var objectType = drawObject->Object.GetObjectType();
        if (objectType != ObjectType.CharacterBase)
        {
            return null;
        }
        
        var characterBase = (CharacterBase*)drawObject;
        var modelType = characterBase->GetModelType();
        var colorTableSets = ParseColorTableTextures(characterBase);
        
        var models = new List<ParsedModelInfo>();
        foreach (var modelPtr in characterBase->ModelsSpan)
        {
            if (modelPtr == null || modelPtr.Value == null)
            {
                continue;
            }
            
            var model = modelPtr.Value;
            var materials = new List<ParsedMaterialInfo?>();
                            
            Stain? stain0 = null;
            Stain? stain1 = null;
            if (modelType == CharacterBase.ModelType.Human)
            {
                var equipId = GetEquipmentModelId(characterBase, (HumanEquipmentSlotIndex)model->SlotIndex);
                stain0 = equipId != null ? StainProvider.GetStain(equipId.Value.Stain0) : null;
                stain1 = equipId != null ? StainProvider.GetStain(equipId.Value.Stain1) : null;
            }
            
            for (int materialIndex = 0; materialIndex < model->MaterialsSpan.Length; materialIndex++)
            {
                var materialPtr = model->MaterialsSpan[materialIndex];
                if (materialPtr == null || materialPtr.Value == null)
                {
                    materials.Add(null);
                    continue;
                }
                
                var material = materialPtr.Value;
                var onRenderMaterialOutput = modelType switch
                {
                    CharacterBase.ModelType.Human => OnRenderMaterialUtil.ResolveHumanOnRenderMaterial((Human*)characterBase, model, (uint)materialIndex),
                    CharacterBase.ModelType.DemiHuman => OnRenderMaterialUtil.ResolveDemihumanOnRenderMaterial((Demihuman*)characterBase, model, (uint)materialIndex),
                    CharacterBase.ModelType.Monster => OnRenderMaterialUtil.ResolveMonsterOnRenderMaterial((Monster*)characterBase, model, (uint)materialIndex),
                    CharacterBase.ModelType.Weapon => OnRenderMaterialUtil.ResolveWeaponOnRenderMaterial((Weapon*)characterBase, model, (uint)materialIndex),
                    _ => throw new NotImplementedException($"OnRenderMaterialUtil not implemented for model type {modelType}")
                };

                var materialPath = material->MaterialResourceHandle->FileName.ParseString();
                var shaderName = material->MaterialResourceHandle->ShpkName.ToString();
                string? materialPathFromModel;
                if (model != null)
                {
                    materialPathFromModel = modelPtr.Value->ModelResourceHandle->GetMaterialFileNameBySlot((uint)materialIndex);
                }
                else
                {
                    materialPathFromModel = materialPath;
                }

                var colorTableSet = GetColorTableSet(modelPtr, materialPtr, (uint)materialIndex, colorTableSets);
                var textures = new List<ParsedTextureInfo>();
                for (var texIdx = 0; texIdx < material->MaterialResourceHandle->TexturesSpan.Length; texIdx++)
                {
                    var texturePtr = material->MaterialResourceHandle->TexturesSpan[texIdx];
                    if (texturePtr.TextureResourceHandle == null) continue;

                    var texturePath = texturePtr.TextureResourceHandle->FileName.ParseString();
                    if (texIdx < material->TextureCount)
                    {
                        var texturePathFromMaterial = material->MaterialResourceHandle->TexturePath(texIdx);
                        var (resource, _) = DxHelper.ExportTextureResource(texturePtr.TextureResourceHandle->Texture);
                        var textureInfo = new ParsedTextureInfo(texturePath, texturePathFromMaterial, resource);
                        textures.Add(textureInfo);
                    }
                }

                var materialInfo = new ParsedMaterialInfo(
                    materialPath,
                    materialPathFromModel,
                    shaderName,
                    onRenderMaterialOutput,
                    colorTableSet,
                    textures.ToArray())
                {
                    Stain0 = stain0,
                    Stain1 = stain1
                };
                materials.Add(materialInfo);
            }
            
            var deform = modelType == CharacterBase.ModelType.Human ? pbdHooks.TryGetDeformer((nint)characterBase, model->SlotIndex) : null;
            var modelInfo = new ParsedModelInfo(
                model->ModelResourceHandle->FileName.ParseString(), 
                characterBase->ResolveMdlPath(model->SlotIndex), 
                deform, 
                StructExtensions.ParseModelShapeAttributes(model), 
                materials.ToArray(), 
                stain0, stain1)
            {
                ModelAddress = (nint)modelPtr.Value
            };
            models.Add(modelInfo);
        }
        
        var skeleton = StructExtensions.GetParsedSkeleton(characterBase);
        var parsedHumanInfo = ParseHuman(characterBase);
        return new ParsedCharacterInfo(models.ToArray(), skeleton, StructExtensions.GetParsedAttach(characterBase), parsedHumanInfo);
    }
    
    public static unsafe ParsedHumanInfo ParseHuman(Pointer<CharacterBase> characterBasePtr)
    {
        var characterBase = characterBasePtr.Value;
        var modelType = characterBase->GetModelType();
        if (modelType != CharacterBase.ModelType.Human)
        {
            return new ParsedHumanInfo
            {
                CustomizeParameter = null,
                CustomizeData = null,
                GenderRace = GenderRace.Unknown,
                EquipmentModelIds = []
            };
        }
        
        var human = (Human*)characterBase;
        var customizeCBuf = human->CustomizeParameterCBuffer->TryGetBuffer<CustomizeParameter>()[0];
        var decalCol = human->DecalColorCBuffer->TryGetBuffer<Vector4>()[0];
        var customizeParams = new Meddle.Utils.Export.CustomizeParameter
        {
            SkinColor = customizeCBuf.SkinColor,
            MuscleTone = customizeCBuf.MuscleTone,
            SkinFresnelValue0 = customizeCBuf.SkinFresnelValue0,
            LipColor = customizeCBuf.LipColor,
            MainColor = customizeCBuf.MainColor,
            FacePaintUvMultiplier = customizeCBuf.FacePaintUVMultiplier,
            HairFresnelValue0 = customizeCBuf.HairFresnelValue0,
            MeshColor = customizeCBuf.MeshColor,
            FacePaintUvOffset = customizeCBuf.FacePaintUVOffset,
            LeftColor = customizeCBuf.LeftColor,
            RightColor = customizeCBuf.RightColor,
            OptionColor = customizeCBuf.OptionColor,
            DecalColor = decalCol
        };
        var customizeData = new CustomizeData
        {
            LipStick = human->Customize.Lipstick,
            Highlights = human->Customize.Highlights,
            FacePaintReversed = human->Customize.FacePaintReversed,
        };
        var genderRace = (GenderRace)human->RaceSexId;
        var equipData = new List<EquipmentModelId>();
        for (var slotIdx = 0; slotIdx <= (int)HumanEquipmentSlotIndex.Extra; slotIdx++)
        {
            equipData.Add(GetEquipmentModelId(characterBase, (HumanEquipmentSlotIndex)slotIdx)!.Value);
        }
        
        return new ParsedHumanInfo
        {
            CustomizeParameter = customizeParams,
            CustomizeData = customizeData,
            GenderRace = genderRace,
            EquipmentModelIds = equipData.ToArray()
        };
    }
    
    private static unsafe IColorTableSet? GetColorTableSet(Pointer<Model> modelPtr, Pointer<Material> materialPtr, uint materialIndex, Dictionary<int, IColorTableSet> colorTableSets)
    {
        IColorTableSet? colorTable = null;
        if (materialPtr == null || materialPtr.Value == null)
        {
            return null;
        }
        
        if (modelPtr == null || modelPtr.Value == null)
        {
            return null;
        }
        
        var model = modelPtr.Value;
        var material = materialPtr.Value;
        if (colorTableSets.TryGetValue((int)(modelPtr.Value->SlotIndex * CharacterBase.MaterialsPerSlot) + (int)materialIndex, out var gpuColorTable))
        {
            colorTable = gpuColorTable;
        }
        else if (material->MaterialResourceHandle->HasColorTable)
        {
            var colorTableSpan = material->MaterialResourceHandle->ColorTableSpan;
            if (colorTableSpan.Length == ColorTable.Size)
            {
                var reader = new SpanBinaryReader(MemoryMarshal.AsBytes(colorTableSpan));
                colorTable = new ColorTableSet
                {
                    ColorTable = new ColorTable(ref reader)
                };
            }
            else if (colorTableSpan.Length == LegacyColorTable.Size)
            {
                var reader = new SpanBinaryReader(MemoryMarshal.AsBytes(colorTableSpan));
                colorTable = new LegacyColorTableSet
                {
                    ColorTable = new LegacyColorTable(ref reader)
                };
            }
        }
        
        return colorTable;
    }
    
    /// <summary>
    /// Parses the color table textures from the character base.
    /// Must be called from the main thread.
    /// </summary>
    public static unsafe Dictionary<int, IColorTableSet> ParseColorTableTextures(Pointer<CharacterBase> characterBasePtr)
    {
        if (characterBasePtr == null || characterBasePtr.Value == null)
        {
            return new Dictionary<int, IColorTableSet>();
        }
        var characterBase = characterBasePtr.Value;
        var colorTableTextures = new Dictionary<int, IColorTableSet>();
        for (var i = 0; i < characterBase->ColorTableTexturesSpan.Length; i++)
        {
            var colorTableTex = characterBase->ColorTableTexturesSpan[i];
            if (colorTableTex == null) continue;

            var colorTableTexture = colorTableTex.Value;
            if (colorTableTexture != null)
            {
                var colorTableSet = ParseColorTableTexture(colorTableTexture);
                colorTableTextures[i] = colorTableSet;
            }
        }

        return colorTableTextures;
    }
    
    public static unsafe IColorTableSet ParseColorTableTexture(Texture* colorTableTexture)
    {
        var (colorTableRes, stride) = DxHelper.ExportTextureResource(colorTableTexture);
        if ((TexFile.TextureFormat)colorTableTexture->TextureFormat != TexFile.TextureFormat.R16G16B16A16F)
        {
            throw new ArgumentException(
                $"Color table is not R16G16B16A16F ({(TexFile.TextureFormat)colorTableTexture->TextureFormat})");
        }

        if (colorTableTexture->ActualWidth == 4 && colorTableTexture->ActualHeight == 16)
        {
            // legacy table
            var stridedData = ImageUtils.AdjustStride((int)stride, (int)colorTableTexture->ActualWidth * 8,
                                                      (int)colorTableTexture->ActualHeight, colorTableRes.Data);
            var reader = new SpanBinaryReader(stridedData);
            return new LegacyColorTableSet
            {
                ColorTable = new LegacyColorTable(ref reader)
            };
        }

        if (colorTableTexture->ActualWidth == 8 && colorTableTexture->ActualHeight == 32)
        {
            // new table
            var stridedData = ImageUtils.AdjustStride((int)stride, (int)colorTableTexture->ActualWidth * 8,
                                                      (int)colorTableTexture->ActualHeight, colorTableRes.Data);
            var reader = new SpanBinaryReader(stridedData);
            return new ColorTableSet
            {
                ColorTable = new ColorTable(ref reader)
            };
        }

        throw new ArgumentException(
            $"Color table is not 4x16 or 8x32 ({colorTableTexture->ActualWidth}x{colorTableTexture->ActualHeight})");
    }

    public static unsafe EquipmentModelId? GetEquipmentModelId(Pointer<CharacterBase> characterBasePtr, HumanEquipmentSlotIndex slotIdx)
    {
        if (characterBasePtr == null || characterBasePtr.Value == null)
        {
            return null;
        }
        
        var characterBase = characterBasePtr.Value;
        if (!Enum.IsDefined(slotIdx))
        {
            return null;
        }
        
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human)
        {
            return null;
        }
        var human = (Human*)characterBase;
        var equipId = (&human->Head)[(int)slotIdx];
        return equipId;
    }
}
