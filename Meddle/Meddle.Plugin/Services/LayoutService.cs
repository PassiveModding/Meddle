using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using FFXIVClientStructs.Interop;
using Lumina.Excel.GeneratedSheets;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;
using CustomizeParameter = Meddle.Plugin.Models.Structs.CustomizeParameter;
using HousingFurniture = FFXIVClientStructs.FFXIV.Client.Game.HousingFurniture;
using Material = Meddle.Utils.Export.Material;
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

    public ParsedInstance[] ResolveInstances(ParsedInstance[] instances)
    {
        foreach (var instance in instances)
        {
            ResolveInstance(instance);
        }

        return instances;
    }

    public unsafe ParsedInstance ResolveInstance(ParsedInstance instance)
    {
        if (instance is ParsedCharacterInstance characterInstance)
        {
            if (characterInstance.CharacterInfo == null)
            {
                var gameObject = (GameObject*)instance.Id;
                var characterInfo = HandleDrawObject(gameObject->DrawObject);
                characterInstance.CharacterInfo = characterInfo;
            }
        }

        foreach (var child in instance.Children)
        {
            ResolveInstance(child);
        }

        return instance;
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
        //var activeLayers = Parse(layoutWorld->ActiveLayout, parseCtx);
        var loadedLayers = layoutWorld->LoadedLayouts.Select(layout => Parse(layout.Value, parseCtx)).SelectMany(x => x)
                                                     .ToArray();
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

    private unsafe ParsedLayer[] Parse(LayoutManager* activeLayout, ParseCtx ctx)
    {
        if (activeLayout == null) return [];

        var layers = new List<ParsedLayer>();
        foreach (var (_, layerPtr) in activeLayout->Layers)
        {
            var layer = ParseLayer(layerPtr, ctx);
            if (layer != null)
            {
                layers.Add(layer);
            }
        }

        return layers.ToArray();
    }

    private unsafe ParsedLayer? ParseLayer(Pointer<LayerManager> layerManagerPtr, ParseCtx ctx)
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

        return new ParsedLayer
        {
            Id = layerManager->LayerGroupId,
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
                var part = ParseBgPart(bgPart, ctx);
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
                var primaryPath = instanceLayout->GetPrimaryPath();
                string? path = null;
                if (primaryPath != null)
                {
                    path = SpanMemoryUtils.GetStringFromNullTerminated(primaryPath);
                }

                return new ParsedLightInstance
                {
                    Id = (nint)instanceLayout,
                    Transform = new Transform(*instanceLayout->GetTransformImpl()),
                    Path = path
                };
            }
            default:
            {
                var primaryPath = instanceLayout->GetPrimaryPath();
                string? path = null;
                if (primaryPath != null)
                {
                    path = SpanMemoryUtils.GetStringFromNullTerminated(primaryPath);
                }

                return new ParsedUnsupportedInstance(ParsedInstanceType.Unsupported, instanceLayout->Id.Type)
                {
                    Id = (nint)instanceLayout,
                    Transform = new Transform(*instanceLayout->GetTransformImpl()),
                    Path = path
                };
            }
        }
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

        var furnitureMatch = ctx.HousingItems.FirstOrDefault(item => item.LayoutInstance == sharedGroupPtr);
        if (furnitureMatch is not null)
        {
            return new ParsedHousingInstance
            {
                Id = (nint)sharedGroup,
                Transform = new Transform(*sharedGroup->GetTransformImpl()),
                Children = children,
                Stain = stainDict.GetValueOrDefault(furnitureMatch.HousingFurniture.Stain),
                Item = itemDict.GetValueOrDefault(furnitureMatch.HousingFurniture.Id),
                Name = furnitureMatch.GameObject->NameString,
                Kind = furnitureMatch.GameObject->ObjectKind,
                Path = path
            };
        }

        return new ParsedSharedInstance
        {
            Id = (nint)sharedGroup,
            Transform = new Transform(*sharedGroup->GetTransformImpl()),
            Children = children,
            Path = path
        };
    }

    private unsafe ParsedInstance? ParseBgPart(Pointer<BgPartsLayoutInstance> bgPartPtr, ParseCtx ctx)
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

        return new ParsedBgPartsInstance
        {
            Id = (nint)bgPartPtr.Value,
            Transform = new Transform(*bgPart->GetTransformImpl()),
            Path = path
        };
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

            objects.Add(new ParsedCharacterInstance
            {
                Id = (nint)obj,
                Kind = type,
                Name = obj->NameString,
                Transform = new Transform(drawObject->Position, drawObject->Rotation, drawObject->Scale),
                CharacterInfo = characterInfo,
                Visible = drawObject->IsVisible
            });
        }

        return objects.ToArray();
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
            if (modelPtr == null) continue;
            var model = modelPtr.Value;
            if (model == null) continue;
            var modelPath = model->ModelResourceHandle->ResourceHandle.FileName.ParseString();
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
                ColorTable? colorTable = null;
                if (colorTableTextures.TryGetValue((int)(model->SlotIndex * CharacterBase.MaterialsPerSlot) + mtrlIdx,
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
                    colorTable = ColorTable.Load(ref reader);
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

            var deform = pbdHooks.TryGetDeformer((nint)characterBase, model->SlotIndex);
            var modelInfo =
                new ParsedModelInfo(modelPath, modelPathFromCharacter, deform, shapeAttributeGroup, materials);
            models.Add(modelInfo);
        }

        var skeleton = StructExtensions.GetParsedSkeleton(characterBase);
        var modelType = characterBase->GetModelType();
        CustomizeData customizeData = new CustomizeData();
        Meddle.Utils.Export.CustomizeParameter customizeParams = new();
        GenderRace genderRace = GenderRace.Unknown;
        if (modelType == CharacterBase.ModelType.Human)
        {
            var human = (Human*)characterBase;
            var customizeCBuf = human->CustomizeParameterCBuffer->TryGetBuffer<CustomizeParameter>()[0];
            customizeParams = new Meddle.Utils.Export.CustomizeParameter
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
            customizeData = new CustomizeData
            {
                LipStick = human->Customize.Lipstick,
                Highlights = human->Customize.Highlights
            };
            genderRace = (GenderRace)human->RaceSexId;
        }

        return new ParsedCharacterInfo
        {
            Models = models,
            Skeleton = skeleton,
            CustomizeData = customizeData,
            CustomizeParameter = customizeParams,
            GenderRace = genderRace
        };
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
