using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public class WorldTab : ITab
{
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly WorldService worldService;
    private readonly ILogger<WorldTab> log;
    private readonly ExportService exportService;
    private readonly ParseService parseService;
    private ExportService.ModelExportProgress? exportProgress;
    private Task? exportTask;
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
    }

    public string Name => "World";
    public int Order => 4;
    
    public void Draw()
    {
        worldService.ShouldDrawOverlay = true;
        fileDialog.Draw();
        ImGui.Text("This is a testing menu, functionality may not work as expected.");
        ImGui.Text("Pixel shader approximation for most non-character shaders is not properly supported at this time.");
        ImGui.Text("Items added from the overlay will keep a snapshot of their transform at the time of being added.");
        
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

        if (ImGui.Button("Add all in range"))
        {
            worldService.ShouldAddAllInRange = true;
        }
        
        if (exportProgress is {DistinctPaths: > 0} && exportTask?.IsCompleted == false)
        {
            var width = ImGui.GetContentRegionAvail().X;
            ImGui.Text($"Models parsed: {exportProgress.ModelsParsed} / {exportProgress.DistinctPaths}");
            ImGui.ProgressBar(exportProgress.ModelsParsed / (float)exportProgress.DistinctPaths, new Vector2(width, 0));
        }

        if (ImGui.Button("Export All"))
        {
            exportProgress = new ExportService.ModelExportProgress();
            var folderName = "ExportedModels";
            fileDialog.SaveFolderDialog("Save Model", folderName,
                    (result, path) =>
                    {
                        if (!result) return;
                        exportTask = Task.Run(async () =>
                        {
                            var bgObjects = worldService.SelectedObjects.Select(x => x.Value)
                                                        .OfType<WorldService.BgObjectSnapshot>()
                                                        .Select(x => (new Transform(x.Transform), x.Path)).ToArray();
                            await exportService.Export(bgObjects, exportProgress, path);
                        });
                    }, Plugin.TempDirectory);
        }
        
        using var modelTable = ImRaii.Table("##worldObjectTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Data", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        
        foreach (var (ptr, obj) in worldService.SelectedObjects.ToArray())
        {
            using var objId = ImRaii.PushId(ptr);
            switch (obj)
            {
                case WorldService.BgObjectSnapshot bgObj:
                    DrawBgObject(ptr, bgObj);
                    break;
                default:
                    DrawUnknownObject(ptr, obj);
                    break;
            }
        }
    }

    private void DrawUnknownObject(nint ptr, WorldService.ObjectSnapshot obj)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            // disable button but still show tooltip
            using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.5f))
            {
                ImGui.Button(FontAwesomeIcon.FileExport.ToIconString());
            }
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.PushFont(UiBuilder.DefaultFont))
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Exporting this object type from the world tab is currently not supported.");
                    ImGui.EndTooltip();
                }
            }
            
            ImGui.SameLine();
            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString()))
            {
                worldService.SelectedObjects.Remove(ptr);
            }
        }
        ImGui.TableSetColumnIndex(1);
        if (ImGui.CollapsingHeader($"{obj.Type} ({ptr:X8})##{ptr}"))
        {
            UiUtil.Text($"Address: {ptr:X8}", $"{ptr:X8}");
            ImGui.Text($"Position: {obj.Position}");
            ImGui.Text($"Rotation: {obj.Rotation}");
            ImGui.Text($"Scale: {obj.Scale}");
        }
    }

    private void DrawBgObject(nint ptr, WorldService.BgObjectSnapshot obj)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
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
                worldService.SelectedObjects.Remove(ptr);
            }
        }

        if (ImGui.BeginPopupContextItem("ExportBgObjectPopup"))
        {
            if (ImGui.MenuItem("Export as mdl"))
            {
                var fileName = Path.GetFileName(obj.Path);
                fileDialog.SaveFileDialog("Save Model", "Model File{.mdl}", fileName, ".mdl",
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
                    }, Plugin.TempDirectory);
            }

            if (ImGui.MenuItem("Export as glTF"))
            {
                exportProgress = new ExportService.ModelExportProgress();
                var folderName = Path.GetFileNameWithoutExtension(obj.Path);
                fileDialog.SaveFolderDialog("Save Model", folderName,
                    (result, path) =>
                    {
                        if (!result) return;
                        exportTask = Task.Run(async () =>
                        {
                            await exportService.Export([(new Transform(obj.Transform), obj.Path)], exportProgress, path);
                        });
                    }, Plugin.TempDirectory);
            }

            ImGui.EndPopup();
        }
        
        ImGui.TableSetColumnIndex(1);
        if (ImGui.CollapsingHeader($"{obj.Path} ({ptr:X8})##{ptr}"))
        {
            UiUtil.Text($"Address: {ptr:X8}", $"{ptr:X8}");
            ImGui.Text($"Position: {obj.Position}");
            ImGui.Text($"Rotation: {obj.Rotation}");
            ImGui.Text($"Scale: {obj.Scale}");
        }
    }
}
