using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Terrain;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;
using HousingFurniture = FFXIVClientStructs.FFXIV.Client.Game.HousingFurniture;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;
using Transform = Meddle.Plugin.Models.Transform;

namespace Meddle.Plugin.Services;

public class LayoutService : IService, IDisposable
{
    // private readonly Dictionary<uint, Item> itemDict;
    private readonly Dictionary<uint, Stain> stainDict;
    private readonly ILogger<LayoutService> logger;
    private readonly IFramework framework;
    private readonly Configuration config;
    private readonly SigUtil sigUtil;

    public LayoutService(
        SigUtil sigUtil, 
        ILogger<LayoutService> logger,
        IDataManager dataManager,
        IFramework framework,
        Configuration config)
    {
        this.sigUtil = sigUtil;
        this.logger = logger;
        this.framework = framework;
        this.config = config;
        stainDict = dataManager.GetExcelSheet<Stain>()!.ToDictionary(row => row.RowId, row => row);
        this.framework.Update += Update;
    }

    public bool RequestUpdate { get; set; }
    
    public ParsedInstance[]? LastState { get; private set; }
    
    private void Update(IFramework _)
    {
        if (RequestUpdate == false)
            return;
        
        var worldState = GetWorldState();
        LastState = worldState;
        RequestUpdate = false;
    }

    public unsafe ParsedInstance[]? GetWorldState()
    {
        var layoutWorld = sigUtil.GetLayoutWorld();
        if (layoutWorld == null)
            return null;
        
        var layers = new List<ParsedInstance>();
        var objects = ParseObjects();
        layers.AddRange(objects);

        var currentTerritory = GetCurrentTerritory();
        var housingItems = ParseTerritoryFurniture(currentTerritory);
        var parseCtx = new ParseContext(housingItems);
        
        var loadedLayouts = layoutWorld->LoadedLayouts.ToArray();
        var loadedLayers = loadedLayouts
                           .Select(layout => ParseLayout(layout.Value, parseCtx))
                           .SelectMany(x => x).ToArray();
        var globalLayers = ParseLayout(layoutWorld->GlobalLayout, parseCtx);


        layers.AddRange(loadedLayers.SelectMany(x => x.Instances));
        layers.AddRange(globalLayers.SelectMany(x => x.Instances));

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

    private unsafe ParsedInstanceSet[] ParseLayout(LayoutManager* activeLayout, ParseContext context)
    {
        if (activeLayout == null) return [];
        var layers = new List<ParsedInstanceSet>();
        foreach (var (_, layerPtr) in activeLayout->Layers)
        {
            var layer = ParseLayer(layerPtr, context);
            if (layer != null)
            {
                layers.Add(layer);
            }
        }

        foreach (var (_, terrainPtr) in activeLayout->Terrains)
        {
            var terrain = ParseTerrain(terrainPtr);
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

    private unsafe ParsedTerrainInstance? ParseTerrain(Pointer<TerrainManager> terrainPtr)
    {
        if (terrainPtr == null || terrainPtr.Value == null)
            return null;
        
        var terrainManager = terrainPtr.Value;
        var path = terrainManager->PathString;
        return new ParsedTerrainInstance((nint)terrainManager, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), path);
    }

    private unsafe ParsedInstanceSet? ParseLayer(Pointer<LayerManager> layerManagerPtr, ParseContext context)
    {
        if (layerManagerPtr == null || layerManagerPtr.Value == null)
            return null;

        var layerManager = layerManagerPtr.Value;
        var instances = new List<ParsedInstance>();
        foreach (var (_, instancePtr) in layerManager->Instances)
        {
            if (instancePtr == null || instancePtr.Value == null)
                continue;
            
            var instance = ParseInstance(instancePtr, context);
            if (instance == null)
                continue;
        
            instances.Add(instance);
        }
        

        return new ParsedInstanceSet
        {
            Instances = instances
        };
    }

    private unsafe ParsedInstance? ParseInstance(Pointer<ILayoutInstance> instancePtr, ParseContext context)
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
                var part = ParseSharedGroup(sharedGroup, context);
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
        if (typedInstance->LightPtr == null || typedInstance->LightPtr->LightItem == null)
            return null;

        return new ParsedLightInstance((nint)light, new Transform(*light->GetTransformImpl()), typedInstance->LightPtr->LightItem);
    }

    private unsafe ParsedInstance? ParseSharedGroup(Pointer<SharedGroupLayoutInstance> sharedGroupPtr, ParseContext context)
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
            var child = ParseInstance(instanceData->Instance, context);
            if (child == null)
                continue;
            children.Add(child);
        }

        if (children.Count == 0)
            return null;


        var primaryPath = sharedGroup->GetPrimaryPath();
        string? path;
        if (primaryPath != null)
        {
            path = SpanMemoryUtils.GetStringFromNullTerminated(primaryPath);
        }
        else
        {
            throw new Exception("SharedGroup has no primary path");
        }

        var furnitureMatch = context.HousingItems.FirstOrDefault(item => item.LayoutInstance == sharedGroupPtr);
        if (furnitureMatch is not null)
        {
            var housing = new ParsedHousingInstance((nint)sharedGroup, new Transform(*sharedGroup->GetTransformImpl()), path,
                                             furnitureMatch.GameObject->NameString,
                                             furnitureMatch.GameObject->ObjectKind,
                                             furnitureMatch.Stain,
                                             furnitureMatch.DefaultStain,
                                             /*furnitureMatch.Item,*/ children);
            foreach (var child in housing.Flatten())
            {
                if (child is ParsedBgPartsInstance parsedBgPartsInstance)
                {
                    parsedBgPartsInstance.StainColor = UiUtil.ConvertU32ColorToVector4(housing.Stain?.Color ?? housing.DefaultStain.Color);
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
        string? path;
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
    
    public unsafe ParsedInstance[] ParseObjects()
    {
        var gameObjectManager = sigUtil.GetGameObjectManager();

        var objects = new List<ParsedInstance>();
        var mounts = new Dictionary<nint, ParsedCharacterInstance>();
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
            
            var anyVisible = drawObject->IsVisible;

            void AddObject(ParsedCharacterInstance instance)
            {
                if (instance.Kind == ObjectKind.Mount)
                {
                    mounts.TryAdd(instance.Id, instance);
                }
                else
                {
                    objects.Add(instance);
                }
            }
            
            var transform = new Transform(drawObject->Position, drawObject->Rotation, drawObject->Scale);
            var instance = new ParsedCharacterInstance((nint)obj, obj->NameString, type, transform, anyVisible);
            AddObject(instance);
            
            if (drawObject->IsVisible == false)
            {
                // want to list children which are visible even if the parent is not.
                void HandleRecursiveVisibility(Object* childObject)
                {
                    if (childObject == null) return;
                    if (childObject->GetObjectType() == ObjectType.CharacterBase)
                    {
                        var cBase = (CharacterBase*)childObject;
                        if (cBase->DrawObject.IsVisible)
                        {
                            var cTransform = new Transform(cBase->DrawObject.Position, cBase->DrawObject.Rotation, cBase->DrawObject.Scale);
                            var cInstance = new ParsedCharacterInstance((nint)childObject, $"Child of {obj->NameString}", type, cTransform, true,
                                                                       ParsedCharacterInstance.ParsedCharacterInstanceIdType.CharacterBase)
                            {
                                Parent = instance
                            };
                            AddObject(cInstance);
                            return; // skip parsing if visible as item should be covered under attaches to parent
                        }
                    }
                    
                    foreach (var childOfChild in childObject->ChildObjects)
                    {
                        if (childOfChild == null) continue;
                        HandleRecursiveVisibility(childOfChild);
                    }
                }
                
                foreach (var childObject in drawObject->ChildObjects.GetEnumerator())
                {
                    HandleRecursiveVisibility(childObject);
                }
            }
        }

        // Setup mount parenting
        var characterAttachedMounts = new Dictionary<nint, ParsedCharacterInstance>();
        foreach (var characterInstance in objects.OfType<ParsedCharacterInstance>().Where(x => x.IdType == ParsedCharacterInstance.ParsedCharacterInstanceIdType.GameObject))
        {
            var gameObjectPtr = (GameObject*)characterInstance.Id;
            if (gameObjectPtr == null) continue;

            if (ResolverService.IsCharacterKind(gameObjectPtr->ObjectKind))
            {
                var characterPtr = (Character*)gameObjectPtr;
                if (characterPtr->Mount.MountObject != null)
                {
                    characterAttachedMounts[(nint)characterPtr->Mount.MountObject] = characterInstance;
                    characterAttachedMounts[(nint)characterPtr->Mount.MountObject->DrawObject] = characterInstance;
                }
            }
        }
            
        foreach (var mount in mounts)
        {
            if (characterAttachedMounts.TryGetValue(mount.Key, out var characterInstance))
            {
                mount.Value.Parent = characterInstance;
            }
            
            objects.Add(mount.Value);
        }

        return objects.ToArray();
    }

    private unsafe Furniture[] ParseTerritoryFurniture(HousingTerritory* territory)
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
        foreach (var item in furniture)
        {
            var index = item.Index;
            if (item.Index == -1) continue;
            var objectPtr = objectManager->Objects[index];
            if (objectPtr == null || objectPtr.Value == null || objectPtr.Value->SharedGroupLayoutInstance == null)
            {
                continue;
            }

            if (objectPtr.Value->ObjectKind != ObjectKind.HousingEventObject)
            {
                logger.LogWarning("ObjectKind is not HousingEventObject");
                continue;
            }
            
            var housingObjectPtr = (HousingObject*)objectPtr.Value;
            var layoutInstance = housingObjectPtr->SharedGroupLayoutInstance;
            var instanceHandle = (SharedGroupResourceHandle*)layoutInstance->ResourceHandle;
            if (instanceHandle == null)
            {
                logger.LogWarning("InstanceHandle is null");
                continue;
            }
            
            var housingSettings = instanceHandle->SceneChunk->GetHousingSettings();
            if (housingSettings == null)
            {
                logger.LogWarning("HousingSettings is null");
                continue;
            }

            items.Add(new Furniture
            {
                GameObject = housingObjectPtr,
                LayoutInstance = layoutInstance,
                HousingFurniture = item,
                Stain = item.Stain != 0 ? stainDict[item.Stain] : null,
                DefaultStain = stainDict[housingSettings.Value->DefaultColorId],
                //Item = itemDict.GetValueOrDefault(item.Id)
            });
        }

        return items.ToArray();
    }

    public class ParseContext
    {
        public readonly Furniture[] HousingItems;

        public ParseContext(Furniture[] housingItems)
        {
            HousingItems = housingItems;
        }
    }

    public unsafe class Furniture
    {
        public HousingObject* GameObject;
        public HousingFurniture HousingFurniture;
        //public Item? Item;
        public SharedGroupLayoutInstance* LayoutInstance;
        public Stain? Stain;
        public Stain DefaultStain;
    }

    public void Dispose()
    {
        framework.Update -= Update;
    }
}
