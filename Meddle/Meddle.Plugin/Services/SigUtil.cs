using System.Numerics;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Microsoft.Extensions.Logging;
using Camera = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Camera;
using CameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager;

namespace Meddle.Plugin.Services;

public class SigUtil : IService, IDisposable
{
    //public static ISigScanner? SigScanner { get; set; } = null!;
    private readonly ISigScanner sigScanner;
    private readonly ILogger<SigUtil> logger;
    private readonly IGameInteropProvider gameInterop;

    public SigUtil(ISigScanner sigScanner, ILogger<SigUtil> logger, IGameInteropProvider gameInterop)
    {
        this.sigScanner = sigScanner;
        this.logger = logger;
        this.gameInterop = gameInterop;
    }
    
    public unsafe World* GetWorld()
    {
        var world = World.Instance();
        if (world == null)
            throw new Exception("World instance is null");
        return world;
    }
    
    public unsafe GameObjectManager* GetGameObjectManager()
    {
        var gameObjectManager = GameObjectManager.Instance();
        if (gameObjectManager == null)
            return null;
        
        return gameObjectManager;
    }
    
    public unsafe BattleChara* GetLocalPlayer()
    {
        var localPlayer = Control.GetLocalPlayer();
        return localPlayer;
    }
    
    public unsafe Vector3 GetLocalPosition()
    {
        var localPlayer = GetLocalPlayer();
        if (localPlayer == null) return Vector3.Zero;
        return localPlayer->Position;
    }
    
    public unsafe HousingManager* GetHousingManager()
    {
        var manager = HousingManager.Instance();
        if (manager == null)
            throw new Exception("Housing manager is null");
        return manager;
    }
    
    public unsafe LayoutWorld* GetLayoutWorld()
    {
        var layoutWorld = LayoutWorld.Instance();
        if (layoutWorld == null)
            throw new Exception("LayoutWorld instance is null");
        return layoutWorld;
    }
    
    public unsafe Camera* GetCamera()
    {
        var manager = CameraManager.Instance();
        if (manager == null)
            throw new Exception("Camera manager is null");
        if (manager->CurrentCamera == null)
            throw new Exception("Current camera is null");
        return manager->CurrentCamera;
    }
    
    public unsafe Device* GetDevice()
    {
        var device = Device.Instance();
        if (device == null)
            throw new Exception("Device instance is null");
        return device;
    }
    
    public unsafe Control* GetControl()
    {
        var control = Control.Instance();
        if (control == null)
            throw new Exception("Control instance is null");
        return control;
    }

    /*public const string CleanupRenderSig = "48 8B D1 45 33 C9 48 8B 49";
    private Hook<CleanupRenderDelegate>? cleanupRenderHook;
    private delegate nint CleanupRenderDelegate(nint a1);

    private void SetupCleanupRender()
    {
        if (sigScanner.TryScanText(CleanupRenderSig, out var ptr))
        {
            logger.LogDebug("Found Object::CleanupRender at {ptr:X}", ptr);
            cleanupRenderHook =
                gameInterop.HookFromAddress<CleanupRenderDelegate>(
                    ptr, CleanupRenderDetour);
            cleanupRenderHook.Enable();
        }
        else
        {
            logger.LogError("Failed to find Human::CreateDeformer, will not be able to cache deformer data");
        }
    }
    
    private nint CleanupRenderDetour(nint a1)
    {
        logger.LogDebug("CleanupRenderDetour on {a1:X8}", a1);
        return cleanupRenderHook!.Original(a1);
    }*/
    
    public void Dispose()
    {
        logger.LogDebug("Disposing SigUtil");
        //cleanupRenderHook?.Dispose();
    }
    
    public unsafe void* TryGetStaticAddressFromSig(string sig, int offset)
    {
        if (sigScanner == null)
            throw new Exception("SigScanner not set");

        if (sigScanner.TryGetStaticAddressFromSig(sig, out var ptr, offset))
        {
            if (ptr != IntPtr.Zero)
                return (void*)ptr;
        }

        throw new Exception($"Failed to find signature {sig} at offset {offset}");
    }
}
