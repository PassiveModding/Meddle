using System.Text.Json.Serialization;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models;

public unsafe class Mesh
{
    public int MeshIdx { get; }
    public ushort MaterialIdx { get; }
    
    [JsonIgnore]
    public IReadOnlyList<Vertex> Vertices { get; }
    [JsonIgnore]
    public IReadOnlyList<ushort> Indices { get; }
    public IReadOnlyList<SubMesh> SubMeshes { get; }
    public IReadOnlyList<string>? BoneTable { get; }

    public Mesh(Pointer<ModelResourceHandle> hnd, int meshIdx, Vertex[] vertices, uint meshIndexOffset, ReadOnlySpan<ushort> indices) : 
        this(hnd.Value, meshIdx, vertices, meshIndexOffset, indices)
    {
    }
    
    public Mesh(ModelResourceHandle* hnd, int meshIdx, Vertex[] vertices, uint meshIndexOffset, ReadOnlySpan<ushort> indices)
    {
        var mesh = &hnd->Meshes[meshIdx];

        MeshIdx = meshIdx;
        MaterialIdx = mesh->MaterialIndex;

        Vertices = vertices.ToList();
        Indices = indices.ToArray().ToList();

        var subMeshes = new List<SubMesh>();
        for (var i = 0; i < mesh->SubMeshCount; ++i)
        {
            var submeshIdx = mesh->SubMeshIndex + i;
            var sm = new SubMesh(hnd, submeshIdx);
            // sm.IndexOffset is relative to the model, not the mesh
            sm.IndexOffset -= meshIndexOffset;
            subMeshes.Add(sm);
        }
        
        SubMeshes = subMeshes;

        if (mesh->BoneTableIndex != 255)
        {
            var boneList = new List<string>();
            var boneTable = &hnd->BoneTables[mesh->BoneTableIndex];
            for (var i = 0; i < boneTable->BoneCount; ++i)
            {
                var namePtr = hnd->StringTable + 8 + hnd->BoneNameOffsets[boneTable->BoneIndex[i]];
                boneList.Add(MemoryHelper.ReadStringNullTerminated((nint)namePtr));
            }
            
            BoneTable = boneList;
        }

        foreach (var index in indices)
        {
            if (index >= vertices.Length)
                throw new ArgumentException($"Mesh {meshIdx} has index {index}, but only {vertices.Length} vertices exist");
        }

        if (indices.Length != mesh->IndexCount)
            throw new ArgumentException($"Mesh {meshIdx} has {indices.Length} indices, but {mesh->IndexCount} were expected");
    }
}
