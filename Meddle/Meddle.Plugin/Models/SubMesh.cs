using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models;

public class SubMesh
{
    public uint IndexOffset { get; }
    public uint IndexCount { get; }
    public IReadOnlyList<string> Attributes { get; }
    
    public unsafe SubMesh(Pointer<ModelResourceHandle> handle, int idx, uint meshIndexOffset) : 
        this(handle.Value, idx, meshIndexOffset)
    {
    }
    
    public unsafe SubMesh(ModelResourceHandle* handle, int idx, uint meshIndexOffset)
    {
        var subMesh = &handle->Submeshes[idx];
        // IndexOffset is relative to the model, not the mesh so we need to adjust it
        IndexOffset = subMesh->IndexOffset - meshIndexOffset;
        IndexCount = subMesh->IndexCount;

        var attributes = new List<string>();
        foreach (var (namePtr, id) in handle->Attributes)
        {
            if ((subMesh->AttributeIndexMask & (1u << id)) != 0)
                attributes.Add(MemoryHelper.ReadStringNullTerminated((nint)namePtr.Value));
        }
        
        Attributes = attributes;
    }
}
