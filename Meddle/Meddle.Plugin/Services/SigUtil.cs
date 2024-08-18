using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

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
