using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
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
    private readonly IGameGui gui;
    private readonly SigUtil sigUtil;
    private readonly HousingService housingService;

    public WorldOverlay(
        ILogger<WorldOverlay> log,
        WorldService worldService,
        IClientState clientState,
        IGameGui gui,
        SigUtil sigUtil,
        HousingService housingService)
    {
        this.log = log;
        this.worldService = worldService;
        this.clientState = clientState;
        this.gui = gui;
        this.sigUtil = sigUtil;
        this.housingService = housingService;
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
    
    public unsafe bool WorldToScreen(Vector3 worldPos, out Vector2 screenPos) {
        var device = sigUtil.GetDevice();
        var camera = sigUtil.GetCamera();
        float width = device->Width;
        float height = device->Height;
        var pCoords = Vector4.Transform(new Vector4(worldPos, 1f), camera->ViewMatrix * camera->RenderCamera->ProjectionMatrix);
        if (Math.Abs(pCoords.W) < float.Epsilon) {
            screenPos = Vector2.Zero;
            return false;
        }

        pCoords *= MathF.Abs(1.0f / pCoords.W);
        screenPos = new Vector2 {
            X = (pCoords.X + 1.0f) * width * 0.5f,
            Y = (1.0f - pCoords.Y) * height * 0.5f
        };

        return IsOnScreen(new Vector3(pCoords.X, pCoords.Y, pCoords.Z));

        static bool IsOnScreen(Vector3 pos) {
            return -1.0 <= pos.X && pos.X <= 1.0 && -1.0 <= pos.Y && pos.Y <= 1.0 && pos.Z <= 1.0 && 0.0 <= pos.Z;
        }
    }

    public unsafe void DrawWorldOverlay()
    {
        var world = sigUtil.GetWorld();
        if (world == null)
        {
            log.LogError("World instance is null");
            return;
        }

        var camera = sigUtil.GetCamera();
        var localPos = clientState.LocalPlayer?.Position ?? Vector3.Zero;
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
            Vector2 screenPos;
            if (worldService.ResolveUsingGameGui)
            {
                if (!gui.WorldToScreen(childObject->Position, out screenPos)) continue;
            }
            else
            {
                if (!camera->WorldToScreen(childObject->Position, out var csScreenPos)) continue;
                screenPos = csScreenPos;
            }
            
            var bg = ImGui.GetBackgroundDrawList();
            bg.AddCircleFilled(screenPos, 5, ImGui.GetColorU32(worldService.DotColor));
            
            if (ImGui.IsMouseHoveringRect(screenPos - new Vector2(5, 5), 
                                          screenPos + new Vector2(5, 5)) && 
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
                if (Vector3.Abs((Vector3)childObject->Position - localPos).Length() > worldService.CutoffDistance)
                    continue;
                worldService.SelectedObjects[(nint)childObject] = ParseObject(wo);
            }
        }
        
        if (hoveredInFrame.Count != 0)
        {
            var housingItems = housingService.GetHousingItems();
            var housingMap = new Dictionary<nint, HousingService.HousingItem>();
            foreach (var housingItem in housingItems)
            {
                foreach (var bgPart in housingItem.Value.BgParts)
                {
                    if (bgPart == null || bgPart.Value == null)
                        continue;
                    housingMap[(nint)bgPart.Value] = housingItem.Value;
                }
            }
            
            using var tt = ImRaii.Tooltip();
            for (var i = 0; i < hoveredInFrame.Count; i++)
            {
                var worldObj = hoveredInFrame[i];
                var childObject = worldObj.Value;
                var type = childObject->GetObjectType();
                ImGui.Separator();
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
                
                if (housingMap.TryGetValue((nint)childObject, out var item))
                {
                    ImGui.Text($"Furniture: {item.Object->NameString}");
                    ImGui.Text($"Index: {item.Furniture.Index}");
                    if (item.Stain != null)
                    {
                        ImGui.Text($"Stain: {item.Furniture.Stain}");
                        ImGui.SameLine();
                        var stainColor = ImGui.ColorConvertU32ToFloat4(item.Stain.Color);
                        ImGui.ColorButton("Stain", stainColor, ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoTooltip);
                    }
                }

                ImGui.SetNextFrameWantCaptureMouse(true);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    worldService.SelectedObjects[(nint)childObject] = ParseObject(worldObj);
                }
            }
        }
    }

    public unsafe void DrawHousingOverlay()
    {
        var housingItems = housingService.GetHousingItems();
        if (housingItems.Count == 0) return;

        var camera = sigUtil.GetCamera();
        var localPos = clientState.LocalPlayer?.Position ?? Vector3.Zero;
        
        foreach (var item in housingItems.Values)
        {
            var childObject = item.Object;
            if (Vector3.Abs((Vector3)childObject->Position - localPos).Length() > worldService.CutoffDistance)
                continue;
            Vector2 screenPos;
            if (worldService.ResolveUsingGameGui)
            {
                if (!gui.WorldToScreen(childObject->Position, out screenPos)) continue;
            }
            else
            {
                if (!camera->WorldToScreen(childObject->Position, out var csScreenPos)) continue;
                screenPos = csScreenPos;
            }
            
            var bg = ImGui.GetBackgroundDrawList();
            bg.AddCircleFilled(screenPos, 5, ImGui.GetColorU32(worldService.DotColor));
            
            if (ImGui.IsMouseHoveringRect(screenPos - new Vector2(5, 5), 
                                          screenPos + new Vector2(5, 5)) && 
                !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
            {
                using var tt = ImRaii.Tooltip();
                ImGui.Separator();
                ImGui.Text($"Address: {(nint)childObject:X8}");
                ImGui.Text($"Kind: {item.Object->GetObjectKind()}");
                ImGui.Text($"Furniture: {item.Object->NameString}");
                ImGui.Text($"Index: {item.Furniture.Index}");
                if (item.Stain != null)
                {
                    ImGui.Text($"Stain: {item.Furniture.Stain}");
                    ImGui.SameLine();
                    var stainColor = ImGui.ColorConvertU32ToFloat4(item.Stain.Color);
                    ImGui.ColorButton("Stain", stainColor, ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoTooltip);
                }

                foreach (var bgObject in item.BgParts)
                {
                    if (bgObject == null || bgObject.Value == null)
                        continue;

                    var draw = bgObject.Value;
                    
                    ImGui.Text($"File: {draw->ModelResourceHandle->ResourceHandle.FileName}");
                    ImGui.Text($"Position: {draw->DrawObject.Position}");
                    ImGui.Text($"Rotation: {draw->DrawObject.Rotation}");
                    ImGui.Text($"Scale: {draw->DrawObject.Scale}");
                }
                
                ImGui.SetNextFrameWantCaptureMouse(true);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    foreach (var bgObject in item.BgParts)
                    {
                        if (bgObject == null || bgObject.Value == null)
                            continue;
                        worldService.SelectedObjects[(nint)bgObject.Value] = ParseHousingBgObject((Object*)bgObject.Value, item);
                    }
                }
            }
        }
    }
    
    public void DrawOverlay()
    {
        if (!worldService.ShouldDrawOverlay)
            return;
        worldService.ShouldDrawOverlay = false;

        switch (worldService.Overlay)
        {
            case WorldService.OverlayType.World:
                DrawWorldOverlay();
                break;
            case WorldService.OverlayType.Housing:
                DrawHousingOverlay();
                break;
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
        return new WorldService.BgObjectSnapshot(path, obj.Value->Position, obj.Value->Rotation, obj.Value->Scale, null);
    }
    
    private unsafe WorldService.HousingObjectSnapshot ParseHousingBgObject(Pointer<Object> obj, HousingService.HousingItem item)
    {
        var bgObj = (BgObject*)obj.Value;
        var path = WorldService.GetBgObjectPath(bgObj);
        var color = ImGui.ColorConvertU32ToFloat4(item.Stain?.Color ?? 0);
        return new WorldService.HousingObjectSnapshot(path, obj.Value->Position, obj.Value->Rotation, obj.Value->Scale, null, color);
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
