using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using InteropGenerator.Runtime;

namespace Meddle.UI.InteropPlugin;

public class InteropService(ISigScanner sigScanner) : IDisposable
{
    /// <summary>
    /// Used to identify if ClientStructs have been resolved.
    /// </summary>
    public bool IsResolved { get; private set; }

    public void Initialize()
    {
        if (IsResolved)
            return;
        
        Resolver.GetInstance.Setup(sigScanner.SearchBase);
        Resolver.GetInstance.Resolve();
        IsResolved = true;
    }

    [Signature("40 53 48 83 EC 20 FF 81 ?? ?? ?? ?? 48 8B D9 48 8D 4C 24", DetourName = nameof(PostTickDetour))]
    private Hook<PostTickDelegate> postTickHook = null!;
    private delegate bool PostTickDelegate(nint a1);
    
    private bool PostTickDetour(nint a1)
    {
        var ret = postTickHook.Original(a1);
        return ret;
    }

    public void Dispose()
    {
        postTickHook?.Dispose();
    }
}
