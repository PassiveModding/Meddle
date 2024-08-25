using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;

namespace Meddle.Plugin.UI;

public class LayoutTab : ITab
{
    private readonly LayoutService layoutService;
    public MenuType MenuType => MenuType.Testing;
    private readonly Configuration config;
    private readonly SigUtil sigUtil;
    private readonly ExportService exportService;
    private readonly PluginState state;
    private readonly List<ParsedInstance> selectedInstances = new();

    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };
    
    public void Dispose()
    {
        state.OnInstanceClick -= HandleInstanceClick;
    }

    private void HandleInstanceClick(ParsedInstance instance)
    {
        var existing = selectedInstances.Find(x => x.Id == instance.Id);
        if (existing == null)
        {
            selectedInstances.Add(instance);
        }
        else
        {
            selectedInstances.Remove(existing);
            selectedInstances.Add(instance);
        }
    }
    
    public LayoutTab(LayoutService layoutService, Configuration config, 
                     SigUtil sigUtil, ExportService exportService,
                     PluginState state)
    {
        this.layoutService = layoutService;
        this.config = config;
        this.sigUtil = sigUtil;
        this.exportService = exportService;
        this.state = state;
        selectedTypes = Enum.GetValues<InstanceType>().ToList();
        this.state.OnInstanceClick += HandleInstanceClick;
    }

    public string Name => "Layout";
    public int Order => 5;

    private readonly List<InstanceType> selectedTypes;
    private ExportService.ModelExportProgress? exportProgress;
    private Task exportTask = Task.CompletedTask;

    public void Draw()
    {
        fileDialog.Draw();
        
        if (ImGui.CollapsingHeader("Options"))
        {
            var cutoff = config.WorldCutoffDistance;
            if (ImGui.DragFloat("Cutoff Distance", ref cutoff, 1, 0, 10000))
            {
                config.WorldCutoffDistance = cutoff;
                config.Save();
            }

            var dotColor = config.WorldDotColor;
            if (ImGui.ColorEdit4("Dot Color", ref dotColor, ImGuiColorEditFlags.NoInputs))
            {
                config.WorldDotColor = dotColor;
                config.Save();
            }

            var drawLayout = state.DrawLayout;
            if (ImGui.Checkbox("Draw Layout", ref drawLayout))
            {
                state.DrawLayout = drawLayout;
            }

            // imgui selectable
            if (ImGui.CollapsingHeader("Selected Types"))
            {
                foreach (var type in Enum.GetValues<InstanceType>())
                {
                    var selected = selectedTypes.Contains(type);
                    if (ImGui.Checkbox(type.ToString(), ref selected))
                    {
                        if (selected)
                        {
                            selectedTypes.Add(type);
                        }
                        else
                        {
                            selectedTypes.Remove(type);
                        }
                    }
                }
            }
        }
                
        if (exportProgress is {DistinctPaths: > 0} && exportTask?.IsCompleted == false)
        {
            var width = ImGui.GetContentRegionAvail().X;
            ImGui.Text($"Models parsed: {exportProgress.ModelsParsed} / {exportProgress.DistinctPaths}");
            ImGui.ProgressBar(exportProgress.ModelsParsed / (float)exportProgress.DistinctPaths, new Vector2(width, 0));
        }
        
        if (ImGui.CollapsingHeader("Current Layout"))
        {
            DrawLayout();
        }
        
        if (ImGui.CollapsingHeader("Selected Instances"))
        {
            DrawSelectedInstances();
        }
    }

    private void DrawSelectedInstances()
    {
        using var id = ImRaii.PushId("SelectedInstances");
        if (ImGui.Button("Export Selected"))
        {
            var defaultName = $"LayoutExport-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
            exportProgress = new ExportService.ModelExportProgress();
            fileDialog.SaveFolderDialog("Save Model", defaultName,
                (result, path) =>
                {
                    if (!result) return;
                    exportTask = Task.Run(async () =>
                    {
                        await exportService.Export(selectedInstances.ToArray(), exportProgress, path);
                    });
                }, Plugin.TempDirectory);
        }
        DrawInstanceTable(selectedInstances.ToArray(), ctx =>
        {
            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString()))
                {
                    selectedInstances.Remove(ctx);
                }
            }
        });
    }
    
    private void DrawInstanceTable(ParsedInstance[] instances, Action<ParsedInstance>? additionalOptions = null)
    {
        using var table = ImRaii.Table("##layoutTable", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Reorderable);
        ImGui.TableSetupColumn("Data", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Options");
        
        foreach (var instance in instances)
        {
            DrawInstance(instance, additionalOptions);
        }
    }

    private void DrawLayout()
    {
        using var id = ImRaii.PushId("Layout");
        var currentLayout = layoutService.GetWorldState();
        if (currentLayout == null)
            return;
        
        var local = sigUtil.GetLocalPosition();
        var allInstances = currentLayout
                           .SelectMany(x => x.Instances)
                           .Where(x => Vector3.Distance(x.Transform.Translation, local) < config.WorldCutoffDistance)
                           .Where(x => selectedTypes.Contains(x.Type))
                           .OrderBy(x => Vector3.Distance(x.Transform.Translation, local))
                           .ToArray();
        
        if (ImGui.Button("Export All"))
        {
            var defaultName = $"LayoutExport-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
            exportProgress = new ExportService.ModelExportProgress();
            fileDialog.SaveFolderDialog("Save Model", defaultName,
                (result, path) =>
                {
                    if (!result) return;
                    exportTask = Task.Run(async () =>
                    {
                        await exportService.Export(allInstances, exportProgress, path);
                    });
                }, Plugin.TempDirectory);
        }

        DrawInstanceTable(allInstances);
    }

    private void DrawInstance(ParsedInstance instance, Action<ParsedInstance>? additionalOptions = null, int depth = 0)
    {
        if (depth > 10)
        {
            ImGui.Text("Max depth reached");
            return;
        }
        
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        using var id = ImRaii.PushId(instance.Id);
        
        var infoHeader = instance switch
        {
            ParsedHousingInstance housingObject => $"{housingObject.Type} - {housingObject.Name}",
            ParsedBgPartsInstance bgObject => $"{bgObject.Type} - {bgObject.Path}",
            _ => $"{instance.Type}"
        };
        
        if (instance.Children.Count > 0)
        {
            var childTypeGroups = instance.Children.GroupBy(x => x.Type);
            // type[count], type[count]
            var childTypes = string.Join(", ", childTypeGroups.Select(x => $"{x.Key}[{x.Count()}]"));
            infoHeader += $" ({childTypes})";
        }

        var distance = Vector3.Distance(instance.Transform.Translation, sigUtil.GetLocalPosition());
        bool displayInner = ImGui.CollapsingHeader($"[{distance:F1}y] {infoHeader}###{instance.Id}");
        if (ImGui.IsItemHovered())
        {
            state.InvokeInstanceHover(instance);
        }
        
        if (displayInner)
        {
            if (instance is ParsedHousingInstance ho)
            {
                ImGui.Text($"Kind: {ho.Kind}");
                ImGui.Text($"Object Name: {ho.Name}");
                ImGui.Text($"Item Name: {ho.Item?.Name}");
                Vector4? color = ho.Stain == null ? null : ImGui.ColorConvertU32ToFloat4(ho.Stain.Color);
                if (color != null)
                {
                    ImGui.ColorButton("Stain", color.Value);
                }
                else
                {
                    ImGui.Text("No Stain");
                }
            }

            if (instance is ParsedBgPartsInstance bg)
            {
                ImGui.Text($"Path: {bg.Path}");
            }

            ImGui.Text($"Position: {instance.Transform.Translation}");
            ImGui.Text($"Rotation: {instance.Transform.Rotation}");
            ImGui.Text($"Scale: {instance.Transform.Scale}");
        }

        ImGui.TableSetColumnIndex(1);
        DrawExportAsGltf(instance);
        
        additionalOptions?.Invoke(instance);
        
        if (displayInner && instance.Children.Count > 0)
        {
            ImGui.TreePush();
            foreach (var obj in instance.Children)
            {
                DrawInstance(obj, additionalOptions, depth + 1);
            }
            ImGui.TreePop();
        }
    }
    
    private void DrawExportAsGltf(params ParsedInstance[] instances)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.FileExport.ToIconString()))
            {
                var defaultName = $"LayoutExport-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                exportProgress = new ExportService.ModelExportProgress();
                fileDialog.SaveFolderDialog("Save Model", defaultName,
                    (result, path) =>
                    {
                        if (!result) return;
                        exportTask = Task.Run(async () =>
                        {
                            await exportService.Export(instances, exportProgress, path);
                        });
                    }, Plugin.TempDirectory);
            }
        }
        
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Export as glTF");
        }
    }
}
