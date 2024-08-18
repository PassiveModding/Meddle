using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Services;
using Microsoft.Extensions.Logging;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Meddle.Plugin.UI;

public class WorldOverlay : IOverlay
{
    private readonly ILogger<WorldOverlay> log;
    private readonly WorldService worldService;
    private readonly IClientState clientState;

    public WorldOverlay(
        ILogger<WorldOverlay> log,
        WorldService worldService,
        IClientState clientState)
    {
        this.log = log;
        this.worldService = worldService;
        this.clientState = clientState;
    }

    private unsafe List<Pointer<Object>> RecurseWorldObjects(
        Object.SiblingEnumerator siblingEnumerator, HashSet<Pointer<Object>> visited)
    {
        var worldObjects = new List<Pointer<Object>>();
        foreach (var childObject in siblingEnumerator)
        {
            if (childObject == null)
                continue;
            if (!visited.Add(childObject))
                continue;
            worldObjects.Add(childObject);
            worldObjects.AddRange(RecurseWorldObjects(childObject->ChildObjects, visited));
        }

        return worldObjects;
    }

    private unsafe Camera* GetCamera()
    {
        var manager = CameraManager.Instance();
        if (manager == null) throw new Exception("Camera manager is null");
        if (manager->CurrentCamera == null) throw new Exception("Current camera is null");
        return manager->CurrentCamera;
    }
    
    public unsafe void DrawOverlay()
    {
        if (!worldService.ShouldDrawOverlay)
            return;
        worldService.ShouldDrawOverlay = false;
        var world = World.Instance();
        if (world == null)
        {
            log.LogError("World instance is null");
            return;
        }

        var camera = GetCamera();
        
        FFXIVClientStructs.FFXIV.Common.Math.Vector3 localPos = clientState.LocalPlayer?.Position ?? Vector3.Zero;
        var worldObjects = RecurseWorldObjects(world->ChildObjects, []);
        
        var hoveredInFrame = new List<Pointer<Object>>();
        foreach (var wo in worldObjects)
        {
            if (wo == null || wo.Value == null)
                continue;
            
            var childObject = wo.Value;
            var drawObject = (DrawObject*)childObject;
            if (!drawObject->IsVisible) continue;
            if (Vector3.Abs(childObject->Position - camera->Position).Length() > worldService.CutoffDistance) continue;
            if (!camera->WorldToScreen(childObject->Position, out var screenPos)) continue;
            // check that the position of said object is infront of the camera
            
            Vector2 sp = screenPos;
            
            var bg = ImGui.GetBackgroundDrawList();
            bg.AddCircleFilled(screenPos, 5, ImGui.GetColorU32(worldService.DotColor));
            
            if (ImGui.IsMouseHoveringRect(sp - new Vector2(5, 5), 
                                          sp + new Vector2(5, 5)) && 
                !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
            {
                hoveredInFrame.Add(childObject);
            }
        }

        if (worldService.ShouldAddAllInRange)
        {
            worldService.ShouldAddAllInRange = false;
            foreach (var wo in worldObjects)
            {
                if (wo == null || wo.Value == null)
                    continue;
                
                var childObject = wo.Value;
                if (Vector3.Abs(childObject->Position - localPos).Length() > worldService.CutoffDistance)
                    continue;
                worldService.SelectedObjects[(nint)childObject] = ParseObject(wo);
            }
        }
        
        if (hoveredInFrame.Count != 0)
        {
            using var tt = ImRaii.Tooltip();
            for (var i = 0; i < hoveredInFrame.Count; i++)
            {
                if (i > 0)
                    ImGui.Separator();
                var worldObj = hoveredInFrame[i];
                var childObject = worldObj.Value;
                var type = childObject->GetObjectType();
                ImGui.Text($"Address: {(nint)childObject:X8}");
                ImGui.Text($"Type: {type}");
                if (WorldService.IsSupportedObject(type))
                {
                    var path = WorldService.GetPath(childObject);
                    ImGui.Text($"Path: {path}");
                }
                ImGui.Text($"Position: {childObject->Position}");
                ImGui.Text($"Rotation: {childObject->Rotation}");
                ImGui.Text($"Scale: {childObject->Scale}");

                ImGui.SetNextFrameWantCaptureMouse(true);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    worldService.SelectedObjects[(nint)childObject] = ParseObject(worldObj);
                }
            }
        }
    }
    
    private unsafe WorldService.ObjectSnapshot ParseObject(Pointer<Object> obj)
    {
        var type = obj.Value->GetObjectType();
        return type switch
        {
            ObjectType.BgObject => ParseBgObject(obj),
            ObjectType.Terrain => ParseTerrain(obj),
            _ => ParseUnknownObject(obj)
        };
    }
    
    private unsafe WorldService.BgObjectSnapshot ParseBgObject(Pointer<Object> obj)
    {
        var bgObj = (BgObject*)obj.Value;
        var path = WorldService.GetBgObjectPath(bgObj);
        return new WorldService.BgObjectSnapshot(path, obj.Value->Position, obj.Value->Rotation, obj.Value->Scale);
    }
    
    private unsafe WorldService.TerrainObjectSnapshot ParseTerrain(Pointer<Object> obj)
    {
        var terrain = (Terrain*)obj.Value;
        var path = WorldService.GetTerrainPath(terrain);
        return new WorldService.TerrainObjectSnapshot(path, obj.Value->Position, obj.Value->Rotation, obj.Value->Scale);
    }
    
    private unsafe WorldService.ObjectSnapshot ParseUnknownObject(Pointer<Object> obj)
    {
        return new WorldService.ObjectSnapshot(obj.Value->GetObjectType(), obj.Value->Position, obj.Value->Rotation, obj.Value->Scale);
    }
}
