using System.Text.Json.Serialization;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace Meddle.Plugin.Models;

public unsafe class Mesh
{
    public int MeshIdx { get; set; }
    public ushort MaterialIdx { get; set; }
    //[JsonIgnore]
    public List<Vertex> Vertices { get; set; }
    [JsonIgnore]
    public List<ushort> Indices { get; set; }
    public List<SubMesh> Submeshes { get; set; }
    public List<string>? BoneTable { get; set; }

    public Mesh(Lumina.Models.Models.Mesh mesh)
    {
        MeshIdx = mesh.MeshIndex;
        MaterialIdx = mesh.Parent.File!.Meshes[mesh.MeshIndex].MaterialIndex;
        Vertices = new();
        foreach (var vertex in mesh.Vertices)
            Vertices.Add(new(vertex));

        Indices = new();
        foreach (var index in mesh.Indices)
            Indices.Add(index);

        Submeshes = new();
        foreach (var submesh in mesh.Submeshes)
            Submeshes.Add(new(submesh));

        // meshes don't have attributes. lumina is lying to you.

        // Copy over the bone table
        var currentMesh = mesh.Parent.File.Meshes[ MeshIdx ];
        int boneTableIndex = currentMesh.BoneTableIndex;
        if( boneTableIndex != 255 )
        {
            var boneTable = mesh.Parent.File.BoneTables[boneTableIndex];
            var table = boneTable.BoneIndex.Take(boneTable.BoneCount);
            BoneTable = table.Select( b => mesh.Parent.StringOffsetToStringMap[(int)mesh.Parent.File.BoneNameOffsets[b]] ).ToList();
        }
    }

    public Mesh(ModelResourceHandle* hnd, int meshIdx, Vertex[] vertices, uint meshIndexOffset, ReadOnlySpan<ushort> indices)
    {
        var mesh = &hnd->Meshes[meshIdx];

        MeshIdx = meshIdx;
        MaterialIdx = mesh->MaterialIndex;

        Vertices = vertices.ToList();
        Indices = indices.ToArray().ToList();

        Submeshes = new();

        for (var i = 0; i < mesh->SubMeshCount; ++i)
        {
            var submeshIdx = mesh->SubMeshIndex + i;
            var sm = new SubMesh(hnd, submeshIdx);
            // sm.IndexOffset is relative to the model, not the mesh
            sm.IndexOffset -= meshIndexOffset;
            Submeshes.Add(sm);
        }

        if (mesh->BoneTableIndex != 255)
        {
            BoneTable = new();
            var boneTable = &hnd->BoneTables[mesh->BoneTableIndex];
            for (var i = 0; i < boneTable->BoneCount; ++i)
            {
                var namePtr = hnd->StringTable + 8 + hnd->BoneNameOffsets[boneTable->BoneIndex[i]];
                BoneTable.Add(MemoryHelper.ReadStringNullTerminated((nint)namePtr));
            }
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
