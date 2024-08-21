using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using FFXIVClientStructs.Interop;
using Lumina.Excel.GeneratedSheets;
using Meddle.Plugin.Models.Structs;
using Microsoft.Extensions.Logging;
using HousingFurniture = FFXIVClientStructs.FFXIV.Client.Game.HousingFurniture;

namespace Meddle.Plugin.Services;

public class HousingService : IService
{
    private readonly SigUtil sigUtil;
    private readonly ILogger<HousingService> logger;
    private readonly Dictionary<uint, Stain> stainDict;
    private readonly Dictionary<uint, Item> itemDict;

    public HousingService(SigUtil sigUtil, ILogger<HousingService> logger, IDataManager dataManager)
    {
        this.sigUtil = sigUtil;
        this.logger = logger;
        this.stainDict = dataManager.GetExcelSheet<Stain>()!.ToDictionary(row => row.RowId, row => row);
        this.itemDict = dataManager.GetExcelSheet<Item>()!
                                   .Where(item => item.AdditionalData != 0 && item.ItemSearchCategory.Row is 65 or 66)
                                   .ToDictionary(row => row.AdditionalData, row => row);
    }
    
    public unsafe Dictionary<nint, HousingItem> GetHousingItems()
    {
        var housingManager = sigUtil.GetHousingManager();
        if (housingManager == null)
            return [];
        
        if (housingManager->CurrentTerritory == null)
            return [];
        
        var territoryType = housingManager->CurrentTerritory->GetTerritoryType();
        switch (territoryType)
        {
            case HousingTerritoryType.Indoor:
            {
                var indoorTerritory = housingManager->IndoorTerritory;
                if (indoorTerritory == null)
                {
                    logger.LogWarning("Indoor territory is null");
                    return [];
                }
            
                var items = new Dictionary<nint, HousingItem>();
                for (int i = 0; i < indoorTerritory->Furniture.Length; i++)
                {
                    var furniture = indoorTerritory->Furniture[i];
                    var index = furniture.Index;
                    if (furniture.Index == -1) continue;
                    try
                    {
                        var objectPtr = indoorTerritory->HousingObjectManager.Objects[index];
                        if (objectPtr == null || objectPtr.Value == null)
                        {
                            continue;
                        }

                        if (objectPtr.Value->LayoutInstance == null)
                        {
                            logger.LogWarning("LayoutInstance is null for object at index {Index}", index);
                            continue;
                        }
                        
                        var layoutInstance = objectPtr.Value->LayoutInstance;
                        var bgParts = ParseBgObjectsFromInstance(layoutInstance);

                        var item = new HousingItem
                        {
                            Furniture = furniture,
                            Object = objectPtr,
                            Stain = stainDict.GetValueOrDefault(furniture.Stain),
                            Item = itemDict.GetValueOrDefault(furniture.Id),
                            BgParts = bgParts
                        };
                        items[(nint)objectPtr.Value] = item;
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Error getting object at index {Index}", index);
                    }
                }
            
                return items;
            }
            case HousingTerritoryType.Outdoor:
            {
                var outdoorTerritory = housingManager->OutdoorTerritory;
                if (outdoorTerritory == null)
                    return [];
            
                var items = new Dictionary<nint, HousingItem>();
                for (int i = 0; i < outdoorTerritory->Furniture.Length; i++)
                {
                    var furniture = outdoorTerritory->Furniture[i];
                    var index = furniture.Index;
                    if (furniture.Index == -1) continue;
                    try
                    {
                        var objectPtr = outdoorTerritory->HousingObjectManager.Objects[index];
                        if (objectPtr == null || objectPtr.Value == null)
                        {
                            continue;
                        }

                        if (objectPtr.Value->LayoutInstance == null)
                        {
                            logger.LogWarning("LayoutInstance is null for object at index {Index}", index);
                            continue;
                        }
                        
                        var layoutInstance = objectPtr.Value->LayoutInstance;
                        var bgParts = ParseBgObjectsFromInstance(layoutInstance);
                    
                        var item = new HousingItem
                        {
                            Furniture = furniture,
                            Object = objectPtr,
                            Stain = stainDict.GetValueOrDefault(furniture.Stain),
                            Item = itemDict.GetValueOrDefault(furniture.Id),
                            BgParts = bgParts
                        };
                        items[(nint)objectPtr.Value] = item;
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Error getting object at index {Index}", index);
                    }
                }
                return items;
            }
            case HousingTerritoryType.Workshop:
                // nothing to do here really, no placeable items
                return [];
            default:
                logger.LogWarning("Unknown territory type {TerritoryType}", territoryType);
                return [];
        }
    }

    private unsafe List<Pointer<BgObject>> ParseBgObjectsFromInstance(ILayoutInstance* instance)
    {
        var bgParts = ParseBgPartsFromInstance(instance);
        var bgObjects = new List<Pointer<BgObject>>();
        foreach (var bgPart in bgParts)
        {
            if (bgPart == null || bgPart.Value == null)
            {
                continue;
            }
            
            if (bgPart.Value->GraphicsObject == null)
            {
                continue;
            }

            var bgObject = (BgObject*)bgPart.Value->GraphicsObject;
            if (bgObject == null)
            {
                continue;
            }
            
            bgObjects.Add(bgObject);
        }
        
        return bgObjects;
    }
    
    private unsafe List<Pointer<BgPartsLayoutInstance>> ParseBgPartsFromInstance(ILayoutInstance* instance)
    {
        var bgParts = new List<Pointer<BgPartsLayoutInstance>>();
        if (instance->Id.Type == InstanceType.BgPart)
        {
            bgParts.Add((BgPartsLayoutInstance*)instance);
        }
        else if (instance->Id.Type == InstanceType.SharedGroup)
        {
            var sharedGroup = (SharedGroupLayoutInstance*)instance;
            foreach (var instanceDataPtr in sharedGroup->Instances.Instances)
            {
                if (instanceDataPtr == null)
                {
                    continue;
                }
                
                var instanceData = instanceDataPtr.Value;
                if (instanceData->Instance->Id.Type == InstanceType.BgPart)
                {
                    bgParts.Add((BgPartsLayoutInstance*)instanceData->Instance);
                }
                else if (instanceData->Instance->Id.Type == InstanceType.SharedGroup)
                {
                    var nestedBgParts = ParseBgPartsFromInstance(instanceData->Instance);
                    bgParts.AddRange(nestedBgParts);
                }
            }
        }
        return bgParts;
    }

    public struct HousingItem
    {
        public HousingFurniture Furniture;
        public Stain? Stain;
        public Item? Item;
        public unsafe GameObject* Object;
        public List<Pointer<BgObject>> BgParts;
    }
}
