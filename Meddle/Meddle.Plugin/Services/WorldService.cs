using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Models.Structs;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Meddle.Plugin.Services;

public class WorldService : IService, IDisposable
{
    private readonly Configuration config;
    public float CutoffDistance;
    public Vector4 DotColor;
    public readonly HashSet<Pointer<Object>> SelectedObjects = [];
    public bool ShouldDrawOverlay;
    
    public void SaveOptions()
    {
        config.WorldCutoffDistance = CutoffDistance;
        config.WorldDotColor = DotColor;
        config.Save();
    }

    public WorldService(Configuration config)
    {
        this.config = config;
        CutoffDistance = config.WorldCutoffDistance;
        DotColor = config.WorldDotColor;
    }
    
    public static bool IsSupportedObject(ObjectType type)
    {
        return type switch
        {
            ObjectType.BgObject => true,
            // ObjectType.Terrain => true,
            _ => false
        };
    }
    
    public static unsafe string GetPath(Object* obj)
    {
        var type = obj->GetObjectType();
        var path = type switch
        {
            ObjectType.BgObject => GetBgObjectPath((BgObject*)obj),
            ObjectType.Terrain => GetTerrainPath((Terrain*)obj),
            _ => "Unknown"
        };
        
        return path;
    }
    
    public static unsafe string GetBgObjectPath(BgObject* bgObject)
    {
        if (bgObject->ResourceHandle == null) return "Unknown";
        return bgObject->ResourceHandle->FileName.ToString();
    }
    
    public static unsafe string GetTerrainPath(Terrain* terrain)
    {
        if (terrain->ResourceHandle == null) return "Unknown";
        return terrain->ResourceHandle->FileName.ToString();
    }

    public void Dispose()
    {
        SelectedObjects.Clear();
    }
}
