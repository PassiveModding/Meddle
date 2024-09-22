using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow : ITab
{
    private readonly LayoutService layoutService;
    private readonly Configuration config;
    private readonly SigUtil sigUtil;
    private readonly ILogger<LayoutWindow> log;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    private readonly ResolverService resolverService;
    private readonly ComposerFactory composerFactory;
    private readonly SqPack dataManager;

    
    public string Name => "Layout";
    public int Order => (int) WindowOrder.Layout;
    public MenuType MenuType => MenuType.Default;
    
    private ProgressEvent? progress;
    private Task exportTask = Task.CompletedTask;
    private CancellationTokenSource cancelToken = new();
    private ParsedInstance[] currentLayout = [];
    private readonly Dictionary<nint, ParsedInstance> selectedInstances = new();
    private Vector3 currentPos;

    private string? lastError;

    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };

    public LayoutWindow(
        LayoutService layoutService,
        Configuration config,
        SigUtil sigUtil,
        ILogger<LayoutWindow> log,
        TextureCache textureCache,
        ITextureProvider textureProvider,
        ResolverService resolverService,
        ComposerFactory composerFactory,
        SqPack dataManager)// : base("Layout")
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

    private void SetupCurrentState()
    {
        currentLayout = layoutService.LastState ?? [];
        currentPos = sigUtil.GetLocalPosition();
    }

    private bool shouldUpdateState = true;
    private string search = "";

    public void Draw()
    {
        try
        {
            if (!exportTask.IsCompleted && progress != null)
            {
                ImGui.Text($"Exporting {progress.Progress} of {progress.Total}");
                ImGui.ProgressBar(progress.Progress / (float)progress.Total, new Vector2(-1, 0), progress.Name);

                var subProgress = progress.SubProgress;
                while (subProgress != null)
                {
                    ImGui.Text($"Exporting {subProgress.Progress} of {subProgress.Total}");
                    ImGui.ProgressBar(subProgress.Progress / (float)subProgress.Total, new Vector2(-1, 0), subProgress.Name);
                    subProgress = subProgress.SubProgress;
                }
                
                if (ImGui.Button("Cancel"))
                {
                    cancelToken.Cancel();
                }
            }

            if (exportTask.IsFaulted)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Export failed");
                ImGui.TextWrapped(exportTask.Exception?.ToString());
            }

            if (shouldUpdateState)
            {
                layoutService.LastDrawTime = DateTime.Now;
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

            if (ImGui.CollapsingHeader("All"))
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

            fileDialog.Draw();
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

    private void DrawExportSingle(ParsedInstance instance)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ExportButton(FontAwesomeIcon.FileExport.ToIconString(), new[] {instance});
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
            InstanceExport(instances.ToArray());
        }
    }

    private void InstanceExport(ParsedInstance[] instances)
    {
        if (!exportTask.IsCompleted)
        {
            log.LogWarning("Export task already running");
            return;
        }

        resolverService.ResolveInstances(instances);

        var defaultName = $"InstanceExport-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
        var currentExportType = config.LayoutConfig.ExportType;
        cancelToken = new CancellationTokenSource();
        fileDialog.SaveFolderDialog("Save Instances", defaultName,
                                    (result, path) =>
                                    {
                                        if (!result) return;
                                        exportTask = Task.Run(() =>
                                        {
                                            var composer = composerFactory.CreateComposer(instances, 
                                                Path.Combine(path, "cache"),
                                                x => progress = x, cancelToken.Token);
                                            var scene = new SceneBuilder();
                                            composer.Compose(scene);
                                            var gltf = scene.ToGltf2();
                                            if (currentExportType.HasFlag(ExportType.GLB))
                                            {
                                                gltf.SaveGLB(Path.Combine(path, $"{defaultName}.glb"));
                                            }

                                            if (currentExportType.HasFlag(ExportType.GLTF))
                                            {
                                                gltf.SaveGLTF(Path.Combine(path, $"{defaultName}.gltf"));
                                            }

                                            if (currentExportType.HasFlag(ExportType.OBJ))
                                            {
                                                gltf.SaveAsWavefront(Path.Combine(path, $"{defaultName}.obj"));
                                            }
                                            
                                            Process.Start("explorer.exe", path);
                                        }, cancelToken.Token);
                                    }, Plugin.TempDirectory);
    }

    public void Dispose()
    {
        cancelToken.Cancel();
    }
}
