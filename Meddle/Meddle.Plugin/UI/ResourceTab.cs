using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Meddle.Plugin.UI.Shared;
using Meddle.Xande;
using Meddle.Xande.Utility;
using Newtonsoft.Json;
using Penumbra.Api;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Meddle.Plugin.UI;

public class ResourceTab : ITab
{
    public string Name => "Resources";
    public int Order => 1;

    private FileDialogManager FileDialogManager { get; }

    private Task<(string hash, CharacterTree tree, DateTime refreshedAt)>? ResourceTask { get; set; }
    // storing result separately so it doesn't disappear while running a new task
    private (string hash, CharacterTree tree, DateTime refreshedAt)? ResourceTaskResult { get; set; }
    private (string name, int index) SelectedGameObject { get; set; }
    private string SearchFilter { get; set; } = string.Empty;

    private DalamudPluginInterface PluginInterface { get; }
    private IPluginLog Log { get; }
    private IObjectTable ObjectTable { get; }
    private ResourceTreeRenderer ResourceTreeRenderer { get; }


    public ResourceTab(DalamudPluginInterface pluginInterface, IPluginLog log, IObjectTable objectTable, ResourceTreeRenderer resourceTreeRenderer)
    {
        PluginInterface = pluginInterface;
        Log = log;
        ObjectTable = objectTable;
        ResourceTreeRenderer = resourceTreeRenderer;
        FileDialogManager = new FileDialogManager();
        SelectedGameObject = ("None", -1);
    }

    public void Draw()
    {
        DrawObjectPicker();
        if (ResourceTask == null)
        {
            ImGui.Text("No resources found");
            return;
        }

        if (ResourceTaskResult != null)
        {
            // check if hash is different
            if (ResourceTask != null && ResourceTask.IsCompletedSuccessfully &&
                ResourceTask.Result.hash != ResourceTaskResult.Value.hash)
            {
                ResourceTaskResult = ResourceTask.Result;
            }
        }
        else if (ResourceTask != null)
        {
            if (!ResourceTask.IsCompleted)
            {
                ImGui.Text("Loading...");
            }
            else if (ResourceTask.Exception != null)
            {
                ImGui.Text($"Error loading resources\n\n{ResourceTask.Exception}");
            }
            else if (ResourceTask.IsCompletedSuccessfully)
            {
                ResourceTaskResult = ResourceTask.Result;
            }
        }

        // dropdown to select
        if (!ResourceTaskResult.HasValue)
        {
            ImGui.Text("No resources found");
            return;
        }

        using var child = ImRaii.Child("##Data");
        if (!child)
            return;

        var resourceTaskResult = ResourceTaskResult.Value;
        ResourceTreeRenderer.DrawResourceTree(resourceTaskResult.tree);
    }

    private void DrawObjectPicker()
    {
        FileDialogManager.Draw();
        // show load from disk button
        if (ImGui.Button("Load from disk"))
        {
            FileDialogManager.OpenFileDialog("Select Resource File", "Json Files{.json}", (selected, paths) =>
            {
                if (!selected) return;
                if (paths.Count == 0)
                {
                    return;
                }

                var path = paths[0];
                ResourceTask = LoadResourceListFromDisk(path);
            }, 1, startPath: Plugin.TempDirectory, isModal: true);
        }

        ImGui.SameLine();
        if (ImGui.Button("Open Temp Directory"))
        {
            Process.Start("explorer.exe", Plugin.TempDirectory);
        }


        var objects = ObjectTable.Where(x => x.IsValid())
            .Where(x =>
                x.ObjectKind is ObjectKind.Player or ObjectKind.BattleNpc or ObjectKind.Retainer or ObjectKind.EventNpc
                    or ObjectKind.Companion
            )
            .Where(x => CharacterUtility.HasDrawObject(x.ObjectIndex, ObjectTable))
            .ToArray();
        if (objects.Length == 0)
        {
            ImGui.Text("No game objects found");
        }
        else
        {
            // combo for selecting game object
            ImGui.Text("Select Game Object");
            var selected = SelectedGameObject.index;
            var filter = SearchFilter;
            if (ImGui.InputText("##Filter", ref filter, 100))
            {
                SearchFilter = filter;
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
                SelectedGameObject = (names[0], 0);
            }

            if (ImGui.Combo("##GameObject", ref selected, names, names.Length))
            {
                SelectedGameObject = (names[selected], selected);
                var selectedObject = objects[selected];
                ResourceTask = TryBuildResourceList(selectedObject.Name.ToString(), selectedObject.ObjectIndex);
            }

            ImGui.SameLine();
            // left/right arrow buttons
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button($"{FontAwesomeIcon.ArrowLeft.ToIconString()}##Left") && SelectedGameObject.index > 0)
                {
                    var idx = SelectedGameObject.index - 1;
                    SelectedGameObject = (names[idx], idx);
                    var selectedObject = objects[idx];
                    ResourceTask = TryBuildResourceList(selectedObject.Name.ToString(), selectedObject.ObjectIndex);
                }

                ImGui.SameLine();
                if (ImGui.Button($"{FontAwesomeIcon.ArrowRight.ToIconString()}##Right") &&
                    SelectedGameObject.index < objects.Length - 1)
                {
                    var idx = SelectedGameObject.index + 1;
                    SelectedGameObject = (names[idx], idx);
                    var selectedObject = objects[SelectedGameObject.index];
                    ResourceTask = TryBuildResourceList(selectedObject.Name.ToString(), selectedObject.ObjectIndex);
                }
            }

            if (ImGui.Button("Refresh") && SelectedGameObject.index != -1 &&
                (ResourceTask == null || ResourceTask.IsCompleted))
            {
                var selectedObject = objects[SelectedGameObject.index];
                ResourceTask = TryBuildResourceList(selectedObject.Name.ToString(), selectedObject.ObjectIndex);
            }
        }
    }

    private Task<(string, CharacterTree, DateTime)> LoadResourceListFromDisk(string pathToFile)
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
                var resourceTree = JsonConvert.DeserializeObject<CharacterTree>(contents);

                if (resourceTree == null)
                {
                    throw new Exception("No resource trees found");
                }

                if (!resourceTree.Nodes.Any())
                {
                    throw new Exception("No resources found");
                }

                var contentHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(contents)));
                return (contentHash, resourceTree, DateTime.UtcNow);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error loading resources from file");
                throw;
            }
        });
    }

    private Task<(string hash, CharacterTree tree, DateTime refreshedAt)> TryBuildResourceList(string name, ushort selectedObjectObjectIndex)
    {
        return Task.Run(() =>
        {
            try
            {
                var resourceTree = Ipc.GetGameObjectResourceTrees.Subscriber(PluginInterface).Invoke(true, selectedObjectObjectIndex)[0]!;
                var tree = new CharacterTree(resourceTree);

                Directory.CreateDirectory(Plugin.TempDirectory);
                var content = JsonConvert.SerializeObject(tree, Formatting.Indented);
                var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
                File.WriteAllText(Path.Combine(Plugin.TempDirectory, $"{name}.json"), content);

                Log.Information($"Built resource tree for {name}");
                return (hash, tree, DateTime.UtcNow);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error loading resources");
                throw;
            }
        });
    }

    public void Dispose()
    {
        ResourceTask?.Dispose();
    }
}
