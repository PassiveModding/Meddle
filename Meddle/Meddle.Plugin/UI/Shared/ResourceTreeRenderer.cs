using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using Meddle.Xande;
using Meddle.Xande.Utility;
using Penumbra.Api;
using Penumbra.Api.Enums;
using System.Numerics;
using Xande.Enums;

namespace Meddle.Plugin.UI.Shared;

public class ResourceTreeRenderer : IDisposable
{
    private ModelConverter ModelConverter { get; }

    private Task? ExportTask { get; set; }
    private bool CopyNormalAlphaToDiffuse { get; set; } = true;
    private ExportType ExportTypeFlags { get; set; } = ExportType.Gltf;
    private CancellationTokenSource ExportCts { get; set; } = new();

    public ResourceTreeRenderer(ModelConverter modelConverter)
    {
        ModelConverter = modelConverter;
    }

    public void DrawResourceTree(Ipc.ResourceTree resourceTree)
    {
        // disable buttons if exporting
        var disableExport = ExportTask != null;
        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, disableExport))
        {
            if (ImGui.Button("Export") && ExportTask == null)
            {
                ExportTask = ModelConverter.ExportResourceTree(resourceTree,
                    true,
                    ExportTypeFlags,
                    Plugin.TempDirectory,
                    CopyNormalAlphaToDiffuse,
                    null,
                    null,
                    null,
                    ExportCts.Token);
            }

            // exportType option, checkboxes for types
            var exportTypeFlags = (int)ExportTypeFlags;
            ImGui.SameLine();
            ImGui.CheckboxFlags("Gltf", ref exportTypeFlags, (int)ExportType.Gltf);
            ImGui.SameLine();
            ImGui.CheckboxFlags("Glb", ref exportTypeFlags, (int)ExportType.Glb);
            ImGui.SameLine();
            ImGui.CheckboxFlags("Wavefront", ref exportTypeFlags, (int)ExportType.Wavefront);
            if (exportTypeFlags != (int)ExportTypeFlags)
            {
                ExportTypeFlags = (ExportType)exportTypeFlags;
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
        ImGui.Text(ModelConverter.GetLastMessage()?.Split("\n").FirstOrDefault() ?? string.Empty);

        using var table = ImRaii.Table("##ResourceTable", 3,
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
        if (!table)
            return;

        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 250);
        ImGui.TableSetupColumn("GamePath", ImGuiTableColumnFlags.WidthStretch, 0.3f);
        ImGui.TableSetupColumn("FullPath", ImGuiTableColumnFlags.WidthStretch, 0.5f);
        ImGui.TableHeadersRow();

        for (int i = 0; i < resourceTree.Nodes.Count; i++)
        {
            var node = resourceTree.Nodes[i];

            // only interested in mdl, sklb and tex
            var type = node.Type;
            if (type != ResourceType.Mdl
                && type != ResourceType.Sklb
                && type != ResourceType.Tex)
                continue;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (node?.Children == null) continue;
            if (node.Children.Any())
            {
                using var section = ImRaii.TreeNode($"{node.Name}##{node.GetHashCode()}",
                    ImGuiTreeNodeFlags.SpanAvailWidth);

                // only render current row
                ImGui.TableNextColumn();
                DrawCopyableText(node.GamePath ?? "Unknown");
                ImGui.TableNextColumn();
                DrawCopyableText(node.FullPath());

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
                DrawCopyableText(node.GamePath ?? "Unknown");
                ImGui.TableNextColumn();
                DrawCopyableText(node.FullPath());
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

    public static void DrawResourceNode(Ipc.ResourceNode node)
    {
        // add same data to the table, expandable if more children, increase indent in first column
        // indent
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (node.Children.Any())
        {
            ImGui.Dummy(new Vector2(5, 0));
            ImGui.SameLine();

            // default open all children
            ImGui.SetNextItemOpen(true, ImGuiCond.Once);
            using var section = ImRaii.TreeNode($"{node.Name}##{node.GetHashCode()}",
                ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.Bullet);
            ImGui.TableNextColumn();
            DrawCopyableText(node.GamePath ?? "Unknown");
            ImGui.TableNextColumn();
            DrawCopyableText(node.FullPath());

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
            DrawCopyableText(node.GamePath ?? "Unknown");
            ImGui.TableNextColumn();
            DrawCopyableText(node.FullPath());
        }
    }

    public void Dispose()
    {
        ExportTask?.Dispose();
        ExportCts.Dispose();
    }
}
