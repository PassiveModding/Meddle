using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public class WorldTab : ITab
{
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly ExportUtil exportUtil;
    private readonly ILogger<WorldTab> log;

    private readonly List<ObjectData> objects = new();
    private readonly PluginState pluginState;
    private readonly List<ObjectData> selectedObjects = new();
    private Task exportTask = Task.CompletedTask;

    public WorldTab(
        PluginState pluginState, IClientState clientState, ExportUtil exportUtil, ILogger<WorldTab> log,
        Configuration config)
    {
        this.pluginState = pluginState;
        this.clientState = clientState;
        this.exportUtil = exportUtil;
        this.log = log;
        this.config = config;
    }

    public bool DisplayTab => config.ShowAdvanced;

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name => "World";
    public int Order => 4;

    public void Draw()
    {
        if (!pluginState.InteropResolved) return;
        var position = clientState.LocalPlayer?.Position ?? Vector3.Zero;

        if (ImGui.Button("Parse world objects"))
        {
            ParseWorld();
            log.LogInformation("Parsed {ObjectCount} objects", objects.Count);
        }

        var availHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("ObjectTable", new Vector2(0, availHeight / 4), true);
        foreach (var obj in objects.Where(x => x.Path.EndsWith(".mdl"))
                                   .OrderBy(o => Vector3.Distance(o.Position, position)))
        {
            var distance = Vector3.Distance(obj.Position, position);
            if (ImGui.Selectable($"[{obj.Type}][{distance:F1}y] {obj.Path}"))
            {
                selectedObjects.Add(obj);
                // set clipboard
                ImGui.SetClipboardText(obj.Path);
            }
        }

        ImGui.EndChild();
        /*ImGui.BeginChild("TeraTable", new Vector2(0, availHeight / 4), true);
        foreach (var obj in objects.Where(x => x.Path.EndsWith(".tera")).OrderBy(o => Vector3.Distance(o.Position, position)))
        {
            var distance = Vector3.Distance(obj.Position, position);
            if (ImGui.Selectable($"[{obj.Type}][{distance:F1}y] {obj.Path}"))
            {
                selectedObjects.Add(obj);
                // set clipboard
                ImGui.SetClipboardText(obj.Path);
            }
        }
        ImGui.EndChild();*/

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
                var resources = new List<Resource>();

                // NOTE: Position is players current position, so if they move the objects will be exported relative to that position
                foreach (var obj in selectedObjectArr)
                {
                    if (obj.Path.EndsWith(".mdl"))
                    {
                        resources.Add(new Resource(obj.Path, obj.Position, obj.Rotation, obj.Scale));
                    }
                    else if (obj.Path.EndsWith(".tera"))
                    {
                        /*
                        var fileData = pack.GetFile(obj.Path);
                        if (fileData != null)
                        {
                            var teraFile = new TeraFile(fileData.Value.file.RawData);
                            // bg/ffxiv/..../bgplate/terrain.tera
                            // need to get bg.lgb file
                            // bg/ffxiv/..../level/bg.lgb
                            var bgLgbPath = obj.Path.Replace("bgplate/terrain.tera", "level/bg.lgb");
                            var bgLgbData = pack.GetFile(bgLgbPath);
                            if (bgLgbData != null)
                            {
                                var lgbFile = new LgbFile(bgLgbData.Value.file.RawData);
                            }
                        }*/
                    }
                }

                exportTask = Task.Run(() => exportUtil.ExportResource(resources.ToArray(), position));
            }

            ImGui.EndDisabled();
        }

        if (exportTask.IsFaulted)
        {
            var exception = exportTask.Exception;
            ImGui.Text($"Export failed: {exception?.Message}");
        }
    }

    public unsafe void ParseWorld()
    {
        objects.Clear();
        selectedObjects.Clear();

        var world = World.Instance();
        if (world == null) return;
        foreach (var childObject in world->ChildObjects)
        {
            if (childObject == null) continue;
            var type = childObject->GetObjectType();
            if (type == ObjectType.BgObject)
            {
                var bgObject = (BgObject*)childObject;
                if (bgObject->ResourceHandle == null) continue;
                var path = bgObject->ResourceHandle->FileName.ToString();
                objects.Add(new ObjectData(ObjectType.BgObject, path, childObject->Position, childObject->Rotation,
                                           childObject->Scale));
            }
            else if (type == ObjectType.Terrain)
            {
                var terrain = (Terrain*)childObject;
                if (terrain->ResourceHandle == null) continue;
                var path = terrain->ResourceHandle->FileName.ToString();
                objects.Add(new ObjectData(ObjectType.Terrain, path, childObject->Position, childObject->Rotation,
                                           childObject->Scale));
            }
        }
    }

    private record ObjectData(ObjectType Type, string Path, Vector3 Position, Quaternion Rotation, Vector3 Scale);
}

[StructLayout(LayoutKind.Explicit, Size = 0xD0)]
public struct BgObject
{
    [FieldOffset(0x90)]
    public unsafe ResourceHandle* ResourceHandle;
}

[StructLayout(LayoutKind.Explicit, Size = 0x1F0)]
public struct Terrain
{
    [FieldOffset(0x90)]
    public unsafe ResourceHandle* ResourceHandle;
}
