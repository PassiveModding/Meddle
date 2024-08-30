using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Services;
using Microsoft.Extensions.Logging;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace Meddle.Plugin.UI;

public class TerrainTab : ITab
{
    private readonly LayoutService layoutService;
    private readonly ILogger<TerrainTab> logger;
    private readonly SigUtil sigUtil;
    private readonly IDataManager dataManager;

    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name => "Terrain";
    public int Order => 0;
    public MenuType MenuType => MenuType.Debug;

    public TerrainTab(
        LayoutService layoutService,
        ILogger<TerrainTab> logger,
        SigUtil sigUtil,
        IDataManager dataManager)
    {
        this.layoutService = layoutService;
        this.logger = logger;
        this.sigUtil = sigUtil;
        this.dataManager = dataManager;
        cts = new CancellationTokenSource();
    }


    private CancellationTokenSource cts;
    private Task task = Task.CompletedTask;
    private ExportType exportType = ExportType.GLTF;
    public enum ExportType
    {
        GLTF,
        OBJ,
        GLB
    }
    
    public void DrawInner()
    {
        ImGui.Text("Note: textures exported from here are not manipulated in any way at this stage.");
        ImGui.Text("To combine with world objects, use the layout tab and export with origin mode set to 'Zero'.");
        
        if (ImGui.BeginCombo("Export Type", exportType.ToString()))
        {
            foreach (ExportType type in Enum.GetValues(typeof(ExportType)))
            {
                var isSelected = type == exportType;
                if (ImGui.Selectable(type.ToString(), isSelected))
                {
                    exportType = type;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
        
        if (task.IsFaulted)
        {
            var ex = task.Exception;
            ImGui.TextWrapped($"Error: {ex}");
        }

        if (task.IsCompleted)
        {
            /*using (var disabled = ImRaii.Disabled())
            {
                if (ImGui.Button("Export Full Layout"))
                {
                    var dirname = $"Export-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                    fileDialog.SaveFolderDialog("Select folder to dump to", dirname,
                                                (result, path) =>
                                                {
                                                    cts = new CancellationTokenSource();
                                                    if (!result) return;
                                                    task = ComposeAll(path, exportType, cts.Token);
                                                }, Plugin.TempDirectory);
                }
            }*/
            if (ImGui.Button($"Export Terrain"))
            {
                var dirname = $"Export-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                fileDialog.SaveFolderDialog("Select folder to dump to", dirname,
                    (result, path) =>
                    {
                        cts = new CancellationTokenSource();
                        if (!result) return;
                        task = ComposeTerrain(path, exportType, cts.Token);
                    }, Plugin.TempDirectory);
            }
        }
        else
        {
            if (LastProgress != null)
            {
                var (progress, total) = LastProgress;
                ImGui.ProgressBar(progress / (float)total, new Vector2(-1, 0), $"Progress: {progress}/{total}");
            }
            
            if (ImGui.Button("Cancel"))
            {
                cts.Cancel();
            }
        }
    }

    public void Draw()
    {
        DrawInner();
        fileDialog.Draw();
    }
    
    private ProgressEvent? LastProgress { get; set; }

    public unsafe Task ComposeTerrain(string outDir, ExportType exportFormat, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Parsing terrain");
        var layoutWorld = sigUtil.GetLayoutWorld();
        if (layoutWorld == null) return Task.CompletedTask;
        var activeLayout = layoutWorld->ActiveLayout;
        if (activeLayout == null) return Task.CompletedTask;

        var scene = new SceneBuilder();
        Directory.CreateDirectory(outDir);
        var cacheDir = Path.Combine(outDir, "cache");
        Directory.CreateDirectory(cacheDir);

        var teraFiles = new HashSet<string>();
        foreach (var (_, terrainPtr) in activeLayout->Terrains)
        {
            if (terrainPtr == null || terrainPtr.Value == null) continue;
            var terrain = terrainPtr.Value;
            var terrainDir = terrain->PathString;
            teraFiles.Add(terrainDir);
        }

        return Task.Run(() =>
        {
            foreach (var dir in teraFiles)
            {
                var terrainSet = new TerrainSet(logger, dataManager, dir, cacheDir, x => LastProgress = x, cancellationToken);
                terrainSet.Compose(scene);
            }

            var sceneGraph = scene.ToGltf2();
            switch (exportFormat)
            {
                case ExportType.GLTF:
                    sceneGraph.SaveGLTF(Path.Combine(outDir, "composed.gltf"));
                    break;
                case ExportType.OBJ:
                    sceneGraph.SaveAsWavefront(Path.Combine(outDir, "composed.obj"));
                    break;
                case ExportType.GLB:
                    sceneGraph.SaveGLB(Path.Combine(outDir, "composed.glb"));
                    break;
            }
            
            Process.Start("explorer.exe", outDir);
            GC.Collect();
        }, cancellationToken);
    }
    
    public unsafe Task ComposeAll(string outDir, ExportType exportFormat, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Parsing terrain");
        var layoutWorld = sigUtil.GetLayoutWorld();
        if (layoutWorld == null) return Task.CompletedTask;
        var activeLayout = layoutWorld->ActiveLayout;
        if (activeLayout == null) return Task.CompletedTask;

        var scene = new SceneBuilder();
        Directory.CreateDirectory(outDir);
        var cacheDir = Path.Combine(outDir, "cache");
        Directory.CreateDirectory(cacheDir);

        var teraFiles = new HashSet<string>();
        foreach (var (_, terrainPtr) in activeLayout->Terrains)
        {
            if (terrainPtr == null || terrainPtr.Value == null) continue;
            var terrain = terrainPtr.Value;
            var terrainDir = terrain->PathString;
            teraFiles.Add(terrainDir);
        }

        var layout = layoutService.GetWorldState();
        
        return Task.Run(() =>
        {
            foreach (var dir in teraFiles)
            {
                var terrainSet = new TerrainSet(logger, dataManager, dir, cacheDir, x => LastProgress = x, cancellationToken);
                terrainSet.Compose(scene);
            }

            if (layout != null)
            {
                var instanceSet = new InstanceSet(logger, dataManager, layout, cacheDir, x => LastProgress = x, cancellationToken);
                instanceSet.Compose(scene);
            }

            var sceneGraph = scene.ToGltf2();
            switch (exportFormat)
            {
                case ExportType.GLTF:
                    sceneGraph.SaveGLTF(Path.Combine(outDir, "composed.gltf"));
                    break;
                case ExportType.OBJ:
                    sceneGraph.SaveAsWavefront(Path.Combine(outDir, "composed.obj"));
                    break;
                case ExportType.GLB:
                    sceneGraph.SaveGLB(Path.Combine(outDir, "composed.glb"));
                    break;
            }
            
            Process.Start("explorer.exe", outDir);
            GC.Collect();
        }, cancellationToken);
    }
}
