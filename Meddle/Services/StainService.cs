using System.Numerics;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Penumbra.GameData.Files;
using Penumbra.GameData.Structs;

namespace Meddle.Plugin.Services;

public class StainService : IService
{
    public StainService(IDataManager gameData)
    {
        Stains = CreateStainData(gameData);
    }

    public IReadOnlyDictionary<byte, (string Name, uint Dye, bool Gloss)> Stains { get; }

    private IReadOnlyDictionary<byte, (string Name, uint Dye, bool Gloss)> CreateStainData(IDataManager dataManager)
    {
        var stainSheet = dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.Stain>(dataManager.Language)!;
        
        return stainSheet.Where(s => s.Color != 0 && s.Name.RawData.Length > 0)
            .ToDictionary(s => (byte)s.RowId, s =>
            {
                var stain = new Stain(s);
                return (stain.Name, stain.RgbaColor, stain.Gloss);
            });
    }

    public (string name, Vector4 color, bool gloss) GetStain(byte id)
    {
        if (Stains.TryGetValue(id, out var stain))
        {
            return (stain.Name, ColorHelpers.RgbaUintToVector4(stain.Dye), stain.Gloss);
        }

        return ("None", Vector4.Zero, false);
    }
}