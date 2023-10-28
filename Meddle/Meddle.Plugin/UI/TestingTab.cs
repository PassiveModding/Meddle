using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ImGuiNET;
using Lumina.Data.Files;
using Meddle.Lumina.Models;
using Meddle.Xande;
using Meddle.Xande.Enums;
using Meddle.Xande.Models;
using Xande;

namespace Meddle.Plugin.UI;

public class TestingTab : ITab
{
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;
    private readonly ModelConverter _modelConverter;
    private readonly LuminaManager _luminaManager;

    public void Dispose()
    {
        // TODO release managed resources here
    }
    
    public TestingTab(IObjectTable objectTable, IPluginLog log, ModelConverter modelConverter, LuminaManager luminaManager)
    {
        _objectTable = objectTable;
        _log = log;
        _modelConverter = modelConverter;
        _luminaManager = luminaManager;
    }

    public string Name => "Testing";
    public int Order => 2;
    private string _models = "";
    private Task<(string path, Model? model)>? _modelTask;
    private Task? _exportTask;
    public void Draw()
    {
        ImGui.Text("Models Paths");
        var input = _models;
        if (ImGui.InputText("##models", ref input, 1000))
        {
            _models = input;
        }
        
        if (_modelTask == null || _modelTask.IsCompleted)
        {
            if (ImGui.Button("Load Model"))
            {
                _modelTask = ModelTask(input);
            }
        }

        if (_modelTask == null || !_modelTask.IsCompletedSuccessfully) return;
        var model = _modelTask.Result;
        ImGui.Text(model.path);
        if (model.model == null)
        {
            ImGui.Text("Failed to load model");
            return;
        }
            
        ImGui.Text($"Materials: {model.model.Materials?.Length}");
        ImGui.Text($"Meshes: {model.model.Meshes?.Length}");
        ImGui.Text($"Shapes: {model.model.Shapes?.Count}");
            
        if (ImGui.Button("Export") && (_exportTask == null || _exportTask.IsCompleted))
        {
            _exportTask = _modelConverter.ExportModel(Plugin.TempDirectory, model.model, ExportType.Gltf);
        }

        if (_exportTask == null || !_exportTask.IsCompleted) return;
        if (_exportTask.IsCompletedSuccessfully)
        {
            ImGui.Text("Exported");
        }
        else
        {
            ImGui.Text("Failed to export");
            if (_exportTask.Exception == null) return;
            ImGui.TextWrapped(_exportTask.Exception.ToString() ?? "");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Click to copy");
            }
                        
            if (ImGui.IsItemClicked())
            {
                ImGui.SetClipboardText(_exportTask.Exception.ToString() ?? "");
            }
        }
    }

    private Task<(string, Model?)> ModelTask(string modelPath)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                return (modelPath, null);
            }

            var modelFile = _luminaManager.GetFile<MdlFile>(modelPath);
            if (modelFile == null)
            {
                return (modelPath, null);
            }
            
            var model = new Model(modelFile);
            return (modelPath, model);
        });
    }
}