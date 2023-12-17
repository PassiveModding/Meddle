using System.Numerics;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Lumina.Data.Files;
using Lumina.Models.Models;
using Meddle.Plugin.UI.Shared;
using Meddle.Xande.Models;
using Penumbra.Api;
using Penumbra.Api.Enums;
using Xande;
using Xande.Enums;

namespace Meddle.Plugin.UI;

public class TestingTab : ITab
{
    private readonly IPluginLog _log;
    private readonly LuminaManager _luminaManager;
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly ResourceTreeRenderer _resourceTreeRenderer;
    
    public TestingTab(IPluginLog log, LuminaManager luminaManager, DalamudPluginInterface pluginInterface, ResourceTreeRenderer resourceTreeRenderer)
    {
        _log = log;
        _luminaManager = luminaManager;
        _pluginInterface = pluginInterface;
        _resourceTreeRenderer = resourceTreeRenderer;
    }

    public string Name => "Testing";
    public int Order => 2;
    private string _modelPath = "";
    private string _skeletons = "";
    private GenderRace _genderRace = GenderRace.Unknown;
    private Task<ResourceTree?>? _modelTask;
    private bool[] _exportOptions = Array.Empty<bool>();

    public void Draw()
    {
        ImGui.Text("Model Path");
        var input = _modelPath;
        if (ImGui.InputText("##models", ref input, 1000))
        {
            _modelPath = input;
        }
        
        var skeletons = _skeletons;
        ImGui.Text("Skeleton Paths");
        if (ImGui.InputTextMultiline("##skeletons", ref skeletons, 1000, new Vector2(0,0)))
        {
            _skeletons = skeletons;
        }
        
        // dropdown genderrace select
        var genderRace = _genderRace;
        var options = Enum.GetValues<GenderRace>();
        ImGui.PushItemWidth(180);
        if (ImGui.BeginCombo("##genderrace", genderRace.ToString()))
        {
            foreach (var option in options)
            {
                var isSelected = option == genderRace;
                if (ImGui.Selectable(option.ToString(), isSelected))
                {
                    _genderRace = option;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
        
        ImGui.SameLine();
        ImGui.Text($"{(int)genderRace}");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Used to set deform steps for the model");
        }
        
        
        
        if (_modelTask == null || _modelTask.IsCompleted)
        {
            if (ImGui.Button("Load Model"))
            {
                _modelTask = ModelToResourceTree(_modelPath, genderRace, skeletons.Split("\n"));
            }
        }

        if (_modelTask == null || !_modelTask.IsCompletedSuccessfully) return;
        var tree = _modelTask.Result;
        if (tree == null) return;
        
        if (_exportOptions.Length != tree.Nodes.Length)
        {
            _exportOptions = new bool[tree.Nodes.Length];
        }
        
        _resourceTreeRenderer.DrawResourceTree(tree, ref _exportOptions);
    }

    private Task<ResourceTree?> ModelToResourceTree(string modelPath, GenderRace genderRace, string[] skeletons)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            return Task.FromResult<ResourceTree?>(null);
        }
        
        var modelFile = _luminaManager.GetFile<MdlFile>(modelPath);
        if (modelFile == null)
        {
            return Task.FromResult<ResourceTree?>(null);
        }
        
        var model = new Model(modelFile);

        var name = Path.GetFileNameWithoutExtension(modelPath);

        var resourceTree = new ResourceTree(name, genderRace);
        var rootNodes = new List<Node>();
        var modelNode = new Node(modelPath, modelPath, ResourceType.Mdl.ToString(), ResourceType.Mdl);
        rootNodes.Add(modelNode);
        
        var modelChildren = new List<Node>();
        
        foreach (var material in model.Materials)
        {
            material.Update(_luminaManager.GameData);
            
            var materialPath = material.ResolvedPath ?? material.MaterialPath;
            var resolvedPath = Ipc.ResolveGameObjectPath.Subscriber(_pluginInterface)
                .Invoke(materialPath, 0);
            
            var materialNode = new Node(resolvedPath, materialPath, ResourceType.Mtrl.ToString(), ResourceType.Mtrl);
            var materialChildren = new List<Node>();
            var resolvedMaterial = _luminaManager.GetMaterial(resolvedPath, materialPath);

            foreach (var texture in resolvedMaterial.Textures)
            {
                var resolvedTexturePath = Ipc.ResolveGameObjectPath.Subscriber(_pluginInterface)
                    .Invoke(texture.TexturePath, 0);
                
                var textureNode = new Node(resolvedTexturePath, texture.TexturePath, ResourceType.Tex.ToString(), ResourceType.Tex);
                materialChildren.Add(textureNode);
            }

            materialNode.Children = materialChildren.ToArray();
            modelChildren.Add(materialNode);
        }
        
        foreach (var skeleton in skeletons.Where(x => !string.IsNullOrWhiteSpace(x) && x.EndsWith(".sklb", StringComparison.OrdinalIgnoreCase)))
        {
            var resolvedPath = Ipc.ResolveGameObjectPath.Subscriber(_pluginInterface)
                .Invoke(skeleton, 0);
            
            var skeletonNode = new Node(resolvedPath, skeleton, ResourceType.Sklb.ToString(), ResourceType.Sklb);
            rootNodes.Add(skeletonNode);
        }
        
        modelNode.Children = modelChildren.ToArray();
        resourceTree.Nodes = rootNodes.ToArray();
        
        // log tree
        var treeJson = JsonSerializer.Serialize(resourceTree, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        File.WriteAllText(Path.Combine(_pluginInterface.GetPluginConfigDirectory(), $"{name}.json"), treeJson);
        return Task.FromResult(resourceTree)!;
    }
    
    public void Dispose()
    {
        _modelTask?.Dispose();
    }
}