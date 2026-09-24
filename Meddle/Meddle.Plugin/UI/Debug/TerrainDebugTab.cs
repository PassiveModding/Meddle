using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using Meddle.Formats.Files;
using Meddle.Formats.Helpers;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.UI.Windows;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.UI.Debug;

public class TerrainDebugTab : ITab
{
    private readonly SigUtil sigUtil;
    private readonly MdlMaterialWindowManager mdlMaterialWindowManager;
    private readonly SqPack.SqPack pack;
    private Dictionary<string, object?> fileCache = new();
    public TerrainDebugTab(SigUtil sigUtil, 
                           MdlMaterialWindowManager mdlMaterialWindowManager, SqPack.SqPack pack)
    {
        this.sigUtil = sigUtil;
        this.mdlMaterialWindowManager = mdlMaterialWindowManager;
        this.pack = pack;
    }
    
    
    public string Name => "Terrain Debug";
    public int Order => 3;
    public MenuType MenuType => MenuType.Debug;
    public unsafe void Draw()
    {
        var renderManager = Manager.Instance();
        UiUtil.Text($"Render Manager: {(nint)renderManager:x8}", $"{(nint)renderManager:x8}");

        var world = sigUtil.GetLayoutWorld();
        if (world == null || world->ActiveLayout == null) return;

        using (var layersTree = ImRaii.TreeNode("Layers"))
        {
            if (layersTree)
            {
                foreach (var layer in world->ActiveLayout->Layers)
                {
                    UiUtil.Text($"Layer: {layer.Item1} - {(nint)layer.Item2.Value:X8}", $"{(nint)layer.Item2.Value:X8}");
                    var layerPtr = layer.Item2.Value;
                    if (layerPtr == null) continue;
                    foreach (var instance in layerPtr->Instances)
                    {
                        UiUtil.Text($"Instance: {instance.Item1} - {(nint)instance.Item2.Value:X8}", $"{(nint)instance.Item2.Value:X8}");
                    }
                }
            }
        }

        foreach (var terrain in world->ActiveLayout->Terrains)
        {
            UiUtil.Text($"Terrain: {terrain.Item1} - {(nint)terrain.Item2.Value:X8}", $"{(nint)terrain.Item2.Value:X8}");
            var terrainPtr = terrain.Item2.Value;
            if (terrainPtr == null) continue;
            UiUtil.Text($"GfxTerrain: {(nint)terrainPtr->GfxTerrain:X8}", $"{(nint)terrainPtr->GfxTerrain:X8}");
            if (terrainPtr->GfxTerrain == null) continue;

            var terrainFileName = terrainPtr->GfxTerrain->TerrainResourceHandle->FileName.ToString();
            var grassRoot = $"{terrainFileName.Split("/bgplate/")[0]}/grass";
            var grassPath = $"{grassRoot}/grass_zone_data.gzd";
            using var tree = ImRaii.TreeNode($"Terrain Resource Handle: {(nint)terrainPtr->GfxTerrain->TerrainResourceHandle:X8} {terrainFileName} {grassPath}");
            if (tree)
            {
                UiUtil.Text($"{terrainFileName}", terrainFileName);
                UiUtil.Text($"{grassPath}", grassPath);
                if (!fileCache.TryGetValue(grassPath, out var gzdFileObj))
                {
                    var grassData = pack.GetFileOrReadFromDisk(grassPath);
                    if (grassData == null)
                    {
                        gzdFileObj = null;
                    }
                    else
                    {
                        gzdFileObj = new GzdFile(grassData);
                    }
                    
                    fileCache[grassPath] = gzdFileObj;
                }

                if (gzdFileObj is GzdFile gzdFile)
                {
                    for (int i = 0; i < gzdFile.CellsH.Length; i++)
                    {
                        var cell = gzdFile.CellsH[i];
                        using var cellTree =
                            ImRaii.TreeNode($"Cell{i}: {cell.GgdName}, Radius: {cell.Radius}, Center:{cell.Center}");
                        if (cellTree)
                        {
                            var cellFileName = $"{grassRoot}/{cell.GgdName}";
                            if (!fileCache.TryGetValue(cellFileName, out var ggdFileObj))
                            {
                                var ggdFileData = pack.GetFileOrReadFromDisk(cellFileName);
                                if (ggdFileData == null)
                                {
                                    ggdFileObj = null;
                                }
                                else
                                {
                                    ggdFileObj = new GgdFile(ggdFileData);
                                }
                                
                                fileCache[cellFileName] = ggdFileObj;
                            }

                            if (ggdFileObj is GgdFile ggdFile)
                            {
                                for (var recordIdx = 0; recordIdx < ggdFile.Records.Length; recordIdx++)
                                {
                                    var ggdRecord = ggdFile.Records[recordIdx];
                                    ImGui.Text($"Record {recordIdx}");
                                    var modelCounts = new Dictionary<string, int>();
                                    for (var modelPathIdx = 0; modelPathIdx < gzdFile.ModelPaths.Length; modelPathIdx++)
                                    {
                                        var modelPath = gzdFile.ModelPaths[modelPathIdx];
                                        var modelCount = ggdRecord.Header.ModelCounts[i];
                                        modelCounts[modelPath] = modelCount;
                                    }

                                    foreach (var modelCount in modelCounts.OrderByDescending(x => x.Value))
                                    {
                                        ImGui.Text($"{modelCount.Key}: {modelCount.Value}");
                                    }
                                }
                            }
                        }
                    }
                    for (int i = 0; i < gzdFile.ModelPaths.Length; i++)
                    {
                        var path = gzdFile.ModelPaths[i];
                        UiUtil.Text($"ModelPath{i}: {path}", path);
                    }
                    for (int i = 0; i < gzdFile.TextureSuffixes.Length; i++)
                    {
                        var textureSuffix = gzdFile.TextureSuffixes[i];
                        ImGui.Text($"TextureSuffix{i}: {textureSuffix}");
                    }
                }
                
                
                var terrainStruct = (Terrain*)terrainPtr->GfxTerrain;
                var terrainModels = terrainStruct->ModelResourceHandlesSpan;
                for (var i = 0; i < terrainStruct->ModelResourceHandleCount; i++)
                {
                    var modelHandle = terrainModels[i].Value;
                    if (modelHandle == null)
                    {
                        UiUtil.Text($"Model Resource Handle {i}: null", $"ModelResourceHandle_{i}_null");
                        continue;
                    }
                    
                    var fileName = modelHandle->FileName.ParseString();
                    using var modelTree = ImRaii.TreeNode($"Model Resource Handle {i}: {(nint)terrainModels[i].Value:X8} - {fileName}");
                    if (modelTree)
                    {
                        UiUtil.Text($"File Name: {fileName}", fileName);
                        if (ImGui.Button($"Preview material"))
                        {
                            mdlMaterialWindowManager.AddMaterialWindow(modelHandle);
                        }

                        var modelData = new ModelResourceHandleData(modelHandle->ModelData);
                        for (int j = 0; j < modelData.ModelHeader.MaterialCount; j++)
                        {
                            var materialHandle = modelHandle->MaterialResourceHandles[j];
                            if (materialHandle == null) continue;

                            var materialFileName = materialHandle->FileName.ParseString();
                            UiUtil.Text($"Material {j}: {materialFileName}", materialFileName);
                        }
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }
}
