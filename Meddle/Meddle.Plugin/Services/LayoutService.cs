using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using FFXIVClientStructs.Interop;
using Lumina.Excel.GeneratedSheets;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;
using HousingFurniture = FFXIVClientStructs.FFXIV.Client.Game.HousingFurniture;

namespace Meddle.Plugin.Services;

public class LayoutService : IService
{
    private readonly SigUtil sigUtil;
    private readonly ILogger<HousingService> logger;
    private readonly Dictionary<uint, Stain> stainDict;
    private readonly Dictionary<uint, Item> itemDict;

    public LayoutService(SigUtil sigUtil, ILogger<HousingService> logger, IDataManager dataManager)
    {
        this.sigUtil = sigUtil;
        this.logger = logger;
        this.stainDict = dataManager.GetExcelSheet<Stain>()!.ToDictionary(row => row.RowId, row => row);
        this.itemDict = dataManager.GetExcelSheet<Item>()!
                                   .Where(item => item.AdditionalData != 0 && item.ItemSearchCategory.Row is 65 or 66)
                                   .ToDictionary(row => row.AdditionalData, row => row);
    }

    private class ParseCtx
    {
        public ParseCtx(Furniture[] housingItems)
        {
            HousingItems = housingItems;
        }
        
        public Furniture[] HousingItems;
    }

    public unsafe ParsedLayer[]? GetWorldState()
    {
        var layoutWorld = sigUtil.GetLayoutWorld();
        if (layoutWorld == null)
            return null;

        var currentTerritory = GetCurrentTerritory();
        var housingItems = ParseTerritory(currentTerritory);
        var parseCtx = new ParseCtx(housingItems);
        //var activeLayers = Parse(layoutWorld->ActiveLayout, parseCtx);
        var loadedLayers = layoutWorld->LoadedLayouts.Select(layout => Parse(layout.Value, parseCtx)).SelectMany(x => x).ToArray();
        var globalLayers = Parse(layoutWorld->GlobalLayout, parseCtx);
        
        var layers = new List<ParsedLayer>();
        
        layers.AddRange(loadedLayers);
        layers.AddRange(globalLayers);
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
                    
                    return new ParsedLightInstance()
                    {
                        Id = (nint)instanceLayout,
                        Transform = *instanceLayout->GetTransformImpl(),
                    };
                }
                default:
                {
                    return new ParsedUnsupportedInstance(instanceLayout->Id.Type)
                    {
                        Id = (nint)instanceLayout,
                        Transform = *instanceLayout->GetTransformImpl(),
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

            var furnitureMatch = ctx.HousingItems.FirstOrDefault(item => item.LayoutInstance == sharedGroupPtr);
            if (furnitureMatch is not null)
            {
                return new ParsedHousingInstance
                {
                    Id = (nint)sharedGroup,
                    Transform = *sharedGroup->GetTransformImpl(),
                    Children = children,
                    Stain = stainDict.GetValueOrDefault(furnitureMatch.HousingFurniture.Stain),
                    Item = itemDict.GetValueOrDefault(furnitureMatch.HousingFurniture.Id),
                    Name = furnitureMatch.GameObject->NameString,
                    Kind = furnitureMatch.GameObject->ObjectKind
                };
            }
            else
            {
                return new ParsedSharedInstance
                {
                    Id = (nint)sharedGroup,
                    Transform = *sharedGroup->GetTransformImpl(),
                    Children = children
                };
            }
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

            return new ParsedBgPartsInstance
            {
                Id = (nint)bgPartPtr.Value,
                Transform = *bgPart->GetTransformImpl(),
                Path = graphics->ModelResourceHandle->ResourceHandle.FileName.ParseString()
            };
        }
    
    public unsafe class Furniture
    {
        public GameObject* GameObject;
        public ILayoutInstance* LayoutInstance;
        public HousingFurniture HousingFurniture;
        public Stain? Stain;
        public Item? Item;
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
        for (int i = 0; i < furniture.Length; i++)
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
}
