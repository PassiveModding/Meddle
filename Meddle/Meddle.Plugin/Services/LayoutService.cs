using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Terrain;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Microsoft.Extensions.Logging;
using CustomizeParameter = Meddle.Plugin.Models.Structs.CustomizeParameter;
using HousingFurniture = FFXIVClientStructs.FFXIV.Client.Game.HousingFurniture;
using Model = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model;
using Transform = Meddle.Plugin.Models.Transform;

namespace Meddle.Plugin.Services;

public class LayoutService : IService
{
    private readonly Dictionary<uint, Item> itemDict;
    private readonly ILogger<HousingService> logger;
    private readonly IDataManager dataManager;
    private readonly SqPack pack;
    private readonly ParseService parseService;
    private readonly PbdHooks pbdHooks;
    private readonly SigUtil sigUtil;
    private readonly Dictionary<uint, Stain> stainDict;

    public LayoutService(
        SigUtil sigUtil, ILogger<HousingService> logger,
        IDataManager dataManager,
        SqPack pack,
        ParseService parseService, PbdHooks pbdHooks)
    {
        this.sigUtil = sigUtil;
        this.logger = logger;
        this.dataManager = dataManager;
        this.pack = pack;
        this.parseService = parseService;
        this.pbdHooks = pbdHooks;
        stainDict = dataManager.GetExcelSheet<Stain>()!.ToDictionary(row => row.RowId, row => row);
        itemDict = dataManager.GetExcelSheet<Item>()!
                              .Where(item => item.AdditionalData != 0 && item.ItemSearchCategory.Row is 65 or 66)
                              .ToDictionary(row => row.AdditionalData, row => row);
    }

    public void ResolveInstances(ParsedInstance[] instances)
    {
        foreach (var instance in instances)
        {
            ResolveInstance(instance);
        }
    }

    public unsafe void ResolveInstance(ParsedInstance instance)
    {
        if (instance is ParsedCharacterInstance {CharacterInfo: null} characterInstance)
        {
            var objects = ParseObjects();
            // check to ensure the character instance is still valid
            if (objects.Any(o => o.Id == instance.Id))
            {
                var gameObject = (GameObject*)instance.Id;
                
                var characterInfo = HandleDrawObject(gameObject->DrawObject);
                characterInstance.CharacterInfo = characterInfo;
            }
        }

        if (instance is ParsedSharedInstance sharedInstance)
        {
            foreach (var child in sharedInstance.Children)
            {
                ResolveInstance(child);
            }
        }
    }

    public unsafe ParsedInstance[]? GetWorldState()
    {
        var layoutWorld = sigUtil.GetLayoutWorld();
        if (layoutWorld == null)
            return null;

        var currentTerritory = GetCurrentTerritory();
        var housingItems = ParseTerritory(currentTerritory);
        var objects = ParseObjects();
        var parseCtx = new ParseCtx(housingItems);
        var loadedLayouts = layoutWorld->LoadedLayouts.ToArray();
        var loadedLayers = loadedLayouts
                           .Select(layout => Parse(layout.Value, parseCtx))
                           .SelectMany(x => x).ToArray();
        var globalLayers = Parse(layoutWorld->GlobalLayout, parseCtx);

        var layers = new List<ParsedInstance>();

        layers.AddRange(loadedLayers.SelectMany(x => x.Instances));
        layers.AddRange(globalLayers.SelectMany(x => x.Instances));
        layers.AddRange(objects);

        return layers.ToArray();
    }

    private unsafe HousingTerritory* GetCurrentTerritory()
    {
        var housingManager = sigUtil.GetHousingManager();
        if (housingManager == null)
            return null;

        if (housingManager->CurrentTerritory == null)
            return null;

        return housingManager->CurrentTerritory;
    }

    private unsafe ParsedInstanceSet[] Parse(LayoutManager* activeLayout, ParseCtx ctx)
    {
        if (activeLayout == null) return [];

        var layers = new List<ParsedInstanceSet>();
        foreach (var (_, layerPtr) in activeLayout->Layers)
        {
            var layer = ParseLayer(layerPtr, ctx);
            if (layer != null)
            {
                layers.Add(layer);
            }
        }

        foreach (var (_, terrainPtr) in activeLayout->Terrains)
        {
            var terrain = ParseTerrain(terrainPtr, ctx);
            if (terrain != null)
            {
                layers.Add(new ParsedInstanceSet
                {
                    Instances = [terrain]
                });
            }
        }

        return layers.ToArray();
    }

    private unsafe ParsedTerrainInstance? ParseTerrain(Pointer<TerrainManager> terrainPtr, ParseCtx ctx)
    {
        if (terrainPtr == null || terrainPtr.Value == null)
            return null;
        
        var terrainManager = terrainPtr.Value;
        var path = terrainManager->PathString;
        return new ParsedTerrainInstance((nint)terrainManager, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), path);
    }

    private unsafe ParsedInstanceSet? ParseLayer(Pointer<LayerManager> layerManagerPtr, ParseCtx ctx)
    {
        if (layerManagerPtr == null || layerManagerPtr.Value == null)
            return null;

        var layerManager = layerManagerPtr.Value;
        var instances = new List<ParsedInstance>();
        foreach (var (_, instancePtr) in layerManager->Instances)
        {
            if (instancePtr == null || instancePtr.Value == null)
                continue;

            var instance = ParseInstance(instancePtr, ctx);
            if (instance == null)
                continue;

            instances.Add(instance);
        }

        return new ParsedInstanceSet
        {
            Instances = instances
        };
    }

    private unsafe ParsedInstance? ParseInstance(Pointer<ILayoutInstance> instancePtr, ParseCtx ctx)
    {
        if (instancePtr == null || instancePtr.Value == null)
            return null;

        var instanceLayout = instancePtr.Value;
        switch (instanceLayout->Id.Type)
        {
            case InstanceType.BgPart:
            {
                var bgPart = (BgPartsLayoutInstance*)instanceLayout;
                var part = ParseBgPart(bgPart);
                return part;
            }
            case InstanceType.SharedGroup:
            {
                var sharedGroup = (SharedGroupLayoutInstance*)instanceLayout;
                var part = ParseSharedGroup(sharedGroup, ctx);
                return part;
            }
            case InstanceType.Light:
            {
                var light = ParsedLightInstance(instanceLayout);
                return light;
            }
            default:
            {
                var primaryPath = instanceLayout->GetPrimaryPath();
                string? path = null;
                if (primaryPath != null)
                {
                    path = SpanMemoryUtils.GetStringFromNullTerminated(primaryPath);
                }

                return new ParsedUnsupportedInstance((nint)instanceLayout, 
                                                     instanceLayout->Id.Type,
                                                     new Transform(*instanceLayout->GetTransformImpl()), 
                                                     path);
            }
        }
    }
    
    private unsafe ParsedLightInstance? ParsedLightInstance(Pointer<ILayoutInstance> lightPtr)
    {
        if (lightPtr == null || lightPtr.Value == null)
            return null;

        var light = lightPtr.Value;
        if (light->Id.Type != InstanceType.Light)
            return null;
        
        var typedInstance = (LightLayoutInstance*)light;
        var color = typedInstance->LightPtr->LightItem->Color;

        return new ParsedLightInstance((nint)light, new Transform(*light->GetTransformImpl()), color);
    }

    private unsafe ParsedInstance? ParseSharedGroup(Pointer<SharedGroupLayoutInstance> sharedGroupPtr, ParseCtx ctx)
    {
        if (sharedGroupPtr == null || sharedGroupPtr.Value == null)
            return null;

        var sharedGroup = sharedGroupPtr.Value;
        if (sharedGroup->Id.Type != InstanceType.SharedGroup)
            return null;

        var children = new List<ParsedInstance>();
        foreach (var instanceDataPtr in sharedGroup->Instances.Instances)
        {
            if (instanceDataPtr == null || instanceDataPtr.Value == null)
                continue;

            var instanceData = instanceDataPtr.Value;
            var child = ParseInstance(instanceData->Instance, ctx);
            if (child == null)
                continue;
            children.Add(child);
        }

        if (children.Count == 0)
            return null;


        var primaryPath = sharedGroup->GetPrimaryPath();
        string? path = null;
        if (primaryPath != null)
        {
            path = SpanMemoryUtils.GetStringFromNullTerminated(primaryPath);
        }
        else
        {
            throw new Exception("SharedGroup has no primary path");
        }

        var furnitureMatch = ctx.HousingItems.FirstOrDefault(item => item.LayoutInstance == sharedGroupPtr);
        if (furnitureMatch is not null)
        {
            // TODO: Kinda messy
            var stain = stainDict.GetValueOrDefault(furnitureMatch.HousingFurniture.Stain);
            var item = itemDict.GetValueOrDefault(furnitureMatch.HousingFurniture.Id);
            
            var housing = new ParsedHousingInstance((nint)sharedGroup, new Transform(*sharedGroup->GetTransformImpl()), path,
                                             furnitureMatch.GameObject->NameString,
                                             furnitureMatch.GameObject->ObjectKind,
                                             stain,
                                             item, children);
            foreach (var child in housing.Flatten())
            {
                if (child is ParsedBgPartsInstance parsedBgPartsInstance)
                {
                    parsedBgPartsInstance.StainColor = stain?.Color != null ? ImGui.ColorConvertU32ToFloat4(stain.Color) : null;
                }
            }
            
            return housing;
        }

        return new ParsedSharedInstance((nint)sharedGroup, new Transform(*sharedGroup->GetTransformImpl()), path, children);
    }

    private unsafe ParsedInstance? ParseBgPart(Pointer<BgPartsLayoutInstance> bgPartPtr)
    {
        if (bgPartPtr == null || bgPartPtr.Value == null)
            return null;

        var bgPart = bgPartPtr.Value;
        if (bgPart->Id.Type != InstanceType.BgPart)
            return null;

        var graphics = (BgObject*)bgPart->GraphicsObject;
        if (graphics == null)
            return null;

        var primaryPath = bgPart->GetPrimaryPath();
        string? path = null;
        if (primaryPath != null)
        {
            path = SpanMemoryUtils.GetStringFromNullTerminated(primaryPath);
        }
        else
        {
            throw new Exception("BgPart has no primary path");
        }

        return new ParsedBgPartsInstance((nint)bgPartPtr.Value, new Transform(*bgPart->GetTransformImpl()), path);
    }

    public unsafe ParsedInstance[] ParseObjects(bool resolveCharacterInfo = false)
    {
        var gameObjectManager = sigUtil.GetGameObjectManager();

        var objects = new List<ParsedInstance>();
        foreach (var objectPtr in gameObjectManager->Objects.GameObjectIdSorted)
        {
            if (objectPtr == null || objectPtr.Value == null)
                continue;

            var obj = objectPtr.Value;
            if (objects.Any(o => o.Id == (nint)obj))
                continue;

            var type = obj->GetObjectKind();
            var drawObject = obj->DrawObject;
            if (drawObject == null)
                continue;

            ParsedCharacterInfo? characterInfo = null;
            if (resolveCharacterInfo)
            {
                characterInfo = HandleDrawObject(drawObject);
            }

            var transform = new Transform(drawObject->Position, drawObject->Rotation, drawObject->Scale);
            objects.Add(
                new ParsedCharacterInstance((nint)obj, obj->NameString, type, transform, drawObject->IsVisible)
                {
                    CharacterInfo = characterInfo
                });
        }

        return objects.ToArray();
    }

    public unsafe ParsedModelInfo? HandleModel(Pointer<CharacterBase> cbasePtr, Pointer<Model> modelPtr, Dictionary<int, IColorTableSet> colorTableSets)
    {
        if (modelPtr == null) return null;
        var model = modelPtr.Value;
        if (model == null) return null;
        var modelPath = model->ModelResourceHandle->ResourceHandle.FileName.ParseString();
        if (cbasePtr == null) return null;
        if (cbasePtr.Value == null) return null;
        var characterBase = cbasePtr.Value;
        var modelPathFromCharacter = characterBase->ResolveMdlPath(model->SlotIndex);
        var shapeAttributeGroup = StructExtensions.ParseModelShapeAttributes(model);

        var materials = new List<ParsedMaterialInfo>();
        for (var mtrlIdx = 0; mtrlIdx < model->MaterialsSpan.Length; mtrlIdx++)
        {
            var materialPtr = model->MaterialsSpan[mtrlIdx];
            if (materialPtr == null) continue;
            var material = materialPtr.Value;
            if (material == null) continue;

            var materialPath = material->MaterialResourceHandle->ResourceHandle.FileName.ParseString();
            var materialPathFromModel =
                model->ModelResourceHandle->GetMaterialFileNameBySlotAsString((uint)mtrlIdx);
            var shaderName = material->MaterialResourceHandle->ShpkNameString;
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
                    var texturePathFromMaterial = material->MaterialResourceHandle->TexturePathString(texIdx);
                    var (resource, stride) =
                        DXHelper.ExportTextureResource(texturePtr.TextureResourceHandle->Texture);
                    var textureInfo = new ParsedTextureInfo(texturePath, texturePathFromMaterial, resource);
                    textures.Add(textureInfo);
                }
            }

            var materialInfo =
                new ParsedMaterialInfo(materialPath, materialPathFromModel, shaderName, colorTable, textures);
            materials.Add(materialInfo);
        }

        var deform = pbdHooks.TryGetDeformer((nint)cbasePtr.Value, model->SlotIndex);
        var modelInfo =
            new ParsedModelInfo((nint)model, modelPath, modelPathFromCharacter, deform, shapeAttributeGroup, materials);
        
            return modelInfo;
    }
    
    public unsafe ParsedCharacterInfo? HandleDrawObject(DrawObject* drawObject)
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
            var modelInfo = HandleModel(characterBase, modelPtr, colorTableTextures);
            if (modelInfo != null)
                models.Add(modelInfo);
        }

        var skeleton = StructExtensions.GetParsedSkeleton(characterBase);
        var (customizeParams, customizeData, genderRace) = ParseHuman(characterBase);

        return new ParsedCharacterInfo(models, skeleton, customizeData, customizeParams, genderRace);
    }

    public unsafe (Meddle.Utils.Export.CustomizeParameter, CustomizeData, GenderRace) ParseHuman(CharacterBase* characterBase)
    {
        var modelType = characterBase->GetModelType();
        if (modelType != CharacterBase.ModelType.Human)
        {
            return (new Meddle.Utils.Export.CustomizeParameter(), new CustomizeData(), GenderRace.Unknown);
        }
        
        var human = (Human*)characterBase;
        var customizeCBuf = human->CustomizeParameterCBuffer->TryGetBuffer<CustomizeParameter>()[0];
        var customizeParams = new Meddle.Utils.Export.CustomizeParameter
        {
            SkinColor = customizeCBuf.SkinColor,
            MuscleTone = customizeCBuf.MuscleTone,
            SkinFresnelValue0 = customizeCBuf.SkinFresnelValue0,
            LipColor = customizeCBuf.LipColor,
            MainColor = customizeCBuf.MainColor,
            FacePaintUVMultiplier = customizeCBuf.FacePaintUVMultiplier,
            HairFresnelValue0 = customizeCBuf.HairFresnelValue0,
            MeshColor = customizeCBuf.MeshColor,
            FacePaintUVOffset = customizeCBuf.FacePaintUVOffset,
            LeftColor = customizeCBuf.LeftColor,
            RightColor = customizeCBuf.RightColor,
            OptionColor = customizeCBuf.OptionColor
        };
        var customizeData = new CustomizeData
        {
            LipStick = human->Customize.Lipstick,
            Highlights = human->Customize.Highlights
        };
        var genderRace = (GenderRace)human->RaceSexId;
        
        return (customizeParams, customizeData, genderRace);
    }

    private unsafe Furniture[] ParseTerritory(HousingTerritory* territory)
    {
        if (territory == null)
            return [];
        var type = territory->GetTerritoryType();
        var furniture = type switch
        {
            HousingTerritoryType.Indoor => ((IndoorTerritory*)territory)->Furniture,
            HousingTerritoryType.Outdoor => ((OutdoorTerritory*)territory)->Furniture,
            _ => null
        };
        var objectManager = type switch
        {
            HousingTerritoryType.Indoor => &((IndoorTerritory*)territory)->HousingObjectManager,
            HousingTerritoryType.Outdoor => &((OutdoorTerritory*)territory)->HousingObjectManager,
            _ => null
        };

        if (furniture == null || objectManager == null)
            return [];

        var items = new List<Furniture>();
        for (var i = 0; i < furniture.Length; i++)
        {
            var item = furniture[i];
            var index = item.Index;
            if (item.Index == -1) continue;
            var objectPtr = objectManager->Objects[index];
            if (objectPtr == null || objectPtr.Value == null || objectPtr.Value->LayoutInstance == null)
            {
                continue;
            }

            var layoutInstance = objectPtr.Value->LayoutInstance;
            items.Add(new Furniture
            {
                GameObject = objectPtr,
                LayoutInstance = layoutInstance,
                HousingFurniture = item,
                Stain = stainDict.GetValueOrDefault(item.Stain),
                Item = itemDict.GetValueOrDefault(item.Id)
            });
        }

        return items.ToArray();
    }

    private class ParseCtx
    {
        public readonly Furniture[] HousingItems;

        public ParseCtx(Furniture[] housingItems)
        {
            HousingItems = housingItems;
        }
    }

    public unsafe class Furniture
    {
        public GameObject* GameObject;
        public HousingFurniture HousingFurniture;
        public Item? Item;
        public ILayoutInstance* LayoutInstance;
        public Stain? Stain;
    }
}
