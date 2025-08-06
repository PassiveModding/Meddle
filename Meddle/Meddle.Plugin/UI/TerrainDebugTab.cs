using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Services;
using Meddle.Plugin.UI.Windows;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.UI;

public class TerrainDebugTab : ITab
{
    private readonly SigUtil sigUtil;
    private readonly MdlMaterialWindowManager mdlMaterialWindowManager;
    public TerrainDebugTab(SigUtil sigUtil, 
                           MdlMaterialWindowManager mdlMaterialWindowManager)
    {
        this.sigUtil = sigUtil;
        this.mdlMaterialWindowManager = mdlMaterialWindowManager;
    }
    
    
    public string Name => "Terrain Debug";
    public int Order => 3;
    public MenuType MenuType => MenuType.Debug;
    public unsafe void Draw()
    {
        var world = sigUtil.GetLayoutWorld();
        if (world == null || world->ActiveLayout == null) return;
        foreach (var terrain in world->ActiveLayout->Terrains)
        {
            UiUtil.Text($"Terrain: {terrain.Item1} - {(nint)terrain.Item2.Value:X8}", $"{(nint)terrain.Item2.Value:X8}");
            var terrainPtr = terrain.Item2.Value;
            if (terrainPtr == null) continue;
            UiUtil.Text($"GfxTerrain: {(nint)terrainPtr->GfxTerrain:X8}", $"{(nint)terrainPtr->GfxTerrain:X8}");
            if (terrainPtr->GfxTerrain == null) continue;

            using var tree = ImRaii.TreeNode($"Terrain Resource Handle: {(nint)terrainPtr->GfxTerrain->ResourceHandle:X8}");
            if (tree)
            {
                var terrainStruct = (Terrain*)terrainPtr->GfxTerrain;
                var terrainModels = terrainStruct->ModelResourceHandlesSpan;
                for (var i = 0; i < terrainStruct->ModelResourceHandleCount; i++)
                {
                    using var modelTree = ImRaii.TreeNode($"Model Resource Handle {i}: {(nint)terrainModels[i].Value:X8}");
                    if (modelTree)
                    {
                        var modelHandle = terrainModels[i].Value;
                        if (modelHandle == null) continue;

                        var fileName = modelHandle->FileName.ParseString();
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
