using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Lumina.Excel.Sheets;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public unsafe class StainHooks : IDisposable, IService
{
    private readonly ILogger<PbdHooks> logger;
    // private const string HumanGetDyeForSlotSignature = "83 FA 09 77 16 8B D2";
    // private readonly Hook<HumanGetDyeForSlotDelegate>? humanGetDyeForSlotHook;
    // private delegate uint HumanGetDyeForSlotDelegate(Human* a1, uint a2, uint a3);
    // private const string DemiHumanGetDyeForSlotSignature = "44 8B CA 49 C1 E1 05 4C 03 89 ?? ?? ?? ??";
    // private readonly Hook<DemiHumanGetDyeForSlotDelegate>? demiHumanGetDyeForSlotHook;
    // private delegate uint DemiHumanGetDyeForSlotDelegate(CharacterBase* a1, uint a2, uint a3);
    // private const string WeaponGetDyeForSlotSignature = "48 8B 81 ?? ?? ?? ?? 41 8B D0 0F B6 44 10 ??";
    // private readonly Hook<WeaponGetDyeForSlotDelegate>? weaponGetDyeForSlotHook;
    // private delegate uint WeaponGetDyeForSlotDelegate(Weapon* a1, uint a2, uint a3);
    // private readonly Dictionary<(nint, uint, uint), uint> dyeCache = new();
    private readonly Dictionary<uint, Stain> stainDict;
    private readonly Dictionary<uint, uint> housingDict;
    private readonly Dictionary<uint, Item> itemDict;
    private readonly Dictionary<uint, HousingExterior> housingExterior;
    public IReadOnlyDictionary<uint, Stain> StainDict => stainDict;
    public IReadOnlyDictionary<uint, uint> HousingDict => housingDict;
    public IReadOnlyDictionary<uint, Item> ItemDict => itemDict;
    public IReadOnlyDictionary<uint, HousingExterior> HousingExterior => housingExterior;

    // public uint? GetDyeFromCache(nint obj, uint slotIdx, uint dyeChannel)
    // {
    //     uint? result = dyeCache.TryGetValue((obj, slotIdx, dyeChannel), out var dye) ? dye : null;
    //     return result;
    // }
    //
    // public (Stain Stain, Vector4 Color)? GetStainFromCache(nint obj, uint slotIdx, uint dyeChannel)
    // {
    //     uint? result = dyeCache.TryGetValue((obj, slotIdx, dyeChannel), out var dye) ? dye : null;
    //     if (result == null) return null;
    //     var stain = GetStain(result.Value);
    //     if (stain == null) return null;
    //     var color = GetStainColor(stain.Value);
    //     return (stain.Value, color);
    // }
    
    public Stain? GetStain(uint rowId)
    {
        return stainDict.TryGetValue(rowId, out var stain) ? stain : null;
    }

    public static Vector4 GetStainColor(Stain stain)
    {
        var mapped = UiUtil.SeColorToRgba(stain.Color);
        return ImGui.ColorConvertU32ToFloat4(mapped);
    }
    
    public StainHooks(ILogger<PbdHooks> logger, IDataManager dataManager, HookManager hookManager)
    {
        this.logger = logger;
        stainDict = dataManager.GetExcelSheet<Stain>().ToDictionary(row => row.RowId, row => row);
        var housingData = dataManager.GetExcelSheet<HousingUnitedExterior>();
        housingExterior = dataManager.GetExcelSheet<HousingExterior>().ToDictionary(row => row.RowId, row => row);
        housingDict = new Dictionary<uint, uint>();
        foreach (var housingItem in housingData)
        {
            housingDict[housingItem.Roof.RowId] = housingItem.RowId;
            housingDict[housingItem.Walls.RowId] = housingItem.RowId;
            housingDict[housingItem.Windows.RowId] = housingItem.RowId;
            housingDict[housingItem.Door.RowId] = housingItem.RowId;
            housingDict[housingItem.Fence.RowId] = housingItem.RowId;
            housingDict[housingItem.OptionalRoof.RowId] = housingItem.RowId;
            housingDict[housingItem.OptionalWall.RowId] = housingItem.RowId;
            housingDict[housingItem.OptionalSignboard.RowId] = housingItem.RowId;
        }
        itemDict = dataManager.GetExcelSheet<Item>()
                              .Where(item => item.AdditionalData.RowId != 0 && item.ItemSearchCategory.RowId is 65 or 66)
                              .ToDictionary(row => row.AdditionalData.RowId, row => row);

        // humanGetDyeForSlotHook = hookManager.CreateHook<HumanGetDyeForSlotDelegate>(HumanGetDyeForSlotSignature, Human_GetDyeForSlotDetour);
        // humanGetDyeForSlotHook?.Enable();
        //
        // demiHumanGetDyeForSlotHook = hookManager.CreateHook<DemiHumanGetDyeForSlotDelegate>(DemiHumanGetDyeForSlotSignature, DemiHuman_GetDyeForSlotDetour);
        // demiHumanGetDyeForSlotHook?.Enable();
        //
        // weaponGetDyeForSlotHook = hookManager.CreateHook<WeaponGetDyeForSlotDelegate>(WeaponGetDyeForSlotSignature, Weapon_GetDyeForSlotDetour);
        // weaponGetDyeForSlotHook?.Enable();
    }
    
    // private uint Weapon_GetDyeForSlotDetour(Weapon* a1, uint slotIdx, uint dyeChannel)
    // {
    //     var result = weaponGetDyeForSlotHook!.Original(a1, slotIdx, dyeChannel);
    //     dyeCache[((nint)a1, slotIdx, dyeChannel)] = result;
    //     return result;
    // }
    //
    // private uint Human_GetDyeForSlotDetour(Human* a1, uint slotIdx, uint dyeChannel)
    // {
    //     var result = humanGetDyeForSlotHook!.Original(a1, slotIdx, dyeChannel);
    //     dyeCache[((nint)a1, slotIdx, dyeChannel)] = result;
    //     return result;
    // }
    //
    // private uint DemiHuman_GetDyeForSlotDetour(CharacterBase* a1, uint slotIdx, uint dyeChannel)
    // {
    //     var result = demiHumanGetDyeForSlotHook!.Original(a1, slotIdx, dyeChannel);
    //     dyeCache[((nint)a1, slotIdx, dyeChannel)] = result;
    //     return result;
    // }
    
    public void Dispose()
    {
        logger.LogDebug("Disposing StainHooks");
    }
}
