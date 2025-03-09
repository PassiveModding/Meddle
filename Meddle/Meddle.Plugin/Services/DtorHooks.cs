// using Dalamud.Game;
// using Dalamud.Hooking;
// using Dalamud.Plugin.Services;
// using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
// using Meddle.Plugin.Utils;
// using Microsoft.Extensions.Logging;
//
// namespace Meddle.Plugin.Services;
//
// public class DtorHooks : IDisposable, IService
// {
//     private readonly string materialResourceHandleDtorSig = 
//         "48 89 5C 24 ?? 57 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 8B D9 48 89 01 8B FA 48 8B 89 ?? ?? ?? ?? " +
//         "48 85 C9 74 22 48 8B 01 FF 50 18 48 8B 93 ?? ?? ?? ?? 48 8B C8 4C 8B 00 41 FF 50 18 48 C7 83 ?? " +
//         "?? ?? ?? ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 40 F6 C7 01 74 0D BA ?? ?? ?? ?? 48 8B CB E8 ?? ?? " +
//         "?? ?? 48 8B C3 48 8B 5C 24 ?? 48 83 C4 20 5F C3 40 53 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 8B D9 " +
//         "48 89 01 F6 C2 01 74 0A BA ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B C3 48 83 C4 20 5B C3 CC CC CC CC CC " +
//         "48 89 5C 24 ?? 57 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 8B D9 48 89 01 8B FA 48 8B 89 ?? ?? ?? ?? " +
//         "48 85 C9 74 05 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9 74 05 E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ??";
//
//     private readonly string modelResourceHandleDtorSig =
//         "48 89 5C 24 ?? 57 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 8B D9 48 89 01 8B FA 48 8B 89 ?? ?? ?? ?? " +
//         "48 85 C9 74 05 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9 74 05 E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ??";
//     
//     private readonly ILogger<DtorHooks> logger;
//     private readonly Hook<MaterialResourceHandleDtorDelegate>? materialResourceHandleDtorHook;
//     private readonly Hook<ModelResourceHandleDtorDelegate>? modelResourceHandleDtorHook;
//     public event EventHandler<MaterialResourceHandleDtorEventArgs>? OnMaterialResourceHandleDtor;
//     public event EventHandler<ModelResourceHandleDtorEventArgs>? OnModelResourceHandleDtor;
//     
//     public DtorHooks(ISigScanner sigScanner, IGameInteropProvider gameInterop, ILogger<DtorHooks> logger)
//     {
//         this.logger = logger;
//         if (sigScanner.TryScanText(materialResourceHandleDtorSig, out var mtrlResourceHandleDtorPtr))
//         {
//             logger.LogDebug("Found MaterialResourceHandle::Dtor at {ptr:X}", mtrlResourceHandleDtorPtr);
//             materialResourceHandleDtorHook = gameInterop.HookFromAddress<MaterialResourceHandleDtorDelegate>(mtrlResourceHandleDtorPtr, MaterialResourceHandle_Dtor_Detour);
//             materialResourceHandleDtorHook.Enable();
//         }
//         else
//         {
//             logger.LogError("Failed to find MaterialResourceHandle::Dtor");
//         }
//         
//         if (sigScanner.TryScanText(modelResourceHandleDtorSig, out var mdlResourceHandleDtorPtr))
//         {
//             logger.LogDebug("Found ModelResourceHandle::Dtor at {ptr:X}", mdlResourceHandleDtorPtr);
//             modelResourceHandleDtorHook = gameInterop.HookFromAddress<ModelResourceHandleDtorDelegate>(mdlResourceHandleDtorPtr, ModelResourceHandle_Dtor_Detour);
//             modelResourceHandleDtorHook.Enable();
//         }
//         else
//         {
//             logger.LogError("Failed to find ModelResourceHandle::Dtor");
//         }
//     }
//     
//     private delegate nint MaterialResourceHandleDtorDelegate(nint materialResourceHandle, char a2);
//     private delegate nint ModelResourceHandleDtorDelegate(nint modelResourceHandle, char a2);
//     
//     public class MaterialResourceHandleDtorEventArgs : EventArgs
//     {
//         public nint MaterialResourceHandle { get; }
//         public char A2 { get; }
//         public MaterialResourceHandleDtorEventArgs(nint materialResourceHandle, char a2)
//         {
//             MaterialResourceHandle = materialResourceHandle;
//             A2 = a2;
//         }
//     }
//     
//     public class ModelResourceHandleDtorEventArgs : EventArgs
//     {
//         public nint ModelResourceHandle { get; }
//         public char A2 { get; }
//         public ModelResourceHandleDtorEventArgs(nint modelResourceHandle, char a2)
//         {
//             ModelResourceHandle = modelResourceHandle;
//             A2 = a2;
//         }
//     }
//     
//     private unsafe nint MaterialResourceHandle_Dtor_Detour(nint materialResourceHandle, char a2)
//     {
//         var mtrlResourceHandle = (MaterialResourceHandle*)materialResourceHandle;
//         var path = mtrlResourceHandle->ResourceHandle.FileName.ParseString();
//         logger.LogDebug("MaterialResourceHandle_Dtor_Detour: {path}", path);
//         var result = materialResourceHandleDtorHook!.Original(materialResourceHandle, a2);
//         OnMaterialResourceHandleDtor?.Invoke(this, new MaterialResourceHandleDtorEventArgs(materialResourceHandle, a2));
//         return result;
//     }
//     
//     private unsafe nint ModelResourceHandle_Dtor_Detour(nint modelResourceHandle, char a2)
//     {
//         var mdlResourceHandle = (ModelResourceHandle*)modelResourceHandle;
//         var path = mdlResourceHandle->ResourceHandle.FileName.ParseString();
//         logger.LogDebug("ModelResourceHandle_Dtor_Detour: {path}", path);
//         var result = modelResourceHandleDtorHook!.Original(modelResourceHandle, a2);
//         OnModelResourceHandleDtor?.Invoke(this, new ModelResourceHandleDtorEventArgs(modelResourceHandle, a2));
//         return result;
//     }
//     
//     public void Dispose()
//     {
//         logger.LogDebug("Disposing DtorHooks");
//         materialResourceHandleDtorHook?.Dispose();
//         modelResourceHandleDtorHook?.Dispose();
//     }
// }
