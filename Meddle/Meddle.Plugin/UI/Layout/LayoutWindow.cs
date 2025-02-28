using System.Diagnostics;
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
using Meddle.Plugin.Utils;
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
    private Vector3 searchOrigin;
    private Vector3 playerPosition;
    private Action? drawExportSettingsCallback;
    private Task exportTask = Task.CompletedTask;
    private ExportProgress? progress;

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
            DrawProgress(exportTask, progress, cancelToken);

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
                      // if (x is ParsedCharacterInstance characterInstance)
                      // {
                      //       if (config.LayoutConfig.ExcludeParented)
                      //       {
                      //           return characterInstance.Parent == null;
                      //       }
                      // }
                      
                      // keep terrain regardless of distance
                      if (x is ParsedTerrainInstance) return true;
                      return Vector3.Distance(x.Transform.Translation, searchOrigin) < config.WorldCutoffDistance;
                  })
                  .Where(x => config.LayoutConfig.DrawTypes.HasFlag(x.Type));
        if (config.LayoutConfig.OrderByDistance)
        {
            set = set.OrderBy(x => Vector3.Distance(x.Transform.Translation, searchOrigin));
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

    private unsafe void SetupCurrentState()
    {
        layoutService.RequestUpdate = true;
        currentLayout = layoutService.LastState ?? [];
        playerPosition = sigUtil.GetLocalPosition();
        if (config.LayoutConfig.OriginAdjustment == OriginAdjustment.Player)
        {
            searchOrigin = playerPosition;
        }
        else if (config.LayoutConfig.OriginAdjustment == OriginAdjustment.Camera)
        {
            searchOrigin = sigUtil.GetCamera()->Position;
        }
        else if (config.LayoutConfig.OriginAdjustment == OriginAdjustment.Origin)
        {
            searchOrigin = Vector3.Zero;
        }
        else
        {
            searchOrigin = Vector3.Zero;
        }
    }


    public static void DrawProgress(Task exportTask, ExportProgress? progress, CancellationTokenSource cancelToken)
    {
        if (exportTask.IsFaulted)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Export failed");
            ImGui.TextWrapped(exportTask.Exception?.ToString());
        }
        
        if (exportTask.IsCompleted) return;

        if (progress != null)
        {
            DrawProgressRecursive(progress);
        }

        if (ImGui.Button("Cancel"))
        {
            cancelToken.Cancel();
        }
        
        return;

        void DrawProgressRecursive(ExportProgress rProgress)
        {
            if (rProgress.IsComplete) return;
            ImGui.Text($"Exporting {rProgress.Progress} of {rProgress.Total}");
            ImGui.ProgressBar(rProgress.Progress / (float)rProgress.Total, new Vector2(-1, 0), rProgress.Name ?? "");
            if (rProgress.Children.Count > 0)
            {
                using var indent = ImRaii.PushIndent();
                foreach (var child in rProgress.Children)
                {
                    if (child == rProgress)
                    {
                        ImGui.Text("Recursive progress detected, skipping");
                        continue;
                    }
                    DrawProgressRecursive(child);
                }
            }
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
            var flags = UiUtil.ExportConfigDrawFlags.None;
            if (instances.Length > 1)
            {
                flags |= UiUtil.ExportConfigDrawFlags.HideExportPose;
            }
            if (UiUtil.DrawExportConfig(config.ExportConfig, flags))
            {
                config.Save();
            }

            if (ImGui.Button("Export"))
            {
                var configClone = config.ExportConfig.Clone();
                if (flags.HasFlag(UiUtil.ExportConfigDrawFlags.HideExportPose))
                {
                    // Force export pose to true if multiple instances are selected
                    configClone.ExportPose = true;
                }

                var filteredInstances = new List<ParsedInstance>();
                foreach (var instance in instances)
                {
                    if (instance is ParsedCharacterInstance {Parent: not null} characterInstance)
                    {
                        var parentMatch = instances.FirstOrDefault(c => c.Id == characterInstance.Parent.Id);
                        if (parentMatch != null)
                        {
                            string parentName;
                            if (parentMatch is ParsedCharacterInstance parentCharacter)
                            {
                                parentName = parentCharacter.Name;
                            }
                            else
                            {
                                parentName = parentMatch.Type.ToString();
                            }
                            
                            log.LogWarning("Parented instance {id} is already in the list as a child of {parentName}, skipping", characterInstance.Id, parentName);
                            continue;
                        }
                    }
                    
                    filteredInstances.Add(instance);
                }
                
                var filteredInstanceArray = filteredInstances.ToArray();
                
                var defaultName = $"InstanceExport-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                cancelToken = new CancellationTokenSource();
                fileDialog.SaveFolderDialog("Save Instances", defaultName,
                                            (result, path) =>
                                            {
                                                if (!result) return;
                                                exportTask = Task.Run(() =>
                                                {
                                                    var composer = composerFactory.CreateComposer(path,
                                                                                                  configClone,
                                                                                                  cancelToken.Token);
                                                    progress = new ExportProgress(filteredInstanceArray.Length, "Instances");
                                                    composer.Compose(filteredInstanceArray, progress);
                                                    Process.Start("explorer.exe", path);
                                                }, cancelToken.Token);
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
}
