using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Services;
using Microsoft.Extensions.Logging;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Meddle.Plugin.UI;

public class WorldOverlay : IOverlay
{
    private readonly ILogger<WorldOverlay> log;
    private readonly IGameGui gui;
    private readonly WorldService worldService;
    private readonly IClientState clientState;

    public WorldOverlay(
        ILogger<WorldOverlay> log,
        IGameGui gui,
        WorldService worldService,
        IClientState clientState)
    {
        this.log = log;
        this.gui = gui;
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
        
        FFXIVClientStructs.FFXIV.Common.Math.Vector3 localPos = clientState.LocalPlayer?.Position ?? Vector3.Zero;
        var worldObjects = RecurseWorldObjects(world->ChildObjects, new HashSet<Pointer<Object>>());
        
        var hoveredInFrame = new List<Pointer<Object>>();
        foreach (var wo in worldObjects)
        {
            if (wo == null || wo.Value == null)
                continue;
            
            var childObject = wo.Value;
            if (Vector3.Abs(childObject->Position - localPos).Length() > worldService.CutoffDistance)
                continue;
            if (!gui.WorldToScreen(childObject->Position, out var screenPos, out var inView)) continue;
            if (!inView) continue;
            
            var bg = ImGui.GetBackgroundDrawList();
            bg.AddCircleFilled(screenPos, 5, ImGui.GetColorU32(worldService.DotColor));
            
            if (ImGui.IsMouseHoveringRect(screenPos - new Vector2(5, 5), 
                                          screenPos + new Vector2(5, 5)) && 
                !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
            {
                hoveredInFrame.Add(childObject);
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
                    worldService.SelectedObjects.Add(worldObj);
                }
            }
        }
    }
}
