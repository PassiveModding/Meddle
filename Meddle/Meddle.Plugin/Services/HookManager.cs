using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class HookManager : IService, IDisposable
{
    private readonly List<IDalamudHook> hooks = [];
    private readonly ILogger<HookManager> logger;
    private readonly ISigScanner sigScanner;
    private readonly IGameInteropProvider gameInterop;

    public HookManager(ILogger<HookManager> logger, ISigScanner sigScanner, IGameInteropProvider gameInterop)
    {
        this.logger = logger;
        this.sigScanner = sigScanner;
        this.gameInterop = gameInterop;
    }

    internal Hook<TDelegate> CreateHook<TDelegate>(string signature, TDelegate detour) where TDelegate : Delegate
    {
        var delegateName = typeof(TDelegate).Name;
        if (sigScanner.TryScanText(signature, out var ptr))
        {
            logger.LogDebug("Found {delegateName} {signature} at {ptr:X}", delegateName, signature, ptr);
            var hook = gameInterop.HookFromAddress(ptr, detour);
            hooks.Add(hook);
            return hook;
        }
        logger.LogError("Failed to find {delegateName} {signature}", delegateName, signature);
        return null!;
    }

    public void Dispose()
    {
        foreach (var hook in hooks)
        {
            hook.Dispose();
        }
    }
}
