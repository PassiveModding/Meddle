using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Meddle.Utils.Files.SqPack;

namespace Meddle.Plugin.UI.Windows;

public class MdlMaterialWindowManager
{
    private readonly WindowSystem windowSystem;
    private readonly SqPack pack;
    private readonly Dictionary<string, MdlMaterialWindow> materialWindows = new();

    
    public MdlMaterialWindowManager(WindowSystem windowSystem, SqPack pack)
    {
        this.windowSystem = windowSystem;
        this.pack = pack;
    }
        
    public unsafe void AddMaterialWindow(Pointer<ModelResourceHandle> model)
    {
        var id = $"{(nint)model.Value:X8}";
        if (materialWindows.ContainsKey(id)) return;
        var window = new MdlMaterialWindow(pack, this, model);
        materialWindows.Add(id, window);
        windowSystem.AddWindow(window);
        window.IsOpen = true;
    }
    
    public void RemoveMaterialWindow(MdlMaterialWindow window)
    {
        materialWindows.Remove(window.Id);
        windowSystem.RemoveWindow(window);
    }
}
