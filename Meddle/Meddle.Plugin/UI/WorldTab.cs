using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using ImGuiNET;
using Meddle.Plugin.Utils;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Meddle.Plugin.UI;

public class WorldTab : ITab
{
    private readonly InteropService interop;
    private readonly IClientState clientState;
    private readonly ExportUtil exportUtil;

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name => "World";
    public int Order => 4;
    
    public WorldTab(InteropService interop, IClientState clientState, ExportUtil exportUtil)
    {
        this.interop = interop;
        this.clientState = clientState;
        this.exportUtil = exportUtil;
    }

    private readonly List<(ObjectType Type, string Path, float Distance, Vector3 Position, Quaternion Rotation, Vector3 Scale)> objects = new();
    private readonly List<(ObjectType Type, string Path, float Distance, Vector3 Position, Quaternion Rotation, Vector3 Scale)> selectedObjects = new();
    private Task exportTask = Task.CompletedTask;
    
    public unsafe void Draw()
    {
        if (!interop.IsResolved) return;
        if (clientState.LocalPlayer == null) return;
        var position = clientState.LocalPlayer.Position;
        
        if (ImGui.Button("Parse world objects"))
        {
            objects.Clear();
            selectedObjects.Clear();
        
            var world = World.Instance();
            if (world == null) return;

            foreach (var worldObject in world->ChildObjects)
            {
                if (worldObject == null) continue;
                var data = GetObjectData(worldObject);
                if (data != null)
                {
                    var distance = Vector3.Distance(position, worldObject->Position);
                    objects.Add((data.Value.Type, data.Value.Path, distance, worldObject->Position, worldObject->Rotation, worldObject->Scale));
                }
            }
        }
        
        var availHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("ObjectTable", new Vector2(0, availHeight/2), true);
        foreach (var obj in objects.OrderBy(o => o.Distance))
        {
            if (ImGui.Selectable($"[{obj.Type}][{obj.Distance:F1}y] {obj.Path}"))
            {
                selectedObjects.Add(obj);
            }
        }
        ImGui.EndChild();

        if (selectedObjects.Count > 0)
        {
            var selectedObjectArr = selectedObjects.ToArray();
            foreach (var selectedObject in selectedObjectArr)
            {
                if (ImGui.Selectable($"{selectedObject.Path}"))
                {
                    selectedObjects.Remove(selectedObject);
                }
            }
            
            ImGui.BeginDisabled(!exportTask.IsCompleted);
            if (ImGui.Button("Export") && exportTask.IsCompleted)
            {
                // NOTE: Position is players current position, so if they move the objects will be exported relative to that position
                var resources = selectedObjectArr.Select(o => new ExportUtil.Resource(o.Path, o.Position, o.Rotation, o.Scale)).ToArray();
                exportTask = Task.Run(() => exportUtil.ExportResource(resources, position));
            }
            ImGui.EndDisabled();
        }
        
        if (exportTask.IsFaulted)
        {
            var exception = exportTask.Exception;
            ImGui.Text($"Export failed: {exception?.Message}");
        }
    }
    
    private unsafe (ObjectType Type, string Path)? GetObjectData(Object* worldObject)
    {
        var type = worldObject->GetObjectType();
        if (type == ObjectType.BgObject)
        {
            var bgObject = (BgObject*)worldObject;
            var resourceHandle = bgObject->ResourceHandle;
            if (resourceHandle == null) return null;
            var resource = resourceHandle->FileName.ToString();
            return (type, resource);
        }

        if (type == ObjectType.Terrain)
        {
            var terrain = (Terrain*)worldObject;
            var resourceHandle = terrain->ResourceHandle;
            if (resourceHandle == null) return null;
            var resource = resourceHandle->FileName.ToString();
            return (type, resource);
        }

        return null;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 0xD0)]
public struct BgObject {
    [FieldOffset(0x90)] public unsafe ResourceHandle* ResourceHandle;
}

[StructLayout(LayoutKind.Explicit, Size = 0x1F0)]
public struct Terrain {
    [FieldOffset(0x90)] public unsafe ResourceHandle* ResourceHandle;
}
