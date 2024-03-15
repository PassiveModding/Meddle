using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models;

public unsafe class SubMesh
{
    public uint IndexOffset { get; set; }
    public uint IndexCount { get; set; }
    public IReadOnlyList<string> Attributes { get; set; }
    
    public SubMesh(Pointer<ModelResourceHandle> handle, int idx) : this(handle.Value, idx)
    {
    }
    
    public SubMesh(ModelResourceHandle* handle, int idx)
    {
        var subMesh = &handle->Submeshes[idx];
        IndexOffset = subMesh->IndexOffset;
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
