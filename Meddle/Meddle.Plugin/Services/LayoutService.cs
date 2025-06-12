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
using Camera = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Camera;
using HousingFurniture = FFXIVClientStructs.FFXIV.Client.Game.HousingFurniture;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;
using SharedGroupResourceHandle = Meddle.Plugin.Models.Structs.SharedGroupResourceHandle;
using Transform = Meddle.Plugin.Models.Transform;

namespace Meddle.Plugin.Services;

public class LayoutService : IService, IDisposable
{
    private readonly ILogger<LayoutService> logger;
    private readonly IFramework framework;
    private readonly StainHooks stainHooks;
    private readonly Configuration config;
    private readonly SigUtil sigUtil;

    public LayoutService(
        SigUtil sigUtil, 
        ILogger<LayoutService> logger,
        IFramework framework,
        StainHooks stainHooks,
        Configuration config)
    {
        this.sigUtil = sigUtil;
        this.logger = logger;
        this.framework = framework;
        this.stainHooks = stainHooks;
        this.config = config;
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
        
        var instances = new List<ParsedInstance>();
        var objects = ParseObjects();
        var cameras = ParseCameras();
        instances.AddRange(objects);
        instances.AddRange(cameras);

        var currentTerritory = GetCurrentTerritory();
        var housingItems = ParseTerritoryFurniture(currentTerritory);
        var parseCtx = new ParseContext(housingItems);
        
        var loadedLayouts = layoutWorld->LoadedLayouts.ToArray();
        var loadedLayers = loadedLayouts
                           .Select(layout => ParseLayout(layout.Value, parseCtx))
                           .SelectMany(x => x).ToArray();
        var globalLayers = ParseLayout(layoutWorld->GlobalLayout, parseCtx);


        instances.AddRange(loadedLayers.SelectMany(x => x.Instances));
        instances.AddRange(globalLayers.SelectMany(x => x.Instances));

        return instances.ToArray();
    }
    
    private unsafe ParsedCameraInstance[] ParseCameras()
    {
        // var cameraManager = sigUtil.GetCameraManager();
        // if (cameraManager == null)
        //     return [];
        //
        // var cameras = new List<ParsedCameraInstance>();
        // for (var i = 0; i < cameraManager->Cameras.Length; i++)
        // {
        //     var cameraPtr = cameraManager->Cameras[i];
        //     if (cameraPtr == null || cameraPtr.Value == null || cameraPtr.Value->RenderCamera == null)
        //         continue;
        //
        //     var parsedCamera = ParseCameraPtr(cameraPtr);
        //     cameras.Add(parsedCamera);
        // }
        //
        // return cameras.ToArray();
        var activeCamera = sigUtil.GetCamera();
        if (activeCamera == null || activeCamera->RenderCamera == null)
            return [];
        
        var parsedCamera = ParseCameraPtr(activeCamera);
        return [parsedCamera];
        
        ParsedCameraInstance ParseCameraPtr(Camera* camera)
        {
            var transform = new Transform(camera->Position, camera->Rotation, camera->Scale);
            var fov = camera->RenderCamera->FoV;
            var aspectRatio = camera->RenderCamera->AspectRatio;
            var position = camera->Position;
            var lookAtVector = camera->LookAtVector;
            return new ParsedCameraInstance((nint)camera, transform, fov, aspectRatio, position, lookAtVector);
        }
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
                if (primaryPath.HasValue)
                {
                    path = primaryPath;
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
        string? path = primaryPath.HasValue ? primaryPath : throw new Exception("SharedGroup has no primary path");

        var furnitureMatch = context.HousingItems.FirstOrDefault(item => item.LayoutInstance == sharedGroupPtr);
        if (furnitureMatch is not null)
        {
            var housing = new ParsedHousingInstance((nint)sharedGroup, new Transform(*sharedGroup->GetTransformImpl()), path,
                                             furnitureMatch.GameObject->NameString,
                                             furnitureMatch.GameObject->ObjectKind,
                                             furnitureMatch.Stain,
                                             furnitureMatch.DefaultStain,
                                             children);
            foreach (var child in housing.Flatten())
            {
                if (child is ParsedBgPartsInstance parsedBgPartsInstance)
                {
                    parsedBgPartsInstance.Stain = furnitureMatch.Stain ?? furnitureMatch.DefaultStain;
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

        var graphics = bgPart->GraphicsObject;
        if (graphics == null)
            return null;

        var primaryPath = bgPart->GetPrimaryPath();
        string? path = primaryPath.HasValue ? primaryPath : throw new Exception("BgPart has no primary path");

        return new ParsedBgPartsInstance((nint)bgPartPtr.Value, graphics->IsVisible, new Transform(*bgPart->GetTransformImpl()), path);
    }

    private unsafe bool IsObjectPlaceHolder(DrawObject* obj)
    {
        if (obj == null)
            return true;
        
        if (obj->GetObjectType() == ObjectType.CharacterBase)
        {
            var characterBase = (CharacterBase*)obj;
            if (IsCharacterPlaceholder(characterBase))
                return true;
        }

        return false;
    }
    
    private unsafe bool IsCharacterPlaceholder(CharacterBase* characterBase)
    {
        if (characterBase == null)
            return true;
        
        if (characterBase->ModelsSpan.Length == 1)
        {
            var model = characterBase->ModelsSpan[0];
            if (model == null || model.Value == null)
                return true;
            if (model.Value->ModelResourceHandle == null)
                return true;
            var fileName = model.Value->ModelResourceHandle->FileName;
            var fileNameString = fileName.ToString();
            if (fileNameString == "chara/monster/m9995/obj/body/b0001/model/m9995b0001.mdl")
                return true;
        }

        return false;
    }
    
    public unsafe ParsedInstance[] ParseObjects()
    {
        var gameObjectManager = sigUtil.GetGameObjectManager();

        var objects = new Dictionary<nint, ParsedInstance>();
        var mounts = new Dictionary<nint, ParsedCharacterInstance>();
        for (var idx = 0; idx < gameObjectManager->Objects.GameObjectIdSorted.Length; idx++)
        {
            var objectPtr = gameObjectManager->Objects.GameObjectIdSorted[idx];
            if (objectPtr == null || objectPtr.Value == null)
                continue;

            var obj = objectPtr.Value;
            if (objects.ContainsKey((nint)obj))
                continue;

            ObjectKind type = obj->GetObjectKind();
            var drawObject = obj->DrawObject;
            if (drawObject == null)
                continue;

            if (IsObjectPlaceHolder(drawObject))
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
                    objects.TryAdd(instance.Id, instance);
                }
            }

            var transform = new Transform(drawObject->Position, drawObject->Rotation, drawObject->Scale);
            var name = obj->NameString.GetCharacterName(config, type, idx.ToString());
            var instance = new ParsedCharacterInstance((nint)obj, name, type, transform, anyVisible);
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
                            if (IsCharacterPlaceholder(cBase))
                                return;

                            var cTransform = new Transform(cBase->DrawObject.Position, cBase->DrawObject.Rotation, cBase->DrawObject.Scale);
                            var cInstance = new ParsedCharacterInstance((nint)childObject, $"Child of {name}", type, cTransform, true,
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
        foreach (var characterInstance in objects.Values.OfType<ParsedCharacterInstance>().Where(x => x.IdType == ParsedCharacterInstance.ParsedCharacterInstanceIdType.GameObject))
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
            
            objects.TryAdd(mount.Key, mount.Value);
        }

        return objects.Values.ToArray();
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
            _ => []
        };
        var objectManager = type switch
        {
            HousingTerritoryType.Indoor => &((IndoorTerritory*)territory)->HousingObjectManager,
            HousingTerritoryType.Outdoor => &((OutdoorTerritory*)territory)->HousingObjectManager,
            _ => null
        };

        if (furniture.Length == 0 || objectManager == null)
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
                Stain = stainHooks.GetStain(item.Stain),
                DefaultStain = stainHooks.GetStain(housingSettings.Value->DefaultColorId)!.Value,
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
