using Dalamud.Game;

namespace Meddle.Plugin.Services;

public class SigUtil : IService
{
    //public static ISigScanner? SigScanner { get; set; } = null!;
    private readonly ISigScanner sigScanner;

    public SigUtil(ISigScanner sigScanner)
    {
        this.sigScanner = sigScanner;
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
