﻿using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow : ITab
{
    private readonly ComposerFactory composerFactory;
    private readonly Configuration config;
    private readonly SqPack dataManager;

    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };

    private readonly LayoutService layoutService;
    private readonly ILogger<LayoutWindow> log;
    private readonly Dictionary<string, MdlFile?> mdlCache = new();
    private readonly Dictionary<string, MtrlFile?> mtrlCache = new();
    private readonly ResolverService resolverService;
    private readonly Dictionary<nint, ParsedInstance> selectedInstances = new();
    private readonly Dictionary<string, ShpkFile?> shpkCache = new();
    private readonly SigUtil sigUtil;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    private CancellationTokenSource cancelToken = new();
    private ParsedInstance[] currentLayout = [];
    private Vector3 currentPos;
    private Action? drawExportSettingsCallback;
    private Task exportTask = Task.CompletedTask;
    private InstanceComposer? instanceComposer;

    private string? lastError;
    private string search = "";
    private bool shouldUpdateState = true;
    private bool requestedPopup;

    public LayoutWindow(
        LayoutService layoutService,
        Configuration config,
        SigUtil sigUtil,
        ILogger<LayoutWindow> log,
        TextureCache textureCache,
        ITextureProvider textureProvider,
        ResolverService resolverService,
        ComposerFactory composerFactory,
        SqPack dataManager)
    {
        this.layoutService = layoutService;
        this.config = config;
        this.sigUtil = sigUtil;
        this.log = log;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
        this.resolverService = resolverService;
        this.composerFactory = composerFactory;
        this.dataManager = dataManager;
    }


    public string Name => "Layout";
    public int Order => (int)WindowOrder.Layout;
    public MenuType MenuType => MenuType.Default;

    public void Draw()
    {
        try
        {
            DrawProgress();

            if (shouldUpdateState)
            {
                SetupCurrentState();
                shouldUpdateState = false;
            }

            if (config.LayoutConfig.DrawOverlay)
            {
                shouldUpdateState = true;
                DrawOverlayWindow(out _, out var selected);
                foreach (var group in selected)
                {
                    selectedInstances[group.Id] = group;
                }
            }

            DrawOptions();

            if (ImGui.CollapsingHeader("Selected"))
            {
                DrawSelected();
            }

            if (ImGui.CollapsingHeader("All"))
            {
                DrawAll();
            }

            fileDialog.Draw();

            if (requestedPopup)
            {
                ImGui.OpenPopup("ExportSettingsPopup");
                requestedPopup = false;
            }

            if (drawExportSettingsCallback != null)
            {
                drawExportSettingsCallback();
            }
        }
        catch (Exception e)
        {
            if (e.ToString() != lastError)
            {
                lastError = e.ToString();
                log.LogError(e, "Failed to draw Layout window");
            }

            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Failed to draw Layout window");
            ImGui.TextWrapped(e.ToString());
        }
    }
    private void DrawAll()
    {

        shouldUpdateState = true;
        ImGui.InputText("Search", ref search, 100);

        using var id = ImRaii.PushId("layoutTable");
        var set = currentLayout
                  .Where(x =>
                  {
                      // keep terrain regardless of distance
                      if (x is ParsedTerrainInstance) return true;
                      return Vector3.Distance(x.Transform.Translation, currentPos) < config.WorldCutoffDistance;
                  })
                  .Where(x => config.LayoutConfig.DrawTypes.HasFlag(x.Type));
        if (config.LayoutConfig.OrderByDistance)
        {
            set = set.OrderBy(x => Vector3.Distance(x.Transform.Translation, sigUtil.GetLocalPosition()));
        }

        if (config.LayoutConfig.HideOffscreenCharacters)
        {
            set = set.Where(x => x is not ParsedCharacterInstance {Visible: false});
        }


        if (!string.IsNullOrWhiteSpace(search))
        {
            set = set.Where(x =>
            {
                if (x is ISearchableInstance si)
                {
                    return si.Search(search);
                }

                return true;
            });
        }

        var items = set.ToArray();


        var count = 0;
        foreach (var item in items)
        {
            if (item is ParsedSharedInstance shared)
            {
                count += shared.Flatten().Length;
            }
            else
            {
                count++;
            }
        }

        ExportButton($"Export {items.Length} instance(s)", items);
        ImGui.SameLine();
        if (ImGui.Button($"Add {items.Length} instance(s) to selection"))
        {
            foreach (var item in items)
            {
                selectedInstances[item.Id] = item;
            }
        }
        ImGui.SameLine();
        ImGui.Text($"Total: {count}");

        DrawInstanceTable(items, DrawLayoutButtons);
    }
    private void DrawSelected()
    {

        if (selectedInstances.Count == 0)
        {
            ImGui.Text("No instances selected");
        }

        using var disabled = ImRaii.Disabled(selectedInstances.Count == 0);
        using var id = ImRaii.PushId("selectedTable");
        ExportButton($"Export {selectedInstances.Count} instance(s)", selectedInstances.Values);

        ImGui.SameLine();
        if (ImGui.Button("Clear all"))
        {
            selectedInstances.Clear();
        }

        DrawInstanceTable(selectedInstances.Values.ToArray(), DrawSelectedButtons);
    }

    public void Dispose()
    {
        cancelToken.Cancel();
    }

    private void SetupCurrentState()
    {
        layoutService.RequestUpdate = true;
        currentLayout = layoutService.LastState ?? [];
        currentPos = sigUtil.GetLocalPosition();
    }


    private void DrawProgress()
    {
        if (exportTask.IsFaulted)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Export failed");
            ImGui.TextWrapped(exportTask.Exception?.ToString());
        }
        if (instanceComposer == null) return;
        if (exportTask.IsCompleted) return;
        
        var progress = instanceComposer.Progress;
        ImGui.Text($"Exporting {progress.Progress} of {progress.Total}");
        ImGui.ProgressBar(progress.Progress / (float)progress.Total, new Vector2(-1, 0));
        
        if (ImGui.Button("Cancel"))
        {
            cancelToken.Cancel();
        }
    }

    private void DrawExportSingle(ParsedInstance instance)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ExportButton(FontAwesomeIcon.FileExport.ToIconString(), [instance]);
        }
    }

    private void DrawSelectedButtons(Stack<ParsedInstance> stack, ParsedInstance instance)
    {
        DrawExportSingle(instance);
        if (instance is IResolvableInstance {IsResolved: false} resolvableInstance)
        {
            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(FontAwesomeIcon.Redo.ToIconString()))
                {
                    resolvableInstance.Resolve(resolverService);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Resolve this instance");
            }
        }

        if (stack.Count > 1)
        {
            return;
        }

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString()))
            {
                selectedInstances.Remove(instance.Id);
            }
        }
    }

    private void DrawLayoutButtons(Stack<ParsedInstance> stack, ParsedInstance instance)
    {
        DrawExportSingle(instance);
        if (stack.Count > 1)
        {
            return;
        }

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            // if contains, refresh icon, if not, plus icon
            var icon = selectedInstances.ContainsKey(instance.Id) ? FontAwesomeIcon.Redo : FontAwesomeIcon.Plus;
            if (ImGui.Button(icon.ToIconString()))
            {
                selectedInstances[instance.Id] = instance;
            }
        }
    }

    private void ExportButton(string text, IEnumerable<ParsedInstance> instances)
    {
        using var disabled = ImRaii.Disabled(!exportTask.IsCompleted);
        if (ImGui.Button(text))
        {
            requestedPopup = true;
            var instancesArray = instances.ToArray();
            resolverService.ResolveInstances(instancesArray);
            drawExportSettingsCallback = () => DrawExportSettings(instancesArray);
        }
    }

    private void DrawExportSettings(ParsedInstance[] instances)
    {
        if (!exportTask.IsCompleted)
        {
            log.LogWarning("Export task already running");
            return;
        }

        if (!ImGui.BeginPopup("ExportSettingsPopup", ImGuiWindowFlags.AlwaysAutoResize)) return;
        try
        {
            var cacheFileTypes = config.ExportConfig.CacheFileTypes;
            if (EnumExtensions.DrawEnumCombo("Cache Files", ref cacheFileTypes))
            {
                config.ExportConfig.CacheFileTypes = cacheFileTypes;
                config.Save();
            }

            var exportPose = config.ExportConfig.ExportPose;
            if (ImGui.Checkbox("Export pose", ref exportPose))
            {
                config.ExportConfig.ExportPose = exportPose;
                config.Save();
            }

            if (ImGui.Button("Export"))
            {
                var configClone = config.ExportConfig.Clone();
                var defaultName = $"InstanceExport-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                cancelToken = new CancellationTokenSource();
                fileDialog.SaveFolderDialog("Save Instances", defaultName,
                                            (result, path) =>
                                            {
                                                if (!result) return;
                                                SetExportTask(instances, path, configClone, cancelToken);
                                            }, config.ExportDirectory);

                drawExportSettingsCallback = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                drawExportSettingsCallback = null;
                ImGui.CloseCurrentPopup();
            }
        } 
        finally
        {
            ImGui.EndPopup();
        }
    }

    private void SetExportTask(ParsedInstance[] instances, string path, Configuration.ExportConfiguration configClone, CancellationTokenSource cancellationTokenSource)
    {
        exportTask = Task.Run(() =>
        {
            log.LogDebug("Exporting {count} instances to {path}, current task id: {taskId}", instances.Length, path, Task.CurrentId);
            try
            {
                var composer = composerFactory.CreateComposer(instances,
                                                              path,
                                                              configClone,
                                                              cancellationTokenSource.Token);
                instanceComposer = composer;
                composer.Compose();
                Process.Start("explorer.exe", path);
            } 
            finally
            {
                instanceComposer = null;
            }
        }, cancellationTokenSource.Token);
    }
}
