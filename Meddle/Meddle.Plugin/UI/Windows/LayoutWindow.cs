using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI.Windows;

public class LayoutWindow : Window, IDisposable
{
    public enum OriginMode
    {
        Player,
        Zero,
        Average
    }

    private readonly Configuration config;
    private readonly ExportService exportService;

    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };

    private readonly LayoutService layoutService;
    private readonly ILogger<LayoutWindow> log;
    private readonly LayoutOverlay overlay;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    private readonly ParseService parseService;
    private readonly List<ParsedInstance> selectedInstances = new();
    private Dictionary<string, MdlFileGroup> mdlGroups = new();

    private ParsedInstanceType selectedTypes = ParsedInstanceType.AllSupported;

    private readonly SigUtil sigUtil;
    private ExportService.ModelExportProgress? exportProgress;
    private Task exportTask = Task.CompletedTask;
    private string? lastError;
    private bool orderByDistance = true;
    private OriginMode originMode = OriginMode.Player;
    private bool traceToExpanded = true;
    private bool traceToHovered = true;

    public LayoutWindow(
        LayoutService layoutService, Configuration config,
        SigUtil sigUtil, ExportService exportService,
        ILogger<LayoutWindow> log, LayoutOverlay overlay,
        TextureCache textureCache, ITextureProvider textureProvider,
        ParseService parseService) : base("Layout")
    {
        this.layoutService = layoutService;
        this.config = config;
        this.sigUtil = sigUtil;
        this.exportService = exportService;
        this.log = log;
        this.overlay = overlay;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
        this.parseService = parseService;
        overlay.OnInstanceClick += AddToSelected;
    }

    public void Dispose()
    {
        overlay.OnInstanceClick -= AddToSelected;
    }

    private void AddToSelected(ParsedInstance instance)
    {
        var existing = selectedInstances.Find(x => x.Id == instance.Id);
        if (existing != null)
        {
            selectedInstances.Remove(existing);
        }

        selectedInstances.Add(instance);
    }

    public override void OnOpen()
    {
        overlay.IsOpen = true;
        base.OnOpen();
    }

    public override void OnClose()
    {
        overlay.IsOpen = false;
        base.OnClose();
    }

    public override void Draw()
    {
        try
        {
            InnerDraw();
        }
        catch (Exception e)
        {
            if (e.ToString() != lastError)
            {
                lastError = e.ToString();
                log.LogError(e, "Failed to draw Layout tab");
            }

            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Failed to draw Layout tab");
            ImGui.TextWrapped(e.ToString());
        }
    }

    private void InnerDraw()
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

            ImGui.Checkbox("Order by Distance", ref orderByDistance);
            ImGui.Checkbox("Trace to Hovered", ref traceToHovered);
            ImGui.Checkbox("Trace to Expanded", ref traceToExpanded);

            if (ImGui.BeginCombo("Origin Mode", originMode.ToString()))
            {
                foreach (var mode in Enum.GetValues<OriginMode>())
                {
                    if (ImGui.Selectable(mode.ToString(), mode == originMode))
                    {
                        originMode = mode;
                    }
                }

                ImGui.EndCombo();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("The origin point for exports\n" +
                                 "Player: Your current position\n" +
                                 "Zero: 0,0,0\n" +
                                 "Average: The average position of all selected instances");
            }

            // imgui selectable
            if (ImGui.CollapsingHeader("Selected Types"))
            {
                foreach (var type in Enum.GetValues<ParsedInstanceType>())
                {
                    var selected = selectedTypes.HasFlag(type);
                    if (ImGui.Checkbox(type.ToString(), ref selected))
                    {
                        if (selected)
                        {
                            selectedTypes |= type;
                        }
                        else
                        {
                            selectedTypes &= ~type;
                        }
                    }
                }
            }

            if (ImGui.CollapsingHeader("Overlay Types"))
            {
                foreach (var type in Enum.GetValues<ParsedInstanceType>())
                {
                    var selected = overlay.DrawTypes.HasFlag(type);
                    if (ImGui.Checkbox(type.ToString(), ref selected))
                    {
                        if (selected)
                        {
                            overlay.DrawTypes |= type;
                        }
                        else
                        {
                            overlay.DrawTypes &= ~type;
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

        if (ImGui.CollapsingHeader("Selected Instances"))
        {
            DrawSelectedInstances();
        }

        if (ImGui.CollapsingHeader("Current Layout"))
        {
            DrawLayout();
        }
    }

    private Vector3 CalculateOrigin(ParsedInstance[] instances)
    {
        switch (originMode)
        {
            case OriginMode.Player:
                return sigUtil.GetLocalPosition();
            case OriginMode.Zero:
                return Vector3.Zero;
            case OriginMode.Average:
                return instances.Aggregate(Vector3.Zero, (acc, x) => acc + x.Transform.Translation) / instances.Length;
            default:
                throw new ArgumentOutOfRangeException();
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
                                                var selected = selectedInstances.ToArray();
                                                var origin = CalculateOrigin(selected);
                                                await exportService.Export(selected, origin, exportProgress, path);
                                            });
                                        }, Plugin.TempDirectory);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Selected"))
        {
            selectedInstances.Clear();
        }

        IEnumerable<ParsedInstance> allInstances = selectedInstances;
        if (orderByDistance)
        {
            allInstances =
                allInstances.OrderBy(x => Vector3.Distance(x.Transform.Translation, sigUtil.GetLocalPosition()));
        }

        DrawInstanceTable(allInstances.ToArray(), ctx =>
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
        using var table =
            ImRaii.Table("##layoutTable", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Reorderable);
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
                           .Where(x => selectedTypes.HasFlag(x.Type));

        if (orderByDistance)
        {
            allInstances = allInstances.OrderBy(x => Vector3.Distance(x.Transform.Translation, local));
        }

        var all = allInstances.ToArray();
        if (ImGui.Button($"Export All ({all.Length})"))
        {
            var defaultName = $"LayoutExport-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
            exportProgress = new ExportService.ModelExportProgress();
            fileDialog.SaveFolderDialog("Save Model", defaultName,
                                        (result, path) =>
                                        {
                                            if (!result) return;
                                            exportTask = Task.Run(async () =>
                                            {
                                                var origin = CalculateOrigin(all);
                                                await exportService.Export(all, origin, exportProgress, path);
                                            });
                                        }, Plugin.TempDirectory);
        }

        DrawInstanceTable(all, ctx =>
        {
            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString()))
                {
                    AddToSelected(ctx);
                }
            }
        });
    }

    private void DrawInstance(ParsedInstance instance, Action<ParsedInstance>? additionalOptions = null, int depth = 0)
    {
        if (!selectedTypes.HasFlag(instance.Type))
            return;
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
            ParsedUnsupportedInstance unsupported => $"{unsupported.Type} - {unsupported.InstanceType}",
            ParsedCharacterInstance character => $"{character.Type} - {character.Kind} - {character.Name}",
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
        var displayInner = ImGui.CollapsingHeader($"[{distance:F1}y] {infoHeader}###{instance.Id}");
        if (ImGui.IsItemHovered() && traceToHovered)
        {
            overlay.EnqueueLayoutTabHoveredInstance(instance);
        }
        else if (displayInner && traceToExpanded)
        {
            overlay.EnqueueLayoutTabHoveredInstance(instance);
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

            UiUtil.Text($"Id: {instance.Id}", $"{instance.Id:X8}");
            ImGui.Text($"Type: {instance.Type}");
            ImGui.Text($"Position: {instance.Transform.Translation}");
            ImGui.Text($"Rotation: {instance.Transform.Rotation}");
            ImGui.Text($"Scale: {instance.Transform.Scale}");
            if (!string.IsNullOrEmpty(instance.Path))
            {
                UiUtil.Text($"Path: {instance.Path}", instance.Path);
            }
            
            if (instance is ParsedBgPartsInstance {Path: not null} bg)
            {
                if (!mdlGroups.TryGetValue(bg.Path, out var group))
                {
                    if (ImGui.Button("Parse"))
                    {
                        Task.Run(async () =>
                        {
                            var mdlGroup = await parseService.ParseFromPath(bg.Path);
                            mdlGroups[bg.Path] = mdlGroup;
                        });
                    }
                }
                else
                {
                    DrawModelGroup(group);
                }
            }
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
        var supportedExportTypes = new[]
        {
            ParsedInstanceType.BgPart,
            ParsedInstanceType.SharedGroup,
            ParsedInstanceType.Light
        };
        
        using (ImRaii.Disabled(!instances.All(x => supportedExportTypes.Contains(x.Type))))
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
                                                    var origin = CalculateOrigin(instances);
                                                    await exportService.Export(instances, origin, exportProgress, path);
                                                });
                                            }, Plugin.TempDirectory);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Export as glTF");
        }
    }

    private void DrawModelGroup(MdlFileGroup mdl)
    {
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
                    DrawMaterialGroup(mtrlGroup);
                }
            }
        }
    }

    private void DrawMaterialGroup(MtrlFileGroup mtrlGroup)
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
