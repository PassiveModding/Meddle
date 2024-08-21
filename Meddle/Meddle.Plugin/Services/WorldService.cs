using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Structs;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Meddle.Plugin.Services;

public class WorldService : IService, IDisposable
{

    public record ObjectSnapshot(ObjectType Type, Vector3 Position, Quaternion Rotation, Vector3 Scale)
    {
        public Matrix4x4 Transform => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
    }
    public record BgObjectSnapshot(string Path, Vector3 Position, Quaternion Rotation, Vector3 Scale, MdlFileGroup? MdlFileGroup) : 
        ObjectSnapshot(ObjectType.BgObject, Position, Rotation, Scale);
    
    public record HousingObjectSnapshot(string Path, Vector3 Position, Quaternion Rotation, Vector3 Scale, MdlFileGroup? MdlFileGroup, Vector4 StainColor) : 
        BgObjectSnapshot(Path, Position, Rotation, Scale, MdlFileGroup);
    
    public record TerrainObjectSnapshot(string Path, Vector3 Position, Quaternion Rotation, Vector3 Scale) : 
        ObjectSnapshot(ObjectType.Terrain, Position, Rotation, Scale);

    private readonly Configuration config;
    public float CutoffDistance;
    public Vector4 DotColor;
    public readonly Dictionary<nint, ObjectSnapshot> SelectedObjects = [];
    
    // TODO: This isn't great, should find a better way to link drawing the World Overlay to the World Tab
    public bool ShouldDrawOverlay;
    public bool ShouldAddAllInRange;

    public enum OverlayType
    {
        Disabled, // hide overlay
        Housing, // draw only housing items
        World // draw all items in the world
    }
    
    public OverlayType Overlay = OverlayType.World;
    
    public bool ResolveUsingGameGui = true;
    
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
        if (bgObject->ModelResourceHandle == null) return "Unknown";
        return bgObject->ModelResourceHandle->ResourceHandle.FileName.ToString();
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
