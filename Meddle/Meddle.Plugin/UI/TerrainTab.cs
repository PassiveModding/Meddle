using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public class TerrainTab : ITab
{
    private readonly LayoutService layoutService;
    private readonly ILogger<TerrainTab> logger;

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name => "Terrain";
    public int Order => 0;
    public MenuType MenuType => MenuType.Debug;
    
    public TerrainTab(LayoutService layoutService, ILogger<TerrainTab> logger)
    {
        this.layoutService = layoutService;
        this.logger = logger;
        cts = new CancellationTokenSource();
    }
    
    
    CancellationTokenSource cts;
    Task task = Task.CompletedTask;
    
    public unsafe void Draw()
    {
        if (ImGui.Button("Dump Terrain"))
        {
            task = Task.Run(() =>
            {
                try
                {
                    layoutService.ParseTerrain(cts.Token);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to parse terrain");
                    throw;
                }
            });
        }

        if (task.IsFaulted)
        {
            var ex = task.Exception;
            ImGui.TextWrapped($"Error: {ex}");
        }
        
        if (task.IsCompleted)
        {
            cts = new CancellationTokenSource();
        }
        
        if (ImGui.Button($"Cancel"))
        {
            cts.Cancel();
            cts = new CancellationTokenSource();
        }
    }
}
