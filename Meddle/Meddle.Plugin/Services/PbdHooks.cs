using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Meddle.Plugin.Models.Structs;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class PbdHooks : IDisposable
{
    public const string HumanCreateDeformerSig = "40 53 48 83 EC 20 4C 8B C1 83 FA 0D";
    private readonly Dictionary<nint, Dictionary<uint, DeformerCachedStruct>> deformerCache = new();
    public IReadOnlyDictionary<nint, Dictionary<uint, DeformerCachedStruct>> DeformerCache => deformerCache;
    private readonly IGameInteropProvider gameInterop;
    private readonly ILogger<PbdHooks> logger;
    private readonly ISigScanner sigScanner;
    private Hook<HumanCreateDeformerDelegate>? humanCreateDeformerHook;

    public PbdHooks(ISigScanner sigScanner, IGameInteropProvider gameInterop, ILogger<PbdHooks> logger)
    {
        this.sigScanner = sigScanner;
        this.gameInterop = gameInterop;
        this.logger = logger;
        Setup();
    }

    public void Dispose()
    {
        logger.LogDebug("Disposing PbdHooks");
        humanCreateDeformerHook?.Dispose();
        deformerCache.Clear();
    }

    public void Setup()
    {
        if (sigScanner.TryScanText(HumanCreateDeformerSig, out var humanCreateDeformerPtr))
        {
            logger.LogDebug("Found Human::CreateDeformer at {ptr:X}", humanCreateDeformerPtr);
            humanCreateDeformerHook =
                gameInterop.HookFromAddress<HumanCreateDeformerDelegate>(
                    humanCreateDeformerPtr, Human_CreateDeformerDetour);
            humanCreateDeformerHook.Enable();
        }
        else
        {
            throw new Exception("Failed to hook into Human::CreateDeformer");
        }
    }

    public DeformerCachedStruct? TryGetDeformer(nint humanPtr, uint slot)
    {
        if (!deformerCache.TryGetValue(humanPtr, out var slotCache))
            return null;
        if (!slotCache.TryGetValue(slot, out var deformer))
            return null;
        return deformer;
    }

    private unsafe nint Human_CreateDeformerDetour(nint humanPtr, uint slot)
    {
        var result = humanCreateDeformerHook!.Original(humanPtr, slot);

        var deformer = (DeformerStruct*)result;
        if (deformer != null && deformer->PbdPointer != null)
        {
            if (!deformerCache.TryGetValue(humanPtr, out var slotCache))
            {
                slotCache = new Dictionary<uint, DeformerCachedStruct>();
                deformerCache[humanPtr] = slotCache;
            }

            slotCache[slot] = new DeformerCachedStruct
            {
                DeformerId = deformer->DeformerId,
                RaceSexId = deformer->RaceSexId,
                PbdPath = deformer->PbdPointer->FileName.ToString()
            };
        }
        else
        {
            if (deformerCache.TryGetValue(humanPtr, out var slotCache))
            {
                slotCache.Remove(slot);
                if (slotCache.Count == 0)
                    deformerCache.Remove(humanPtr);
            }
        }

        return result;
    }

    private delegate nint HumanCreateDeformerDelegate(nint humanPtr, uint slot);
}
