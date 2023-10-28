using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Lumina;
using Meddle.Xande;
using Meddle.Xande.Enums;
using Meddle.Xande.Models;
using Meddle.Xande.Utility;
using Newtonsoft.Json;
using Penumbra.Api;
using Penumbra.Api.Enums;
using Xande;
using Xande.Havok;
using Task = System.Threading.Tasks.Task;

namespace Meddle.Plugin.UI;

public class ResourceTab : ITab
{
    public string Name => "Resources";
    public int Order => 1;
    private readonly FileDialogManager _fileDialogManager;
    private Task<(string hash, ResourceTree tree, DateTime refreshedAt, bool[] exportOptions)>? _resourceTask;
    private (string hash, ResourceTree tree, DateTime refreshedAt, bool[] exportOptions)? _resourceTaskResult;
    private CancellationTokenSource _exportCts;
    private Task? _exportTask;
    private (string name, int index) _selectedGameObject;
    private string _searchFilter = string.Empty;
    private readonly ModelConverter _modelConverter;
    private ExportType _selectedExportTypeFlags = ExportType.Gltf;
    private readonly LuminaManager _luminaManager;
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;
    private readonly IObjectTable _objectTable;


    public ResourceTab(DalamudPluginInterface pluginInterface, ModelConverter modelConverter, LuminaManager luminaManager, IPluginLog log, IObjectTable objectTable)
    {
        _pluginInterface = pluginInterface;
        _log = log;
        _objectTable = objectTable;
        _modelConverter = modelConverter;
        _luminaManager = luminaManager;
        _fileDialogManager = new FileDialogManager();
        _exportCts = new CancellationTokenSource();
        _selectedGameObject = ("None", -1);
    }

    public void Draw()
    {
        DrawObjectPicker();
        if (_resourceTask == null)
        {
            ImGui.Text("No resources found");
            return;
        }

        if (_resourceTaskResult != null)
        {
            // check if hash is different
            if (_resourceTask != null && _resourceTask.IsCompletedSuccessfully &&
                _resourceTask.Result.hash != _resourceTaskResult.Value.hash)
            {
                _resourceTaskResult = _resourceTask.Result;
            }
        }
        else if (_resourceTask != null)
        {
            if (!_resourceTask.IsCompleted)
            {
                ImGui.Text("Loading...");
            }
            else if (_resourceTask.Exception != null)
            {
                ImGui.Text($"Error loading resources\n\n{_resourceTask.Exception}");
            }
            else if (_resourceTask.IsCompletedSuccessfully)
            {
                _resourceTaskResult = _resourceTask.Result;
            }
        }

        // dropdown to select
        if (!_resourceTaskResult.HasValue)
        {
            ImGui.Text("No resources found");
            return;
        }

        using var child = ImRaii.Child("##Data");
        if (!child)
            return;

        var resourceTaskResult = _resourceTaskResult.Value;

        DrawResourceTree(resourceTaskResult.tree, ref resourceTaskResult.exportOptions);
    }

    private void DrawObjectPicker()
    {
        _fileDialogManager.Draw();
        // show load from disk button
        if (ImGui.Button("Load from disk"))
        {
            _fileDialogManager.OpenFileDialog("Select Resource File", "Json Files{.json}", (selected, paths) =>
            {
                if (!selected) return;
                if (paths.Count == 0)
                {
                    return;
                }

                var path = paths[0];
                _resourceTask = LoadResourceListFromDisk(path);
            }, 1, startPath: Plugin.TempDirectory, isModal: true);
        }

        ImGui.SameLine();
        if (ImGui.Button("Open Temp Directory"))
        {
            Process.Start("explorer.exe", Plugin.TempDirectory);
        }


        var objects = _objectTable.Where(x => x.IsValid())
            .Where(x =>
                x.ObjectKind is ObjectKind.Player or ObjectKind.BattleNpc or ObjectKind.Retainer or ObjectKind.EventNpc
                    or ObjectKind.Companion
            )
            .Where(x => CharacterUtility.HasDrawObject(x.ObjectIndex, _objectTable))
            .ToArray();
        if (objects.Length == 0)
        {
            ImGui.Text("No game objects found");
        }
        else
        {
            // combo for selecting game object
            ImGui.Text("Select Game Object");
            var selected = _selectedGameObject.index;
            var filter = _searchFilter;
            if (ImGui.InputText("##Filter", ref filter, 100))
            {
                _searchFilter = filter;
            }

            if (!string.IsNullOrEmpty(filter))
            {
                objects = objects.Where(x => x.Name.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                // edit index if it's out of range
                if (selected >= objects.Length)
                {
                    selected = objects.Length - 1;
                }
            }

            var names = objects.Select(x =>
            {
                var name = x.Name?.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    name = x.ObjectIndex.ToString();
                }
                return $"{name} - {x.ObjectKind}";
            }).ToArray();

            if (selected == -1 && names.Length > 0)
            {
                // set to first object
                _selectedGameObject.index = 0;
                _selectedGameObject.name = names[0];
            }

            if (ImGui.Combo("##GameObject", ref selected, names, names.Length))
            {
                _selectedGameObject.index = selected;
                _selectedGameObject.name = names[selected];
                var selectedObject = objects[selected];
                _resourceTask = TryBuildResourceList(selectedObject.Name.ToString(), selectedObject.ObjectIndex);
            }
            
            ImGui.SameLine();
            // left/right arrow buttons
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button($"{FontAwesomeIcon.ArrowLeft.ToIconString()}##Left") && _selectedGameObject.index > 0)
                {
                    _selectedGameObject.index--;
                    _selectedGameObject.name = names[_selectedGameObject.index];
                    var selectedObject = objects[_selectedGameObject.index];
                    _resourceTask = TryBuildResourceList(selectedObject.Name.ToString(), selectedObject.ObjectIndex);
                }

                ImGui.SameLine();
                if (ImGui.Button($"{FontAwesomeIcon.ArrowRight.ToIconString()}##Right") &&
                    _selectedGameObject.index < objects.Length - 1)
                {
                    _selectedGameObject.index++;
                    _selectedGameObject.name = names[_selectedGameObject.index];
                    var selectedObject = objects[_selectedGameObject.index];
                    _resourceTask = TryBuildResourceList(selectedObject.Name.ToString(), selectedObject.ObjectIndex);
                }
            }

            if (ImGui.Button("Refresh") && _selectedGameObject.index != -1 &&
                (_resourceTask == null || _resourceTask.IsCompleted))
            {
                var selectedObject = objects[_selectedGameObject.index];
                _resourceTask = TryBuildResourceList(selectedObject.Name.ToString(), selectedObject.ObjectIndex);
            }
        }
    }

    private void DrawResourceTree(ResourceTree resourceTree, ref bool[] exportOptions)
    {
        // disable buttons if exporting
        var disableExport = _exportTask != null;
        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, disableExport))
        {

            // export button
            if (ImGui.Button($"Export {exportOptions.Count(x => x)} selected") && _exportTask == null)
            {
                _exportTask = _modelConverter.ExportResourceTree(resourceTree, exportOptions,
                    true,
                    _selectedExportTypeFlags, Plugin.TempDirectory, _exportCts.Token);
            }

            ImGui.SameLine();
            // export all button
            if (ImGui.Button("Export All") && _exportTask == null)
            {
                _exportTask = _modelConverter.ExportResourceTree(resourceTree,
                    new bool[resourceTree.Nodes.Length].Select(_ => true).ToArray(),
                    true,
                    _selectedExportTypeFlags,
                    Plugin.TempDirectory,
                    _exportCts.Token);
            }

            // exportType option, checkboxes for types
            var exportTypeFlags = (int) _selectedExportTypeFlags;
            ImGui.SameLine();
            ImGui.CheckboxFlags("Gltf", ref exportTypeFlags, (int) ExportType.Gltf);
            ImGui.SameLine();
            ImGui.CheckboxFlags("Glb", ref exportTypeFlags, (int) ExportType.Glb);
            ImGui.SameLine();
            ImGui.CheckboxFlags("Wavefront", ref exportTypeFlags, (int) ExportType.Wavefront);
            if (exportTypeFlags != (int) _selectedExportTypeFlags)
            {
                _selectedExportTypeFlags = (ExportType) exportTypeFlags;
            }
        }

        // cancel button
        if (_exportTask == null)
        {
            ImGui.SameLine();
            ImGui.Text("No export in progress");
        }
        else
        {
            if (_exportTask.IsCompleted)
            {
                _exportTask = null!;
            }
            else if (_exportTask.IsCanceled)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted("Export Cancelled...");
            }
            else
            {
                ImGui.SameLine();
                if (ImGui.Button("Cancel Export"))
                {
                    _exportCts.Cancel();
                    _exportCts.Dispose();
                    _exportCts = new CancellationTokenSource();
                }
            }
        }

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
                            if (ImGui.Button($"{FontAwesomeIcon.FileExport.ToIconString()}##{node.GetHashCode()}") && _exportTask == null)
                            {
                                var tmpExportOptions = new bool[resourceTree.Nodes.Length];
                                tmpExportOptions[i] = true;
                                _exportTask = _modelConverter.ExportResourceTree(resourceTree, tmpExportOptions,
                                    true,
                                    _selectedExportTypeFlags,
                                    Plugin.TempDirectory,
                                    _exportCts.Token);
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

    private void DrawCopyableText(string text)
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

    private void DrawResourceNode(Node node)
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

    private Task<(string, ResourceTree, DateTime, bool[])> LoadResourceListFromDisk(string pathToFile)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!File.Exists(pathToFile))
                {
                    throw new Exception("No resource file found");
                }

                var contents = File.ReadAllText(pathToFile);
                var resourceTree = JsonConvert.DeserializeObject<ResourceTree>(contents);

                if (resourceTree == null)
                {
                    throw new Exception("No resource trees found");
                }

                if (resourceTree.Nodes.Length == 0)
                {
                    throw new Exception("No resources found");
                }

                var contentHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(contents)));
                var exportOptions = new bool[resourceTree.Nodes.Length];
                return (contentHash, resourceTree, DateTime.UtcNow, exportOptions);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error loading resources from file");
                throw;
            }
        });
    }


    private Task<(string hash, ResourceTree tree, DateTime refreshedAt, bool[] exportOptions)> TryBuildResourceList(string name, ushort selectedObjectObjectIndex)
    {
        return Task.Run(() =>
        {
            try
            {
                var characterInfo = CharacterUtility.GetCharacterInfo(selectedObjectObjectIndex, _objectTable, _luminaManager);
                if (characterInfo == null)
                {
                    throw new Exception("Failed to get character info");
                }
                
                var resourceTree = characterInfo.AsResourceTree(name, _pluginInterface);
                
                Directory.CreateDirectory(Plugin.TempDirectory);
                var content = JsonConvert.SerializeObject(resourceTree, Formatting.Indented);
                var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
                File.WriteAllText(Path.Combine(Plugin.TempDirectory, $"{name}.json"), content);
                
                return (hash, resourceTree, DateTime.UtcNow, new bool[resourceTree.Nodes.Length]);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error loading resources");
                throw;
            }
        });
    }

    public void Dispose()
    {
        _resourceTask?.Dispose();
        _exportCts.Dispose();
        _exportTask?.Dispose();
    }
}