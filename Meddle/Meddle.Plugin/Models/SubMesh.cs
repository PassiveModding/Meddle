using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Lumina.Models.Models;

namespace Meddle.Plugin.Models;

public unsafe class SubMesh
{
    public uint IndexOffset { get; set; }
    public uint IndexCount { get; set; }
    public List<string> Attributes { get; set; }

    public SubMesh(Submesh mesh)
    {
        IndexOffset = mesh.IndexOffset;
        IndexCount = mesh.IndexNum;
        Attributes = new();
        foreach (var attribute in mesh.Attributes)
            Attributes.Add(attribute);
    }

    public SubMesh(ModelResourceHandle* handle, int idx)
    {
        var submesh = &handle->Submeshes[idx];
        IndexOffset = submesh->IndexOffset;
        IndexCount = submesh->IndexCount;

        Attributes = new();
        foreach (var (namePtr, id) in handle->Attributes)
        {
            if ((submesh->AttributeIndexMask & (1u << id)) != 0)
                Attributes.Add(MemoryHelper.ReadStringNullTerminated((nint)namePtr.Value));
        }
    }
}
