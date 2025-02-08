/*using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public unsafe class ResourceHooks : IDisposable, IService
{
    private readonly ILogger<ResourceHooks> logger;
    public const string GetResourceAsyncSig = "E8 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 83 C4 68";
    public const string GetResourceSyncSig = "E8 ?? ?? ?? ?? 48 8B D8 8B C7";
    
    private readonly Hook<GetResourceAsyncDelegate>? getResourceAsyncHook;
    private readonly Hook<GetResourceSyncDelegate>? getResourceSyncHook;
    private delegate nint GetResourceAsyncDelegate(ResourceManager* resourceManager, ResourceCategory* category, uint* type, uint* hash, byte* path, void* unknown, bool isUnknown);
    private delegate nint GetResourceSyncDelegate(ResourceManager* resourceManager, ResourceCategory* category, uint* type, uint* hash, byte* path, void* unknown);
    private readonly Dictionary<string, ResourceRequestInfo> resourceRequests = new();

    private class ResourceRequestInfo
    {
        public required string Path { get; set; }
        public int RequestCount { get; set; }
        public required uint Type { get; set; }
    }
    
    public ResourceHooks(ISigScanner sigScanner, IGameInteropProvider gameInterop, ILogger<ResourceHooks> logger)
    {
        this.logger = logger;
        
        if (sigScanner.TryScanText(GetResourceAsyncSig, out var getResourceAsyncPtr))
        {
            logger.LogDebug("Found GetResourceAsync at {ptr:X}", getResourceAsyncPtr);
            getResourceAsyncHook = gameInterop.HookFromAddress<GetResourceAsyncDelegate>(getResourceAsyncPtr, GetResourceAsyncDetour);
            getResourceAsyncHook.Enable();
        }
        
        if (sigScanner.TryScanText(GetResourceSyncSig, out var getResourceSyncPtr))
        {
            logger.LogDebug("Found GetResourceSync at {ptr:X}", getResourceSyncPtr);
            getResourceSyncHook = gameInterop.HookFromAddress<GetResourceSyncDelegate>(getResourceSyncPtr, GetResourceSyncDetour);
            getResourceSyncHook.Enable();
        }
    }

    
    public void Dispose()
    {
        getResourceAsyncHook?.Disable();
        getResourceSyncHook?.Disable();
        getResourceAsyncHook?.Dispose();
        getResourceSyncHook?.Dispose();
    }
    
    private nint GetResourceSyncDetour(ResourceManager* resourceManager, ResourceCategory* category, uint* type, uint* hash, byte* path, void* unknown)
    {
        var ret = getResourceSyncHook!.Original(resourceManager, category, type, hash, path, unknown);
        ProcessHook(path, type, ret, Source.Sync);
        return ret; 
    }

    private nint GetResourceAsyncDetour(ResourceManager* resourceManager, ResourceCategory* category, uint* type, uint* hash, byte* path, void* unknown, bool isUnknown)
    {
        var ret = getResourceAsyncHook!.Original(resourceManager, category, type, hash, path, unknown, isUnknown);
        ProcessHook(path, type, ret, Source.Async);
        return ret;    
    }

    private enum Source
    {
        Sync,
        Async
    }

    private void ProcessHook(byte* pathPtr, uint* type, nint resourceHandlePtr, Source source)
    {
        try
        {
            if ((nint)pathPtr == nint.Zero) return;
            var path = Marshal.PtrToStringAnsi((nint)pathPtr);
            if (string.IsNullOrEmpty(path)) return;
            if (!resourceRequests.TryGetValue(path, out var requestInfo))
            {
                requestInfo = new ResourceRequestInfo { Path = path, Type = 0 };
                var typeValueStr = Marshal.PtrToStringAnsi((nint)type);


                resourceRequests[path] = requestInfo;
                if (resourceHandlePtr != nint.Zero)
                {
                    var resourceHandle = (ResourceHandle*)resourceHandlePtr;
                    
                    logger.LogDebug("[{typeValueStr}][{source}] Loaded {path}", typeValueStr, source, path);
                }
                else
                {
                    logger.LogDebug("[{typeValueStr}][{source}] Requested {path}", typeValueStr, source, path);
                }
            }
            
            requestInfo.RequestCount++;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing hook");
        }
    }
}*/
