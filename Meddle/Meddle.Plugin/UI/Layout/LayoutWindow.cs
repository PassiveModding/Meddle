using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow : Window, IDisposable
{
    private readonly LayoutService layoutService;
    private readonly Configuration config;
    private readonly SigUtil sigUtil;
    private readonly ILogger<LayoutWindow> log;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    private readonly SqPack dataManager;

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
        SqPack dataManager) : base("Layout")
    {
        this.layoutService = layoutService;
        this.config = config;
        this.sigUtil = sigUtil;
        this.log = log;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
        this.dataManager = dataManager;
    }

    private void SetupCurrentState()
    {
        currentLayout = layoutService.GetWorldState() ?? [];
        currentPos = sigUtil.GetLocalPosition();
    }

    private bool shouldUpdateState = true;
    
    public override void Draw()
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
                SetupCurrentState();
                shouldUpdateState = false;
            }
            
            if (drawOverlay)
            {
                shouldUpdateState = true;
                DrawOverlayWindow(out var hovered, out var selected);
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
                using var id = ImRaii.PushId("layoutTable");
                var set = currentLayout
                          .Where(x =>
                          {
                              // keep terrain regardless of distance
                              if (x is ParsedTerrainInstance) return true;
                              return Vector3.Distance(x.Transform.Translation, currentPos) < config.WorldCutoffDistance;
                          })
                          .Where(x => drawTypes.HasFlag(x.Type));
                if (orderByDistance)
                {
                    set = set.OrderBy(x => Vector3.Distance(x.Transform.Translation, sigUtil.GetLocalPosition()));
                }

                if (hideOffscreenCharacters)
                {
                    set = set.Where(x => x is not ParsedCharacterInstance {Visible: false});
                }

                var items = set.ToArray();

                ExportButton($"Export {items.Length} instance(s)", items);
                ImGui.SameLine();
                if (ImGui.Button($"Add {items.Length} instance(s) to selection"))
                {
                    foreach (var item in items)
                    {
                        selectedInstances[item.Id] = item;
                    }
                }

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
                    resolvableInstance.Resolve(layoutService);
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

        layoutService.ResolveInstances(instances);

        var defaultName = $"InstanceExport-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
        var currentExportType = exportType;
        cancelToken = new CancellationTokenSource();
        fileDialog.SaveFolderDialog("Save Instances", defaultName,
                                    (result, path) =>
                                    {
                                        if (!result) return;
                                        exportTask = Task.Run(() =>
                                        {
                                            Directory.CreateDirectory(path);
                                            var cacheDir = Path.Combine(path, "cache");
                                            Directory.CreateDirectory(cacheDir);
                                            var instanceSet =
                                                new InstanceComposer(log, dataManager, config, instances, cacheDir,
                                                                x => progress = x, bakeTextures, cancelToken.Token);
                                            var scene = new SceneBuilder();
                                            instanceSet.Compose(scene);
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
