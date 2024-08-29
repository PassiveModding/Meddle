/*
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public class WorldTab : ITab
{
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
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
        TextureCache textureCache,
        ITextureProvider textureProvider,
        WorldService worldService)
    {
        this.clientState = clientState;
        this.log = log;
        this.exportService = exportService;
        this.parseService = parseService;
        this.pack = pack;
        this.config = config;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
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

        if (ImGui.CollapsingHeader("Options"))
        {
            if (ImGui.DragFloat("Cutoff Distance", ref worldService.CutoffDistance, 1, 0, 10000))
            {
                worldService.SaveOptions();
            }

            if (ImGui.ColorEdit4("Dot Color", ref worldService.DotColor, ImGuiColorEditFlags.NoInputs))
            {
                worldService.SaveOptions();
            }
            
            if (ImGui.BeginCombo("Overlay Type##OverlayType", worldService.Overlay.ToString()))
            {
                foreach (var overlayType in Enum.GetValues<WorldService.OverlayType>())
                {
                    var isSelected = worldService.Overlay == overlayType;
                    if (ImGui.Selectable(overlayType.ToString(), isSelected))
                    {
                        worldService.Overlay = overlayType;
                    }
                }
                ImGui.EndCombo();
            }
            
            ImGui.Checkbox("Resolve using GameGui", ref worldService.ResolveUsingGameGui);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("If disabled, will resolve using your current camera, " +
                                 "you should only need this if trying to resolve environments while not logged in.");
            }
            ImGui.Separator();
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
            if (obj.MdlFileGroup == null)
            {
                if (ImGui.Button("Parse Model"))
                {
                    Task.Run(async () =>
                    {
                        var mdlFileGroup = await parseService.ParseFromPath(obj.Path);
                        if (worldService.SelectedObjects.TryGetValue(ptr, out var snapshot) && snapshot is WorldService.BgObjectSnapshot bgObj)
                        {
                            worldService.SelectedObjects[ptr] = bgObj with {MdlFileGroup = mdlFileGroup};
                        }
                    });
                }
            }
            else
            {
                var mdl = obj.MdlFileGroup;
                ImGui.Text($"Lods: {mdl.MdlFile.Lods.Length}");
                ImGui.Text($"Attribute Count: {mdl.MdlFile.ModelHeader.AttributeCount}");
                ImGui.Text($"Bone Count: {mdl.MdlFile.ModelHeader.BoneCount}");
                ImGui.Text($"Bone Table Count: {mdl.MdlFile.ModelHeader.BoneTableCount}");
                ImGui.Text($"Mesh Count: {mdl.MdlFile.ModelHeader.MeshCount}");
                ImGui.Text($"Submesh Count: {mdl.MdlFile.ModelHeader.SubmeshCount}");
                ImGui.Text($"Material Count: {mdl.MdlFile.ModelHeader.MaterialCount}");
                ImGui.Text($"Radius: {mdl.MdlFile.ModelHeader.Radius}");
                ImGui.Text($"Shapemesh Count: {mdl.MdlFile.ModelHeader.ShapeMeshCount}");
                ImGui.Text($"Shape Count: {mdl.MdlFile.ModelHeader.ShapeCount}");
                ImGui.Text($"Vertex declarations: {mdl.MdlFile.VertexDeclarations.Length}");
                
                for (int i = 0; i < mdl.MtrlFiles.Length; i++)
                {
                    using var mtrlId = ImRaii.PushId($"Mtrl{i}");
                    var mtrl = mdl.MtrlFiles[i];
                    if (mtrl is MtrlFileGroup mtrlGroup)
                    {
                        if (ImGui.CollapsingHeader($"Material {i}: {mtrlGroup.Path}"))
                        {
                            using var mtrlIndent = ImRaii.PushIndent();
                            ImGui.Text($"Mdl Path: {mtrlGroup.MdlPath}");
                            ImGui.Text($"Shader Path: {mtrlGroup.ShpkPath}");
                            if (ImGui.CollapsingHeader("Color Table"))
                            {
                                UiUtil.DrawColorTable(mtrlGroup.MtrlFile.ColorTable, mtrlGroup.MtrlFile.ColorDyeTable);
                            }

                            for (int j = 0; j < mtrlGroup.TexFiles.Length; j++)
                            {
                                using var texId = ImRaii.PushId($"Tex{j}");
                                var tex = mtrlGroup.TexFiles[j];
                                if (ImGui.CollapsingHeader($"Texture {j}: {tex.Path}"))
                                {
                                    using var texIndent = ImRaii.PushIndent();
                                    ImGui.Text($"Mtrl Path: {tex.MtrlPath}");
                                    ImGui.Text($"Width: {tex.Resource.Width}");
                                    ImGui.Text($"Height: {tex.Resource.Height}");
                                    ImGui.Text($"Format: {tex.Resource.Format}");
                                    ImGui.Text($"Mipmap Count: {tex.Resource.MipLevels}");
                                    ImGui.Text($"Array Count: {tex.Resource.ArraySize}");
                                    
                                    var availableWidth = ImGui.GetContentRegionAvail().X;
                                    float displayWidth = tex.Resource.Width;
                                    float displayHeight = tex.Resource.Height;
                                    if (displayWidth > availableWidth)
                                    {
                                        var ratio = availableWidth / displayWidth;
                                        displayWidth *= ratio;
                                        displayHeight *= ratio;
                                    }
                                    
                                    var wrap = textureCache.GetOrAdd($"{mtrlGroup.Path}_{tex.Path}", () =>
                                    {
                                        var textureData = tex.Resource.ToBitmap().GetPixelSpan();
                                        var wrap = textureProvider.CreateFromRaw(
                                            RawImageSpecification.Rgba32(tex.Resource.Width, tex.Resource.Height), textureData,
                                            $"Meddle_World_{mtrlGroup.Path}_{tex.Path}");
                                        return wrap;
                                    });
                                    
                                    ImGui.Image(wrap.ImGuiHandle, new Vector2(displayWidth, displayHeight));
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
*/


/*
    public async Task Export((Transform transform, string mdlPath)[] models, ModelExportProgress progress, string? outputFolder = null, CancellationToken token = default)
    {
        try
        {
            using var activity = ActivitySource.StartActivity();
            var scene = new SceneBuilder();
            
            var distinctPaths = models.Select(x => x.mdlPath).Distinct().ToArray();
            progress.DistinctPaths = distinctPaths.Length;
            var caches = new ConcurrentDictionary<string, (Model model, ModelBuilder.MeshExport mesh)[]>();
            
            var bones = new List<BoneNodeBuilder>();
            await Parallel.ForEachAsync(distinctPaths, new ParallelOptions {CancellationToken = token}, async (path, tkn) =>
            {
                if (token.IsCancellationRequested) return;
                if (tkn.IsCancellationRequested) return;
                try
                {
                    var mdlGroup = await parseService.ParseFromPath(path);
                    var cache = HandleModel(new CustomizeData(),
                                        new CustomizeParameter(),
                                        GenderRace.Unknown,
                                        mdlGroup,
                                        ref bones,
                                        null,
                                        true,
                                        token).ToArray();
                    caches[path] = cache;

                    progress.ModelsParsed++;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error handling model {Path}", path);
                }
            });
            
            foreach (var (transform, path) in models)
            {
                if (token.IsCancellationRequested) return;
                if (!caches.TryGetValue(path, out var cache))
                {
                    logger.LogWarning("Cache not found for {Path}", path);
                    continue;
                }
                foreach (var (model, mesh) in cache)
                {
                    AddMesh(scene, transform.AffineTransform.Matrix, model, mesh, []);
                }
            }
            
            var sceneGraph = scene.ToGltf2();
            if (outputFolder != null)
            {
                Directory.CreateDirectory(outputFolder);
            }

            var folder = outputFolder ?? GetPathForOutput();
            var outputPath = Path.Combine(folder, "scene.gltf");
            sceneGraph.SaveGLTF(outputPath);
            Process.Start("explorer.exe", folder);
            logger.LogInformation("Export complete");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to export model set");
            throw;
        }
    }
    */
