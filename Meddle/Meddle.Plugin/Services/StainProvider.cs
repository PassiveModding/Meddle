using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class StainProvider : IDisposable, IService
{
    private readonly ILogger<PbdHooks> logger;
    public static IReadOnlyDictionary<uint, Stain> StainDict { get; private set; } = null!;
    private static Dictionary<uint, uint> HousingDictPriv = null!;
    public static IReadOnlyDictionary<uint, uint> HousingDict => HousingDictPriv;
    public static IReadOnlyDictionary<uint, Item> ItemDict { get; private set; } = null!;
    public static IReadOnlyDictionary<uint, HousingExterior> HousingExterior { get; private set; } = null!;
    
    public static Stain? GetStain(uint rowId)
    {
        return StainDict.TryGetValue(rowId, out var stain) ? stain : null;
    }

    public static Vector4 GetStainColor(Stain stain)
    {
        var mapped = UiUtil.SeColorToRgba(stain.Color);
        return ImGui.ColorConvertU32ToFloat4(mapped);
    }
    
    public StainProvider(ILogger<PbdHooks> logger, IDataManager dataManager, HookManager hookManager)
    {
        this.logger = logger;
        StainDict = dataManager.GetExcelSheet<Stain>().ToDictionary(row => row.RowId, row => row);
        var housingData = dataManager.GetExcelSheet<HousingUnitedExterior>();
        HousingExterior = dataManager.GetExcelSheet<HousingExterior>().ToDictionary(row => row.RowId, row => row);
        HousingDictPriv = new Dictionary<uint, uint>();
        foreach (var housingItem in housingData)
        {
            HousingDictPriv[housingItem.Roof.RowId] = housingItem.RowId;
            HousingDictPriv[housingItem.Walls.RowId] = housingItem.RowId;
            HousingDictPriv[housingItem.Windows.RowId] = housingItem.RowId;
            HousingDictPriv[housingItem.Door.RowId] = housingItem.RowId;
            HousingDictPriv[housingItem.Fence.RowId] = housingItem.RowId;
            HousingDictPriv[housingItem.OptionalRoof.RowId] = housingItem.RowId;
            HousingDictPriv[housingItem.OptionalWall.RowId] = housingItem.RowId;
            HousingDictPriv[housingItem.OptionalSignboard.RowId] = housingItem.RowId;
        }
        ItemDict = dataManager.GetExcelSheet<Item>()
                              .Where(item => item.AdditionalData.RowId != 0 && item.ItemSearchCategory.RowId is 65 or 66)
                              .ToDictionary(row => row.AdditionalData.RowId, row => row);
    }
    
    public void Dispose()
    {
        logger.LogDebug("Disposing StainHooks");
    }
}
