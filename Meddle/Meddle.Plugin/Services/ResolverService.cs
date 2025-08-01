﻿using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using CustomizeData = Meddle.Utils.Export.CustomizeData;
using CustomizeParameter = Meddle.Plugin.Models.Structs.CustomizeParameter;
using Model = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model;

namespace Meddle.Plugin.Services;

public class ResolverService : IService
{
    private readonly ILogger<ResolverService> logger;
    private readonly LayoutService layoutService;
    private readonly SqPack pack;
    private readonly ParseService parseService;
    private readonly IFramework framework;
    private readonly StainHooks stainHooks;
    private readonly PbdHooks pbdHooks;

    public ResolverService(
        ILogger<ResolverService> logger, 
        LayoutService layoutService,
        SqPack pack,
        ParseService parseService, 
        IFramework framework,
        StainHooks stainHooks,
        IDataManager dataManager,
        PbdHooks pbdHooks)
    {
        this.logger = logger;
        this.layoutService = layoutService;
        this.pack = pack;
        this.parseService = parseService;
        this.framework = framework;
        this.stainHooks = stainHooks;
        this.pbdHooks = pbdHooks;
    }
    
    
    public void ResolveInstances(params ParsedInstance[] instances)
    {
        framework.RunOnTick(() =>
        {
            foreach (var instance in instances)
            {
                ResolveInstance(instance);
            }
        }).GetAwaiter().GetResult();
    }
    
    public static bool IsCharacterKind(ObjectKind kind)
    {
        return kind switch
        {
            ObjectKind.Pc => true,
            ObjectKind.Mount => true,
            ObjectKind.Companion => true,
            ObjectKind.Retainer => true,
            ObjectKind.BattleNpc => true,
            ObjectKind.EventNpc => true,
            ObjectKind.Ornament => true,
            _ => false
        };
    }

    private unsafe void ResolveParsedCharacterInstance(ParsedCharacterInstance characterInstance)
    {
        var objects = layoutService.ParseObjects();
        // check to ensure the character instance is still valid
        if (objects.Any(o => o.Id == characterInstance.Id))
        {
            if (characterInstance.IdType == ParsedCharacterInstance.ParsedCharacterInstanceIdType.CharacterBase)
            {
                var cBase = (CharacterBase*)characterInstance.Id;
                var characterInfo = ParseDrawObject(&cBase->DrawObject);
                characterInstance.CharacterInfo = characterInfo;
            }
            else
            {
                var gameObject = (GameObject*)characterInstance.Id;
                if (IsCharacterKind(gameObject->ObjectKind))
                {
                    var characterInfo = ParseCharacter((Character*)gameObject);
                    characterInstance.CharacterInfo = characterInfo;
                }
                else
                {
                    var characterInfo = ParseDrawObject(gameObject->DrawObject);
                    characterInstance.CharacterInfo = characterInfo;
                }
            }
        }
        else
        {
            logger.LogWarning("Character instance {Id} no longer exists", characterInstance.Id);
        }
    }
    
    private void ResolveParsedTerrainInstance(ParsedTerrainInstance terrainInstance)
    {
        var path = terrainInstance.Path;
        var teraPath = $"{terrainInstance.Path.GamePath}/bgplate/terrain.tera";
        var teraResource = pack.GetFile(teraPath);
        
        if (teraResource == null)
        {
            logger.LogWarning("Failed to load terrain.tera for {Path}", path);
            return;
        }
        
        var terrain = new TeraFile(teraResource.Value.file.RawData);
        terrainInstance.Data = new ParsedTerrainInstanceData(terrain);
    }
    
    private void ResolveInstance(ParsedInstance instance)
    {
        if (instance is ParsedCharacterInstance {IsResolved: false} characterInstance)
        {
            ResolveParsedCharacterInstance(characterInstance);
        }

        if (instance is ParsedTerrainInstance {IsResolved: false} terrainInstance)
        {
            ResolveParsedTerrainInstance(terrainInstance);
        }

        if (instance is ParsedSharedInstance sharedInstance)
        {
            foreach (var child in sharedInstance.Children)
            {
                ResolveInstance(child);
            }
        }
    }
    
    public ParsedModelInfo? ParseModelFromPath(string path)
    {
        var modelResource = pack.GetFile(path);
        if (modelResource == null)
        {
            logger.LogWarning("Failed to load model file: {Path}", path);
            return null;
        }

        var modelData = modelResource.Value.file.RawData;
        var mdlFile = new MdlFile(modelData);
        var materials = new List<ParsedMaterialInfo>();
        var mtrlNames = mdlFile.GetMaterialNames().Select(x => x.Value)
                               .ToArray();
        foreach (var mtrlName in mtrlNames)
        {
            if (mtrlName.StartsWith('/')) throw new InvalidOperationException("Cannot resolve relative paths");
            
            var mtrlResource = pack.GetFile(mtrlName);
            if (mtrlResource == null)
            {
                logger.LogWarning("Failed to load material file: {Path}", mtrlName);
                continue;
            }
            
            var mtrlData = mtrlResource.Value.file.RawData;
            var mtrlFile = new MtrlFile(mtrlData);
            var shaderName = mtrlFile.GetShaderPackageName();
            var colorTable = mtrlFile.GetColorTable();
            
            var textures = new List<ParsedTextureInfo>();
            var textureNames = mtrlFile.GetTexturePaths().Select(x => x.Value)
                                       .ToArray();
            for (var texIdx = 0; texIdx < textureNames.Length; texIdx++)
            {
                var texName = textureNames[texIdx];
                var texResource = pack.GetFile(texName);
                if (texResource == null)
                {
                    logger.LogWarning("Failed to load texture file: {Path}", texName);
                    continue;
                }
                
                var texData = texResource.Value.file.RawData;
                var texFile = new TexFile(texData);
                var texRes = texFile.ToResource();
                var texInfo = new ParsedTextureInfo(texName, texName, texRes);
                textures.Add(texInfo);
            }

            var materialInfo = new ParsedMaterialInfo(mtrlName, mtrlName, shaderName, colorTable, textures.ToArray(), null, null);
            
            materials.Add(materialInfo);
        }

        var modelInfo = new ParsedModelInfo(path, path, null, null, materials.ToArray(), null, null);
        return modelInfo;
    }
    
    public unsafe ParsedModelInfo? ParseModel(Pointer<CharacterBase> characterBasePtr, Pointer<Model> modelPtr, 
                                              Dictionary<int, IColorTableSet> colorTableSets)
    {
        if (modelPtr == null) return null;
        var model = modelPtr.Value;
        if (model == null) return null;
        var modelPath = model->ModelResourceHandle->ResourceHandle.FileName.ParseString();
        if (characterBasePtr == null) return null;
        if (characterBasePtr.Value == null) return null;
        var characterBase = characterBasePtr.Value;
        var modelPathFromCharacter = characterBase->ResolveMdlPath(model->SlotIndex);
        var shapeAttributeGroup = StructExtensions.ParseModelShapeAttributes(model);

        var stain0 = stainHooks.GetStainFromCache((nint)characterBasePtr.Value, model->SlotIndex, 0);
        var stain1 = stainHooks.GetStainFromCache((nint)characterBasePtr.Value, model->SlotIndex, 1);
        
        var materials = new List<ParsedMaterialInfo?>();
        for (var mtrlIdx = 0; mtrlIdx < model->MaterialsSpan.Length; mtrlIdx++)
        {
            var materialPtr = model->MaterialsSpan[mtrlIdx];
            if (materialPtr == null || materialPtr.Value == null)
            {
                materials.Add(null);
                continue;
            }
            
            var material = materialPtr.Value;
            var materialPath = material->MaterialResourceHandle->ResourceHandle.FileName.ParseString();
            var materialPathFromModel =
                model->ModelResourceHandle->GetMaterialFileNameBySlot((uint)mtrlIdx);
            var shaderName = material->MaterialResourceHandle->ShpkName;
            IColorTableSet? colorTable = null;
            if (colorTableSets.TryGetValue((int)(model->SlotIndex * CharacterBase.MaterialsPerSlot) + mtrlIdx,
                                               out var gpuColorTable))
            {
                colorTable = gpuColorTable;
            }
            else if (material->MaterialResourceHandle->ColorTableSpan.Length == 32)
            {
                var colorTableRows = material->MaterialResourceHandle->ColorTableSpan;
                var colorTableBytes = MemoryMarshal.AsBytes(colorTableRows);
                var colorTableBuf = new byte[colorTableBytes.Length];
                colorTableBytes.CopyTo(colorTableBuf);
                var reader = new SpanBinaryReader(colorTableBuf);
                colorTable = new ColorTableSet
                {
                    ColorTable = new ColorTable(ref reader)
                };
            }

            var textures = new List<ParsedTextureInfo>();
            for (var texIdx = 0; texIdx < material->MaterialResourceHandle->TexturesSpan.Length; texIdx++)
            {
                var texturePtr = material->MaterialResourceHandle->TexturesSpan[texIdx];
                if (texturePtr.TextureResourceHandle == null) continue;

                var texturePath = texturePtr.TextureResourceHandle->FileName.ParseString();
                if (texIdx < material->TextureCount)
                {
                    var texturePathFromMaterial = material->MaterialResourceHandle->TexturePath(texIdx);
                    var (resource, _) =
                        DxHelper.ExportTextureResource(texturePtr.TextureResourceHandle->Texture);
                    var textureInfo = new ParsedTextureInfo(texturePath, texturePathFromMaterial, resource);
                    textures.Add(textureInfo);
                }
            }

            var materialInfo =
                new ParsedMaterialInfo(materialPath, materialPathFromModel, shaderName, colorTable, textures.ToArray(),
                                       stain0?.Stain, stain1?.Stain);
            materials.Add(materialInfo);
        }

        var deform = pbdHooks.TryGetDeformer((nint)characterBasePtr.Value, model->SlotIndex);
        var modelInfo =
            new ParsedModelInfo(modelPath, modelPathFromCharacter, deform, shapeAttributeGroup, materials.ToArray(),
                                stain0?.Stain, stain1?.Stain);
        
            return modelInfo;
    }
    
    public unsafe ParsedCharacterInfo? ParseCharacter(Character* character)
    {
        if (character == null)
        {
            return null;
        }
        
        var drawObject = character->DrawObject;
        if (drawObject == null)
        {
            return null;
        }
        
        var characterInfo = ParseDrawObject(drawObject);
        if (characterInfo == null)
        {
            return null;
        }
        
        var attaches = new List<ParsedCharacterInfo>();
        var mountInfo = ParseCharacter(character->Mount.MountObject);
        if (mountInfo != null)
        {
            attaches.Add(mountInfo);
        }
        
        var ornamentInfo = ParseCharacter((Character*)character->OrnamentData.OrnamentObject);
        if (ornamentInfo != null)
        {
            attaches.Add(ornamentInfo);
        }

        foreach (var weapon in character->DrawData.WeaponData)
        {
            var weaponInfo = ParseDrawObject(weapon.DrawObject);
            if (weaponInfo != null)
            {
                attaches.Add(weaponInfo);
            }
        }

        characterInfo.Attaches = attaches.ToArray();
        
        return characterInfo;
    }

    public unsafe ParsedModelInfo? ParseTerrainModelFromPointer(ModelResourceHandle* handle)
    {
        if (handle == null || handle->ModelData == null)
        {
            return null;
        }

        var modelData = new ModelResourceHandleData(handle->ModelData);
        var path = handle->ResourceHandle.FileName.ParseString();

        var materials = new List<ParsedMaterialInfo>();
        for (int i = 0; i < modelData.ModelHeader.MaterialCount; i++)
        {
            var material = handle->MaterialResourceHandles[i];
            if (material == null)
            {
                continue;
            }
            
            var materialPath = material->ResourceHandle.FileName.ParseString();
            var pathFromModel = handle->GetMaterialFileNameBySlot((uint)i);
            var shaderName = material->ShpkName;
            IColorTableSet? colorTable = null;
            // skip color table parsing for now
            var textures = new List<ParsedTextureInfo>();
            for (var texIdx = 0; texIdx < material->TexturesSpan.Length; texIdx++)
            {
                var texturePtr = material->TexturesSpan[texIdx];
                if (texturePtr.TextureResourceHandle == null) continue;

                var texturePath = texturePtr.TextureResourceHandle->FileName.ParseString();
                if (texIdx < material->TextureCount)
                {
                    var texturePathFromMaterial = material->TexturePath(texIdx);
                    var (resource, _) =
                        DxHelper.ExportTextureResource(texturePtr.TextureResourceHandle->Texture);
                    var textureInfo = new ParsedTextureInfo(texturePath, texturePathFromMaterial, resource);
                    textures.Add(textureInfo);
                }
            }
            
            var materialInfo = new ParsedMaterialInfo(materialPath, pathFromModel, shaderName, colorTable, textures.ToArray(), null, null);
            materials.Add(materialInfo);
        }
        
        var model = new ParsedModelInfo(
            path, 
            path, 
            null, // Deform is not available here
            null,
            materials.ToArray(), 
            null,
            null
        );
        
        return model;
    }
    
    public unsafe ParsedCharacterInfo? ParseDrawObject(DrawObject* drawObject)
    {
        if (drawObject == null)
        {
            return null;
        }

        var objectType = drawObject->Object.GetObjectType();
        if (objectType != ObjectType.CharacterBase)
        {
            return null;
        }

        var characterBase = (CharacterBase*)drawObject;
        var colorTableTextures = parseService.ParseColorTableTextures(characterBase);
        var models = new List<ParsedModelInfo>();
        foreach (var modelPtr in characterBase->ModelsSpan)
        {
            var modelInfo = ParseModel(characterBase, modelPtr, colorTableTextures);
            if (modelInfo != null)
                models.Add(modelInfo);
        }

        var skeleton = StructExtensions.GetParsedSkeleton(characterBase);
        var (customizeParams, customizeData, genderRace) = ParseHuman(characterBase);

        return new ParsedCharacterInfo(models.ToArray(), skeleton, StructExtensions.GetParsedAttach(characterBase), customizeData, customizeParams, genderRace);
    }

    public unsafe (Meddle.Utils.Export.CustomizeParameter customizeParameter, CustomizeData customizeData, GenderRace genderRace) ParseHuman(CharacterBase* characterBase)
    {
        var modelType = characterBase->GetModelType();
        if (modelType != CharacterBase.ModelType.Human)
        {
            return (new Meddle.Utils.Export.CustomizeParameter(), new CustomizeData(), GenderRace.Unknown);
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
            DecalPath = GetTexturePath(human->Decal),
            LegacyBodyDecalPath = GetTexturePath(human->LegacyBodyDecal),
            FacePaintReversed = human->Customize.FacePaintReversed,
        };
        var genderRace = (GenderRace)human->RaceSexId;
        
        return (customizeParams, customizeData, genderRace);
    }

    private unsafe string? GetTexturePath(Pointer<TextureResourceHandle> ptr)
    {
        if (ptr == null || ptr.Value == null)
        {
            return null;
        }

        var textureResourceHandle = ptr.Value;
        var texturePath = textureResourceHandle->FileName.ParseString();
        return texturePath;
    }
}
