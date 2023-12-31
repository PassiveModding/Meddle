﻿using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Xande;
using Meddle.Xande.Enums;
using Meddle.Xande.Models;
using Penumbra.Api.Enums;

namespace Meddle.Plugin.UI.Shared;

public class ResourceTreeRenderer : IDisposable
{
    private readonly ModelConverter _modelConverter;
    private Task? ExportTask { get; set; }
    private bool CopyNormalAlphaToDiffuse { get; set; }= true;
    private ExportType ExportTypeFlags { get; set; }= ExportType.Gltf;
    private CancellationTokenSource ExportCts { get; set; } = new();

    public ResourceTreeRenderer(ModelConverter modelConverter)
    {
        _modelConverter = modelConverter;
    }
    
    public void DrawResourceTree(ResourceTree resourceTree, ref bool[] exportOptions)
    {
        // disable buttons if exporting
        var disableExport = ExportTask != null;
        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, disableExport))
        {
            // export button
            if (ImGui.Button($"Export {exportOptions.Count(x => x)} selected") && ExportTask == null)
            {
                ExportTask = _modelConverter.ExportResourceTree(resourceTree, exportOptions,
                    true,
                    ExportTypeFlags, 
                    Plugin.TempDirectory, 
                    CopyNormalAlphaToDiffuse,
                    ExportCts.Token);
            }

            ImGui.SameLine();
            // export all button
            if (ImGui.Button("Export All") && ExportTask == null)
            {
                ExportTask = _modelConverter.ExportResourceTree(resourceTree,
                    new bool[resourceTree.Nodes.Length].Select(_ => true).ToArray(),
                    true,
                    ExportTypeFlags,
                    Plugin.TempDirectory,
                    CopyNormalAlphaToDiffuse,
                    ExportCts.Token);
            }

            // exportType option, checkboxes for types
            var exportTypeFlags = (int) ExportTypeFlags;
            ImGui.SameLine();
            ImGui.CheckboxFlags("Gltf", ref exportTypeFlags, (int) ExportType.Gltf);
            ImGui.SameLine();
            ImGui.CheckboxFlags("Glb", ref exportTypeFlags, (int) ExportType.Glb);
            ImGui.SameLine();
            ImGui.CheckboxFlags("Wavefront", ref exportTypeFlags, (int) ExportType.Wavefront);
            if (exportTypeFlags != (int) ExportTypeFlags)
            {
                ExportTypeFlags = (ExportType) exportTypeFlags;
            }
            
            var copyNormalAlphaToDiffuseX = CopyNormalAlphaToDiffuse;
            if (ImGui.Checkbox("Copy Normal Alpha to Diffuse", ref copyNormalAlphaToDiffuseX) && copyNormalAlphaToDiffuseX != CopyNormalAlphaToDiffuse)
            {
                CopyNormalAlphaToDiffuse = copyNormalAlphaToDiffuseX;
            }
        }

        // cancel button
        if (ExportTask != null)
        {
            if (ExportTask.IsCompleted)
            {
                ExportTask = null!;
            }
            else if (ExportTask.IsCanceled)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted("Export Cancelled...");
            }
            else
            {
                ImGui.SameLine();
                if (ImGui.Button("Cancel Export"))
                {
                    ExportCts.Cancel();
                    ExportCts.Dispose();
                    ExportCts = new CancellationTokenSource();
                }
            }
        }
        
        ImGui.SameLine();
        ImGui.Text(_modelConverter.GetLastMessage()?.Split("\n").FirstOrDefault() ?? string.Empty);

        if (resourceTree?.Nodes == null)
        {
            return;
        }

        using var table = ImRaii.Table("##ResourceTable", 3,
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
        if (!table)
            return;

        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 250);
        ImGui.TableSetupColumn("GamePath", ImGuiTableColumnFlags.WidthStretch, 0.3f);
        ImGui.TableSetupColumn("FullPath", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableHeadersRow();

        for (int i = 0; i < resourceTree.Nodes.Length; i++)
        {
            var node = resourceTree.Nodes[i];
            var exportOption = exportOptions[i];

            // only interested in mdl, sklb and tex
            var type = node.Type;
            if (type != ResourceType.Mdl
                && type != ResourceType.Sklb
                && type != ResourceType.Tex)
                continue;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (node?.Children == null) continue;
            if (node.Children.Length > 0)
            {
                if (type == ResourceType.Mdl)
                {
                    ImGui.Checkbox($"##{node.GetHashCode()}", ref exportOption);
                    exportOptions[i] = exportOption;
                    // hover to show tooltip
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Export \"{node.Name}\"");
                    }

                    // quick export button
                    ImGui.SameLine();
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        // if export task is running, disable button
                        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, disableExport))
                        {
                            if (ImGui.Button($"{FontAwesomeIcon.FileExport.ToIconString()}##{node.GetHashCode()}") && ExportTask == null)
                            {
                                var tmpExportOptions = new bool[resourceTree.Nodes.Length];
                                tmpExportOptions[i] = true;
                                ExportTask = _modelConverter.ExportResourceTree(resourceTree, tmpExportOptions,
                                    true,
                                    ExportTypeFlags,
                                    Plugin.TempDirectory,
                                    CopyNormalAlphaToDiffuse,
                                    ExportCts.Token);
                            }
                        }
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Export \"{node.Name}\" as individual model");
                    }

                    ImGui.SameLine();
                }

                using var section = ImRaii.TreeNode($"{node.Name}##{node.GetHashCode()}",
                    ImGuiTreeNodeFlags.SpanAvailWidth);

                // only render current row
                ImGui.TableNextColumn();
                DrawCopyableText(node.GamePath);
                ImGui.TableNextColumn();
                DrawCopyableText(node.FullPath);

                if (!section) continue;
                foreach (var childNode in node.Children)
                {
                    DrawResourceNode(childNode);
                }
                // add line to separate
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                // vertical spacing to help separate next node
                ImGui.Dummy(new Vector2(0, 10));
            }
            else
            {
                using var section = ImRaii.TreeNode($"{node.Name}##{node.GetHashCode()}",
                    ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.Leaf);
                ImGui.TableNextColumn();
                DrawCopyableText(node.GamePath);
                ImGui.TableNextColumn();
                DrawCopyableText(node.FullPath);
            }
        }
    }

    public static void DrawCopyableText(string text)
    {
        ImGui.Text(text);
        // click to copy
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ImGui.SetClipboardText(text);
        }

        // hover to show tooltip
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Copy \"{text}\" to clipboard");
        }
    }

    public static void DrawResourceNode(Node node)
    {
        // add same data to the table, expandable if more children, increase indent in first column
        // indent
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (node.Children.Length > 0)
        {
            ImGui.Dummy(new Vector2(5, 0));
            ImGui.SameLine();

            // default open all children
            ImGui.SetNextItemOpen(true, ImGuiCond.Once);
            using var section = ImRaii.TreeNode($"{node.Name}##{node.GetHashCode()}",
                ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.Bullet);
            ImGui.TableNextColumn();
            DrawCopyableText(node.GamePath);
            ImGui.TableNextColumn();
            DrawCopyableText(node.FullPath);

            if (section)
            {
                foreach (var child in node.Children)
                {
                    DrawResourceNode(child);
                }
            }
        }
        else
        {
            using var section = ImRaii.TreeNode($"{node.Name}##{node.GetHashCode()}",
                ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.Leaf);
            ImGui.TableNextColumn();
            DrawCopyableText(node.GamePath);
            ImGui.TableNextColumn();
            DrawCopyableText(node.FullPath);
        }
    }

    public void Dispose()
    {
        ExportTask?.Dispose();
        ExportCts.Dispose();
    }
}