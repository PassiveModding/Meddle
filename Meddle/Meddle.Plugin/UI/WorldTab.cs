using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Meddle.Plugin.UI;

public class WorldTab : ITab
{
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly WorldService worldService;
    private readonly ILogger<WorldTab> log;
    private readonly ExportService exportService;
    private readonly ParseService parseService;
    private readonly SqPack pack;
    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };
    
    public WorldTab(
        IClientState clientState, 
        ILogger<WorldTab> log,
        ExportService exportService,
        ParseService parseService,
        SqPack pack,
        Configuration config,
        WorldService worldService)
    {
        this.clientState = clientState;
        this.log = log;
        this.exportService = exportService;
        this.parseService = parseService;
        this.pack = pack;
        this.config = config;
        this.worldService = worldService;
    }

    public bool DisplayTab => config.ShowTesting;

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name => "World";
    public int Order => 4;
    
    public unsafe void Draw()
    {
        worldService.ShouldDrawOverlay = true;
        fileDialog.Draw();
        ImGui.Text("This is a testing menu, functionality may not work as expected.");

        // selector for cutoff distance
        if (ImGui.DragFloat("Cutoff Distance", ref worldService.CutoffDistance, 1, 0, 10000))
        {
            worldService.SaveOptions();
        }

        if (ImGui.ColorEdit4("Dot Color", ref worldService.DotColor, ImGuiColorEditFlags.NoInputs))
        {
            worldService.SaveOptions();
        }
        
        
        if (ImGui.Button("Clear"))
        {
            worldService.SelectedObjects.Clear();
        }

        if (ImGui.Button("Export All"))
        {
            var folderName = "ExportedModels";
            fileDialog.SaveFolderDialog("Save Model", folderName,
                    (result, path) =>
                    {
                        if (!result) return;
                        var objects = worldService.SelectedObjects.ToArray();
                        var groups = new List<(Transform, MdlFileGroup)>();
                        foreach (var selectedObject in objects)
                        {
                            if (selectedObject == null) continue;
                            if (selectedObject.Value == null) continue;
                            var obj = selectedObject.Value;
                            var objType = obj->GetObjectType();
                            if (!WorldService.IsSupportedObject(objType))
                            {
                                log.LogWarning("Skipping object type {Type}", objType);
                                continue;
                            }
                            var bgObj = (BgObject*)obj;
                            var transformMatrix = Matrix4x4.CreateScale(obj->Scale) *
                                                 Matrix4x4.CreateFromQuaternion(obj->Rotation) *
                                                 Matrix4x4.CreateTranslation(obj->Position);
                            var transform = new Transform(transformMatrix);
                            var data = parseService.ParseBgObject(bgObj);
                            groups.Add((transform, data));
                        }
                        
                        Task.Run(() => exportService.Export(groups.ToArray(), path));
                    }, Plugin.TempDirectory);
        }
        
        using var modelTable = ImRaii.Table("##worldObjectTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Data", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        
        foreach (var selectedObject in worldService.SelectedObjects.ToArray())
        {
            if (selectedObject == null) continue;
            if (selectedObject.Value == null) continue;
            var obj = selectedObject.Value;
            using var objId = ImRaii.PushId((nint)obj);
            var objType = obj->GetObjectType();
            if (objType == ObjectType.BgObject)
            {            
                DrawBgObject((BgObject*)obj);
            }
            else
            {
                DrawUnknownObject(obj);
            }
        }
    }

    private unsafe void DrawUnknownObject(Object* obj)
    {
        if (obj == null) return;
        var type = obj->GetObjectType();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using (ImRaii.Disabled())
            {
                ImGui.Button(FontAwesomeIcon.FileExport.ToIconString());
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("This object will not be exported.");
                    ImGui.EndTooltip();
                }
            }
            
            ImGui.SameLine();
            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString()))
            {
                worldService.SelectedObjects.Remove(obj);
            }
        }
        ImGui.TableSetColumnIndex(1);
        if (ImGui.CollapsingHeader($"{type} {(nint)obj:X8}"))
        {
            UiUtil.Text($"Address: {(nint)obj:X8}", $"{(nint)obj:X8}");
        }
    }

    private unsafe void DrawBgObject(BgObject* obj)
    {
        if (obj == null) return;
        var draw = (DrawObject*)obj;
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        var objPath = WorldService.GetBgObjectPath(obj);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.FileExport.ToIconString()))
            {
                ImGui.OpenPopup("ExportBgObjectPopup");
            }
            
            // remove icon
            ImGui.SameLine();
            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString()))
            {
                worldService.SelectedObjects.Remove((Object*)obj);
            }
        }

        if (ImGui.BeginPopupContextItem("ExportBgObjectPopup"))
        {
            if (ImGui.MenuItem("Export as mdl"))
            {
                var defaultFileName = Path.GetFileName(objPath);
                fileDialog.SaveFileDialog("Save Model", "Model File{.mdl}", defaultFileName, ".mdl",
                    (result, path) =>
                    {
                        if (!result) return;
                        var data = pack.GetFileOrReadFromDisk(path);
                        if (data == null)
                        {
                            log.LogError("Failed to get model data from pack or disk for {FileName}", path);
                            return;
                        }

                        File.WriteAllBytes(path, data);
                    });
            }

            if (ImGui.MenuItem("Export as glTF"))
            {
                var folderName = Path.GetFileNameWithoutExtension(objPath);
                fileDialog.SaveFolderDialog("Save Model", folderName,
                    (result, path) =>
                    {
                        if (!result) return;
                        var transformMatrix = Matrix4x4.CreateScale(draw->Scale) *
                                             Matrix4x4.CreateFromQuaternion(draw->Rotation) *
                                             Matrix4x4.CreateTranslation(draw->Position);
                        var transform = new Transform(transformMatrix);
                        var data = parseService.ParseBgObject(obj);
                        Task.Run(() => exportService.Export([
                            (transform, data)
                        ], path));
                    }, Plugin.TempDirectory);
            }

            ImGui.EndPopup();
        }
        
        ImGui.TableSetColumnIndex(1);
        if (ImGui.CollapsingHeader(objPath))
        {
            UiUtil.Text($"Address: {(nint)obj:X8}", $"{(nint)obj:X8}");
            ImGui.Text($"Pos: {draw->Position}");
            ImGui.Text($"Rot: {draw->Rotation}");
            ImGui.Text($"Scale: {draw->Scale}");
        }
    }
}


