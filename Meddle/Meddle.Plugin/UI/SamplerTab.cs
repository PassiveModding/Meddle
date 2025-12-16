using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;

namespace Meddle.Plugin.UI;

public class SamplerTab : ITab
{
    private readonly SqPack pack;
    private readonly IDataManager dataManager;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    public string Name => "Sampler";
    public int Order => 5;
    public MenuType MenuType => MenuType.Debug;

    public SamplerTab(SqPack pack,
    IDataManager dataManager,
    TextureCache textureCache,
    ITextureProvider textureProvider)
    {
        this.pack = pack;
        this.dataManager = dataManager;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
    }
    
    private Dictionary<string, ShaderPackage> loadedShpkFiles = new();
    
    public unsafe void Draw()
    {
        ImGui.Text("Sampler Tab");
        var manager = Manager.Instance();
        UiUtil.Text($"Manager Ptr: 0x{(nint)manager:X}", $"0x{(nint)manager:X}");
        var shaderPackages = manager->ShaderManager.ShaderPackageResourceHandles;
        if (ImGui.CollapsingHeader("Loaded Shader Packages"))
        {
            foreach (var shpkPtr in shaderPackages)
            {
                if (shpkPtr == null || shpkPtr.Value == null)
                    continue;
                var shpk = shpkPtr.Value;
                if (ImGui.CollapsingHeader($"{shpk->FileName.ParseString()}"))
                {
                    UiUtil.Text($"Handle: 0x{(nint)shpk:X}", $"0x{(nint)shpk:X}");
                    UiUtil.Text($"Ref Count: {shpk->RefCount}", $"{shpk->RefCount}");
                    if (!loadedShpkFiles.TryGetValue(shpk->FileName.ParseString(), out var shaderPackage))
                    {
                        var shpkData = pack.GetFile(shpk->FileName.ParseString());
                        if (shpkData != null)
                        {
                            var shpkFile = new ShpkFile(shpkData.Value.file.RawData);
                            loadedShpkFiles[shpk->FileName.ParseString()] = new ShaderPackage(shpkFile, shpk->FileName.ParseString());
                        }
                    }

                    if (shaderPackage != null)
                    {
                        foreach (var texture in shaderPackage.Textures.Values)
                        {
                            UiUtil.Text($"Texture Slot: {texture}", $"{texture}");
                        }
                    }
                }
            }
        }

        var renderTargetManager = RenderTargetManager.Instance();
        UiUtil.Text($"RenderTargetManager Ptr: 0x{(nint)renderTargetManager:X}", $"0x{(nint)renderTargetManager:X}");
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }
}
