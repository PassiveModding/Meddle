using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;

namespace Meddle.Plugin.UI.Windows;

public class MdlMaterialWindowManager
{
    private readonly WindowSystem windowSystem;
    private readonly SqPack pack;
    private readonly Dictionary<string, MdlMaterialWindow> materialWindows = new();
    private readonly Dictionary<string, ShpkFile> shpkCache = new();
    public ShpkFile GetShpkFile(string path)
    {
        if (!shpkCache.TryGetValue(path, out var shpk))
        {
            var shpkData = pack.GetFileOrReadFromDisk(path);
            if (shpkData != null)
            {
                shpk = new ShpkFile(shpkData);
                shpkCache[path] = shpk;
            }
            else
            {
                throw new Exception($"Failed to load {path}");
            }
        }
        
        return shpk;
    }
    
    public MdlMaterialWindowManager(WindowSystem windowSystem, SqPack pack)
    {
        this.windowSystem = windowSystem;
        this.pack = pack;
    }
        
    public void AddMaterialWindow(Pointer<ModelResourceHandle> model)
    {
        var window = new MdlMaterialWindow(this, model);
        if (!materialWindows.TryAdd(window.Id, window)) return;
        windowSystem.AddWindow(window);
        window.IsOpen = true;
    }
    
    public void RemoveMaterialWindow(MdlMaterialWindow window)
    {
        materialWindows.Remove(window.Id);
        windowSystem.RemoveWindow(window);
    }
    
    public void AddMaterialWindow(Pointer<Model> model)
    {
        var window = new MdlMaterialWindow(this, model);
        if (!materialWindows.TryAdd(window.Id, window)) return;
        windowSystem.AddWindow(window);
        window.IsOpen = true;
    }
}
