using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.Interop.Generated;
using InteropGenerator.Runtime;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class InteropService : IHostedService, IDisposable
{
    private readonly ILogger<InteropService> log;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ISigScanner sigScanner;
    private readonly PbdHooks pbdHooks;
    private readonly PluginState state;

    private bool disposed;

    // Client::System::Framework::Framework_Tick
    [Signature("40 53 48 83 EC 20 FF 81 ?? ?? ?? ?? 48 8B D9 48 8D 4C 24", DetourName = nameof(PostTickDetour))]
    private readonly Hook<PostTickDelegate> postTickHook = null!;

    public InteropService(
        ISigScanner sigScanner, PbdHooks pbdHooks, ILogger<InteropService> log, IDalamudPluginInterface pluginInterface, PluginState state)
    {
        this.sigScanner = sigScanner;
        this.pbdHooks = pbdHooks;
        this.log = log;
        this.pluginInterface = pluginInterface;
        this.state = state;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            log.LogInformation("Disposing InteropService");
            postTickHook?.Dispose();
            disposed = true;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (state.InteropResolved)
            return Task.CompletedTask;
        log.LogInformation("Resolving ClientStructs");
        Addresses.Register();

        var cacheFile =
            new FileInfo(Path.Combine(pluginInterface.ConfigDirectory.FullName, "Meddle.ClientStructs.cache"));

        Resolver.GetInstance.Setup(sigScanner.SearchBase, cacheFile: cacheFile);
        Resolver.GetInstance.Resolve();
        state.InteropResolved = true;
        log.LogInformation("Resolved ClientStructs");
        
        pbdHooks.Setup();
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private bool PostTickDetour(nint a1)
    {
        var ret = postTickHook.Original(a1);
        return ret;
    }

    private delegate bool PostTickDelegate(nint a1);
}
