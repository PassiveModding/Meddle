using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Customize;
using Meddle.Plugin.Models.ResourceTree;
using Meddle.Plugin.Services;
using Penumbra.Api.Enums;
using Penumbra.GameData.Files;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Meddle.Plugin.UI;

public class GameObjectTab : ITab
{
    private readonly ExportService _exportService;
    private readonly FileService _fileService;
    private readonly IObjectTable _objectTable;
    private readonly DalamudPluginInterface _pi;
    private readonly StainService _stainService;
    private readonly IPluginLog _logger;
    private Task<(string hash, ResourceTree tree, DateTime refreshedAt)>? _resourceTask;
    private ExportConfig _config;
    private Task _exportTask = Task.CompletedTask;

    public void Dispose()
    {
        //
    }
    
    public GameObjectTab(ExportService exportService, FileService fileService, IObjectTable objectTable, DalamudPluginInterface pi,
        StainService stainService, IPluginLog logger)
    {
        _exportService = exportService;
        _fileService = fileService;
        _objectTable = objectTable;
        _pi = pi;
        _stainService = stainService;
        _logger = logger;
        _config = new ExportConfig();
    }

    public string Name => "Game Objects";
    public int Order => 0;
    
    public void Draw()
    {
        if (ImGui.Button("Open Temp Directory"))
        {
            Process.Start("explorer.exe", Plugin.TempDirectory);
        }
        
        DrawObjectPicker();   
        if (_resourceTask == null)
        {
            ImGui.Text("No resources found");
            return;
        }

        if (_resourceTask != null)
        {
            if (!_resourceTask.IsCompleted)
            {
                ImGui.Text("Loading...");
            }
            else if (_resourceTask.Exception != null)
            {
                ImGui.Text($"Error loading resources\n\n{_resourceTask.Exception}");
            }
        }
        
        DrawResourceList();
    }
    
    private void DrawResourceList()
    {
        if (_resourceTask.Result == default)
        {
            ImGui.Text("No resources found");
            return;
        }

        var (hash, tree, refreshedAt) = _resourceTask.Result;
        ImGui.Text($"Last refreshed at {refreshedAt}");
        ImGui.Text($"Hash: {hash}");
        ImGui.Text($"Is Exporting: {_exportService.IsRunning}");
        ImGui.Separator();

        if (ImGui.Button("Export") && _exportService.IsRunning == false)
        {
            var tmpDir = Path.Combine(Plugin.TempDirectory, DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss"));
            Directory.CreateDirectory(tmpDir);
            var path = Path.Combine(tmpDir, "out.gltf");
            _exportTask = _exportService.Execute(_config, tree.Nodes.ToArray(), new Dictionary<string, MtrlFile.ColorTable>(), tree.GenderRace, path, new CancellationToken());
        }
        
        DrawResourceTree(tree);
    }
    
    private void DrawResourceTree(ResourceTree tree)
    {
        if (ImGui.CollapsingHeader("Customize"))
        {
            tree.Customize.Draw(_stainService);
        }
        
        if (ImGui.CollapsingHeader("Resources"))
        {
            var mdlNodes = tree.Nodes.Where(x => x.Type == ResourceType.Mdl).ToArray();
            if (ImGui.CollapsingHeader($"Models ({mdlNodes.Length})"))
            {
                foreach (var node in mdlNodes)
                {
                    DrawResourceNode(tree, node);
                }
            }
            var sklNodes = tree.Nodes.Where(x => x.Type == ResourceType.Sklb).ToArray();
            if (ImGui.CollapsingHeader($"Skeletons ({sklNodes.Length})"))
            {
                foreach (var node in sklNodes)
                {
                    DrawResourceNode(tree, node);
                }
            }
            var texNodes = tree.Nodes.Where(x => x.Type == ResourceType.Tex).ToArray();
            if (ImGui.CollapsingHeader($"Textures ({texNodes.Length})"))
            {
                foreach (var node in texNodes)
                {
                    DrawResourceNode(tree, node);
                }
            }
        }
    }
    
    private void DrawResourceNode(ResourceTree tree, ResourceTree.ResourceNode node)
    {
        // three columns, first is the name, second is the game path, third is the actual path
        ImGui.Columns(3, "ResourceNodeColumns", true);
        //_logger.Debug($"Drawing node {node.Name}\n{node.GamePath}\n{node.ActualPath}\n{node.Type}");
        try
        {
            if (node.Children.Length <= 0)
            {
                ImGui.Text(node.Name);
                ImGui.NextColumn();
                ImGui.Text(node.GamePath);
                ImGui.NextColumn();
                ImGui.Text(node.ActualPath);
                ImGui.NextColumn();
                return;
            }
            
            if (node.Type == ResourceType.Mdl)
            {
                // individual node export
                if (ImGui.Button($"Export##{node.GetHashCode()}") && _exportService.IsRunning == false)
                {
                    var tmpDir = Path.Combine(Plugin.TempDirectory,
                        DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss"));
                    Directory.CreateDirectory(tmpDir);
                    var path = Path.Combine(tmpDir, $"out.gltf");

                    var nodes = tree.Nodes.Where(x =>
                        (x.Type == ResourceType.Mdl && x == node) ||
                        x.Type == ResourceType.Sklb ||
                        x.Type == ResourceType.Tex).ToArray();

                    _exportTask = _exportService.Execute(_config, nodes,
                        new Dictionary<string, MtrlFile.ColorTable>(), tree.GenderRace, path,
                        new CancellationToken());
                }

                ImGui.SameLine();
            }

            using (var open = ImRaii.TreeNode(node.Name, ImGuiTreeNodeFlags.CollapsingHeader))
            {
                ImGui.NextColumn();
                ImGui.Text(node.GamePath);
                ImGui.NextColumn();
                ImGui.Text(node.ActualPath);
                ImGui.NextColumn();

                if (open)
                {
                    foreach (var child in node.Children)
                    {
                        DrawResourceNode(tree, child);
                    }
                }
            }
        }
        finally
        {
            ImGui.Columns(1);
        }
    }
    
    private (int index, string name) _selectedGameObject = (-1, string.Empty);
    private string _searchFilter = string.Empty;
    private void DrawObjectPicker()
    {
        var objects = _objectTable.Where(x => x.IsValid())
            .Where(x =>
                x.ObjectKind is ObjectKind.Player or 
                    ObjectKind.BattleNpc or 
                    ObjectKind.Retainer or 
                    ObjectKind.EventNpc or 
                    ObjectKind.Companion
            )
            .Where(x =>
            {
                unsafe
                {
                    var gameObject = (GameObject*) x.Address;
                    var drawObject = gameObject->DrawObject;
                    if (drawObject == null)
                    {
                        return false;
                    }
                }
                
                return true;
            })
            .ToArray();
        
        if (objects.Length == 0)
        {
            ImGui.Text("No game objects found");
            return;
        }

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
            var name = x.Name.TextValue;
            if (string.IsNullOrEmpty(name))
            {
                name = "Unnamed";
            }

            var dist = new Vector3(x.YalmDistanceX, 0, x.YalmDistanceZ);
            var distance = dist.Length();
            return $"{x.Address:X8}:{x.ObjectId:X} - {x.ObjectKind} - {name} - {distance:00}y";
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
            _resourceTask = TryBuildResourceList(selectedObject.ObjectIndex);
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
                _resourceTask = TryBuildResourceList(selectedObject.ObjectIndex);
            }

            ImGui.SameLine();
            if (ImGui.Button($"{FontAwesomeIcon.ArrowRight.ToIconString()}##Right") &&
                _selectedGameObject.index < objects.Length - 1)
            {
                _selectedGameObject.index++;
                _selectedGameObject.name = names[_selectedGameObject.index];
                var selectedObject = objects[_selectedGameObject.index];
                _resourceTask = TryBuildResourceList(selectedObject.ObjectIndex);
            }
        }

        if (ImGui.Button("Refresh") && _selectedGameObject.index != -1 &&
            (_resourceTask == null || _resourceTask.IsCompleted))
        {
            var selectedObject = objects[_selectedGameObject.index];
            _resourceTask = TryBuildResourceList(selectedObject.ObjectIndex);
        }
    }
    
    private Task<(string hash, ResourceTree tree, DateTime refreshedAt)> TryBuildResourceList(ushort index)
    {
        return Task.Run(() =>
        {
            try
            {
                //CustomizeResult customizeResult;
                //var cmp = new CmpFile(_fileService.ReadFile("chara/xls/charamake/human.cmp") ?? throw new Exception("Failed to read customize file"));
                string name;
                Customize? customizeResult;
                unsafe
                {
                    var obj = _objectTable[index] ?? throw new Exception("Failed to get object");
                    var dalamudChar = _objectTable.OfType<Character>().First(x => x.ObjectIndex == index);
                    
                    customizeResult = Customize.FromCharacter(_pi, dalamudChar) ?? throw new Exception("Failed to get customization");
                    
                    //var xivChar = (XivCharacter*) obj.Address;
                    //var customize = (CustomizeArray*)xivChar->DrawData.CustomizeData.Data;
                    
                    name = $"{obj.Name}-{obj.ObjectId}";
                    //customizeResult = CustomizeUtility.ParseCustomize(cmp, ref customize);
                }
                
                _config.Customize = customizeResult;
                var resourceTree = ResourceTree.GetResourceTree(_pi, index, customizeResult);
                

                Directory.CreateDirectory(Plugin.TempDirectory);
                var content = JsonSerializer.Serialize(resourceTree, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
                File.WriteAllText(Path.Combine(Plugin.TempDirectory, $"{name}.json"), content);

                _logger.Information($"Built resource tree for {name}");
                
                return (hash, resourceTree, DateTime.Now);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get resource list");
                throw;
            }
        });
    }
}