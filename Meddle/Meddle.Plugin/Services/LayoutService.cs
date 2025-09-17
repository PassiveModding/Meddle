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
using BgObject = Meddle.Plugin.Models.Structs.BgObject;
using Camera = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Camera;
using HousingFurniture = FFXIVClientStructs.FFXIV.Client.Game.HousingFurniture;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;
using OutdoorPlotFixtureData = Meddle.Plugin.Models.Structs.Outdoor.OutdoorPlotFixtureData;
using OutdoorPlotLayoutData = Meddle.Plugin.Models.Structs.Outdoor.OutdoorPlotLayoutData;
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
    public Vector3 SearchOrigin { get; set; } = Vector3.Zero;
    
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
        var envLight = ParseEnvLight(cameras.Length > 0 ? cameras[0].Transform.Translation : Vector3.Zero);
        if (envLight != null)
        {
            instances.Add(envLight);
        }
        instances.AddRange(objects);
        instances.AddRange(cameras);
        
        
        var loadedLayouts = layoutWorld->LoadedLayouts.ToArray();
        var loadedLayers = loadedLayouts
                           .Select(layout => ParseLayout(layout.Value))
                           .SelectMany(x => x).ToArray();
        var globalLayers = ParseLayout(layoutWorld->GlobalLayout);

        instances.AddRange(loadedLayers.SelectMany(x => x.Instances));
        instances.AddRange(globalLayers.SelectMany(x => x.Instances));

        return instances.ToArray();
    }
    
    private unsafe ParsedCameraInstance[] ParseCameras()
    {
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

    private unsafe ParsedInstanceSet[] ParseLayout(LayoutManager* activeLayout)
    {
        if (activeLayout == null) return [];
        var layers = new List<ParsedInstanceSet>();
        
        var housingItems = ParseTerritoryFurniture(activeLayout);
        var context = new ParseContext(housingItems);
        
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
        
        return new ParsedTerrainInstance((nint)terrainManager, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), path, SearchOrigin);
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
        var type = instanceLayout->Id.Type;
        var pos = instanceLayout->GetTranslationImpl();
        if (pos != null)
        {
            if (Vector3.Distance(SearchOrigin, *pos) > config.LayoutConfig.WorldCutoffDistance)
            {
                return null;
            }
        }
        
        switch (type)
        {
            case InstanceType.Decal when !config.LayoutConfig.DrawTypes.HasFlag(ParsedInstanceType.Decal):
                return null;
            case InstanceType.Light when !config.LayoutConfig.DrawTypes.HasFlag(ParsedInstanceType.Light):
                return null;
            case InstanceType.BgPart when !config.LayoutConfig.DrawTypes.HasFlag(ParsedInstanceType.BgPart):
                return null;
            case InstanceType.SharedGroup when !config.LayoutConfig.DrawTypes.HasFlag(ParsedInstanceType.SharedGroup):
                return null;
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
            case InstanceType.Decal:
            {
                var decal = ParseDecalInstance(instanceLayout);
                return decal;
            }
            default:
            {
                if (!config.LayoutConfig.DrawTypes.HasFlag(ParsedInstanceType.Unsupported))
                    return null;
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
    
    private unsafe ParsedInstance? ParseDecalInstance(Pointer<ILayoutInstance> decalPtr)
    {
        if (decalPtr == null || decalPtr.Value == null)
            return null;

        var decalLayout = decalPtr.Value;
        if (decalLayout->Id.Type != InstanceType.Decal)
            return null;

        var typedInstance = (DecalLayoutInstance*)decalLayout;
        if (typedInstance->DecalPtr == null || typedInstance->DecalPtr->DecalItem == null)
            return null;

        var transform = decalLayout->GetTransformImpl();
        if (transform == null)
            return null;
        
        var decalData = typedInstance->DecalPtr->DecalItem;
        var diffuseTex = decalData->TexDiffuse;
        var diffusePath = diffuseTex != null ? diffuseTex->FileName.ParseString() : string.Empty;
        var normalTex = decalData->TexNormal;
        var normalPath = normalTex != null ? normalTex->FileName.ParseString() : string.Empty;
        var specularTex = decalData->TexSpecular;
        var specularPath = specularTex != null ? specularTex->FileName.ParseString() : string.Empty;

        return new ParsedWorldDecalInstance((nint)decalLayout,
                                       new Transform(*transform),
                                       diffusePath,
                                       normalPath,
                                       specularPath);
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

    private unsafe ParsedEnvLightInstance? ParseEnvLight(Vector3 position)
    {
        var envMan = EnvManagerEx.Instance();
        if (envMan == null)
            return null;

        var lighting = envMan->EnvState.Lighting;
        return new ParsedEnvLightInstance((nint)envMan,
                                          new Transform(position, Quaternion.Identity, Vector3.One),
                                          lighting);
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
        
        var plotMatch = context.Housing.Plots.FirstOrDefault(plot => plot.LayoutInstance == sharedGroupPtr);
        if (plotMatch is not null)
        {
            var childrenArray = children.ToArray();
            for (var i = 0; i < childrenArray.Length; i++)
            {
                var child = childrenArray[i];
                var fixtureMatch = plotMatch.Fixtures.FirstOrDefault(f => f.LayoutInstance == (SharedGroupLayoutInstance*)child.Id);
                if (fixtureMatch is null || child is not ParsedSharedInstance sgbInstance) continue;
                // replace with housing furniture instance
                var housing = new ParsedHousingInstance(sgbInstance.Id, sgbInstance.Transform, sgbInstance.Path.GamePath,
                                                        fixtureMatch.FixtureName ?? sgbInstance.Path.GamePath,
                                                        ObjectKind.HousingEventObject, // faking this
                                                        fixtureMatch.Stain,
                                                        fixtureMatch.DefaultStain,
                                                        sgbInstance.Children.ToArray());
                foreach (var grandChild in housing.Flatten())
                {
                    if (grandChild is ParsedBgPartsInstance parsedBgPartsInstance)
                    {
                        if (fixtureMatch.Stain == null || fixtureMatch.Stain.Value.RowId == 0)
                        {
                            if (fixtureMatch.DefaultStain.RowId != 0)
                            {
                                parsedBgPartsInstance.Stain = fixtureMatch.DefaultStain;
                            }
                        }
                        else
                        {
                            parsedBgPartsInstance.Stain = fixtureMatch.Stain;
                        }
                    }
                }
                    
                childrenArray[i] = housing;
            }
            
            children = childrenArray.ToList();
        }

        var primaryPath = sharedGroup->GetPrimaryPath();
        string path = primaryPath.HasValue ? primaryPath : throw new Exception("SharedGroup has no primary path");

        var furnitureMatch = context.Housing.Furniture.FirstOrDefault(item => item.LayoutInstance == sharedGroupPtr);
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
                    if (furnitureMatch.Stain == null || furnitureMatch.Stain.Value.RowId == 0)
                    {
                        if (furnitureMatch.DefaultStain.RowId != 0)
                        {
                            parsedBgPartsInstance.Stain = furnitureMatch.DefaultStain;
                        }
                    }
                    else
                    {
                        parsedBgPartsInstance.Stain = furnitureMatch.Stain;
                    }
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

        BgObject* graphics = (BgObject*)bgPart->GraphicsObject;
        if (graphics == null || graphics->ModelResourceHandle == null)
            return null;
        
        if (graphics->ModelResourceHandle->LoadState < 7)
        {
            return null; 
        }

        var primaryPath = bgPart->GetPrimaryPath();
        string path = primaryPath.HasValue ? primaryPath : throw new Exception("BgPart has no primary path");
        
        var bgChangeHandle = graphics->GetBgChangeMaterial();
        (int BgChangeMaterialIndex, string Path)? bgChangeMaterial = null;
        if (bgChangeHandle != null && bgChangeHandle.Value.ResourceHandle != null && bgChangeHandle.Value.ResourceHandle.Value != null)
        {
            bgChangeMaterial = (bgChangeHandle.Value.MaterialIndex, bgChangeHandle.Value.ResourceHandle.Value->FileName.ParseString());
        }

        var modelPtr = (nint)graphics->ModelResourceHandle;
        return new ParsedBgPartsInstance((nint)bgPartPtr.Value, bgPart->GraphicsObject->IsVisible, new Transform(*bgPart->GetTransformImpl()), path, bgChangeMaterial)
        {
            ModelPtr = modelPtr
        };
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
        var children = new Dictionary<nint, ParsedInstance>();
        var mounts = new Dictionary<nint, ParsedCharacterInstance>();
        var ornaments = new Dictionary<nint, ParsedCharacterInstance>();
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

            void AddObject(ParsedCharacterInstance instance, bool isChild = false)
            {
                if (isChild)
                {
                    children.TryAdd(instance.Id, instance);
                }
                else if (instance.Kind == ObjectKind.Mount)
                {
                    mounts.TryAdd(instance.Id, instance);
                }
                else if (instance.Kind == ObjectKind.Ornament)
                {
                    ornaments.TryAdd(instance.Id, instance);
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
                            AddObject(cInstance, true);
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
        var parentedInstances = new Dictionary<nint, ParsedCharacterInstance>();
        foreach (var characterInstance in objects.Values.OfType<ParsedCharacterInstance>().Where(x => x.IdType == ParsedCharacterInstance.ParsedCharacterInstanceIdType.GameObject))
        {
            var gameObjectPtr = (GameObject*)characterInstance.Id;
            if (gameObjectPtr == null) continue;

            if (ResolverService.IsCharacterKind(gameObjectPtr->ObjectKind))
            {
                var characterPtr = (Character*)gameObjectPtr;
                if (characterPtr->Mount.MountObject != null)
                {
                    parentedInstances[(nint)characterPtr->Mount.MountObject] = characterInstance;
                    parentedInstances[(nint)characterPtr->Mount.MountObject->DrawObject] = characterInstance;
                }
                if (characterPtr->OrnamentData.OrnamentObject != null)
                {
                    parentedInstances[(nint)characterPtr->OrnamentData.OrnamentObject] = characterInstance;
                    parentedInstances[(nint)characterPtr->OrnamentData.OrnamentObject->DrawObject] = characterInstance;
                }
            }
        }
            
        foreach (var mount in mounts)
        {
            if (parentedInstances.TryGetValue(mount.Key, out var characterInstance))
            {
                mount.Value.Parent = characterInstance;
            }
            
            objects.TryAdd(mount.Key, mount.Value);
        }
        
        foreach (var ornament in ornaments)
        {
            if (parentedInstances.TryGetValue(ornament.Key, out var characterInstance))
            {
                ornament.Value.Parent = characterInstance;
            }
            objects.TryAdd(ornament.Key, ornament.Value);
        }

        return objects.Values.ToArray();
    }
    
    private unsafe HousingTerritoryData ParseTerritoryFurniture(LayoutManager* activeLayout)
    {
        var territory = GetCurrentTerritory();
        if (territory == null || territory->IsLoaded() == false)
            return HousingTerritoryData.Empty;
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

        bool IsSgbHandleValid(SharedGroupResourceHandle* handle)
        {
            if (handle == null || handle->ResourceHandle == null)
                return false;
            if (handle->ResourceHandle->LoadState < 7 || handle->SceneChunk == null || handle->SceneChunk->SgbData == null)
                return false;
            return true;
        }
        
        var plots = new List<Plot>();
        if (type == HousingTerritoryType.Outdoor && activeLayout != null && activeLayout->OutdoorAreaData != null)
        {
            var outdoorData = activeLayout->OutdoorAreaData;
            for (var i = 0; i < outdoorData->Plots.Length; i++) // outdoor
            {
                var plot = outdoorData->Plots[i];
                OutdoorPlotLayoutData meddlePlot = *(OutdoorPlotLayoutData*)&plot; 
                var fixtures = new List<Fixture>();
                foreach (var fixtureData in plot.Fixture)
                {
                    OutdoorPlotFixtureData meddleFixture = *(OutdoorPlotFixtureData*)&fixtureData;
                    if (meddleFixture.UnkGroup == null || meddleFixture.UnkGroup->FixtureLayoutInstance == null)
                        continue;
                    
                    
                    var layoutInstance = meddleFixture.UnkGroup->FixtureLayoutInstance;
                    var instanceHandle = (SharedGroupResourceHandle*)layoutInstance->ResourceHandle;
                    if (!IsSgbHandleValid(instanceHandle))
                        continue;
                    

                    Pointer<HousingSettings> housingSettings = instanceHandle->SceneChunk->GetHousingSettings();
                    if (housingSettings == null)
                    {
                        logger.LogWarning("HousingSettings is null");
                        continue;
                    }
                    
                    var defaultStain = stainHooks.GetStain(housingSettings.Value->DefaultColorId);
                    if (defaultStain == null)
                    {
                        logger.LogWarning("Default stain is null for fixture {FixtureId}", meddleFixture.FixtureId);
                        continue;
                    }

                    string? fixtureName = null;
                    if (meddleFixture.FixtureId != 0 && stainHooks.HousingDict.TryGetValue(meddleFixture.FixtureId, out var itemId) && stainHooks.ItemDict.TryGetValue(itemId, out var item))
                    {
                        fixtureName = item.Name.ToString();
                    }
                    
                    fixtures.Add(new Fixture
                    {
                        FixtureName = fixtureName,
                        FixtureId = meddleFixture.FixtureId,
                        Stain =  stainHooks.GetStain(meddleFixture.StainId),
                        DefaultStain = defaultStain.Value,
                        LayoutInstance = meddleFixture.UnkGroup->FixtureLayoutInstance
                    });
                }

                
                plots.Add(new Plot
                {
                    LayoutInstance = meddlePlot.PlotLayoutInstance,
                    PlotId = i,
                    Fixtures = fixtures.ToArray()
                });
            }
        }
        
        if (furniture.Length == 0 || objectManager == null)
        {
            return new HousingTerritoryData
            {
                Furniture = [],
                Plots = plots.ToArray()
            };
        }
        
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
            if (!IsSgbHandleValid(instanceHandle))
            {
                continue;
            }

            Pointer<HousingSettings> housingSettings = instanceHandle->SceneChunk->GetHousingSettings();
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

        return new HousingTerritoryData
        {
            Furniture = items.ToArray(),
            Plots = plots.ToArray()
        };
    }

    public class ParseContext
    {
        public readonly HousingTerritoryData Housing;

        public ParseContext(HousingTerritoryData housing)
        {
            Housing = housing;
        }
    }
    
    public record HousingTerritoryData
    {
        public Furniture[] Furniture = [];
        public Plot[] Plots = [];
        
        public static HousingTerritoryData Empty => new HousingTerritoryData
        {
            Furniture = [],
            Plots = []
        };
    }
    
    public unsafe class Plot
    {
        public SharedGroupLayoutInstance* LayoutInstance;
        public int PlotId;
        public Fixture[] Fixtures = [];
        public Stain DefaultStain;
    }

    public unsafe class Fixture
    {
        public ulong FixtureId;
        public string? FixtureName;
        public Stain? Stain;
        public Stain DefaultStain;
        public SharedGroupLayoutInstance* LayoutInstance;
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
