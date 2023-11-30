using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Meddle.Plugin.UI.Shared;
using Meddle.Xande.Utility;
using Newtonsoft.Json;
using Penumbra.Api;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xande;

namespace Meddle.Plugin.UI;

public class ResourceTab : ITab
{
    public string Name => "Resources";
    public int Order => 1;
    private readonly FileDialogManager _fileDialogManager;
    private Task<(string hash, Ipc.ResourceTree tree, DateTime refreshedAt, bool[] exportOptions)>? _resourceTask;
    // storing result separately so it doesn't disappear while running a new task
    private (string hash, Ipc.ResourceTree tree, DateTime refreshedAt, bool[] exportOptions)? _resourceTaskResult;
    private (string name, int index) _selectedGameObject;
    private string _searchFilter = string.Empty;
    private readonly LuminaManager _luminaManager;
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;
    private readonly IObjectTable _objectTable;
    private readonly ResourceTreeRenderer _resourceTreeRenderer;


    public ResourceTab(DalamudPluginInterface pluginInterface, LuminaManager luminaManager, IPluginLog log, IObjectTable objectTable, ResourceTreeRenderer resourceTreeRenderer)
    {
        _pluginInterface = pluginInterface;
        _log = log;
        _objectTable = objectTable;
        _resourceTreeRenderer = resourceTreeRenderer;
        _luminaManager = luminaManager;
        _fileDialogManager = new FileDialogManager();
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
        _resourceTreeRenderer.DrawResourceTree(resourceTaskResult.tree, ref resourceTaskResult.exportOptions);
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

    private Task<(string, Ipc.ResourceTree, DateTime, bool[])> LoadResourceListFromDisk(string pathToFile)
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
                var resourceTree = JsonConvert.DeserializeObject<Ipc.ResourceTree>(contents);

                if (resourceTree == null)
                {
                    throw new Exception("No resource trees found");
                }

                if (!resourceTree.Nodes.Any())
                {
                    throw new Exception("No resources found");
                }

                var contentHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(contents)));
                var exportOptions = new bool[resourceTree.Nodes.Count];
                return (contentHash, resourceTree, DateTime.UtcNow, exportOptions);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error loading resources from file");
                throw;
            }
        });
    }


    private Task<(string hash, Ipc.ResourceTree tree, DateTime refreshedAt, bool[] exportOptions)> TryBuildResourceList(string name, ushort selectedObjectObjectIndex)
    {
        return Task.Run(() =>
        {
            try
            {
                var resourceTree = Ipc.GetGameObjectResourceTrees.Subscriber(_pluginInterface).Invoke(true, selectedObjectObjectIndex)[0]!;

                Directory.CreateDirectory(Plugin.TempDirectory);
                var content = JsonConvert.SerializeObject(resourceTree, Formatting.Indented);
                var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
                File.WriteAllText(Path.Combine(Plugin.TempDirectory, $"{name}.json"), content);

                _log.Information($"Built resource tree for {name}");
                return (hash, resourceTree, DateTime.UtcNow, new bool[resourceTree.Nodes.Count]);
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
    }
}
