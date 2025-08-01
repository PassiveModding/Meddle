using Meddle.Utils.Files;

namespace Meddle.Utils.Export;

public class Mesh
{
    public string? Path { get; set; }
    public int MeshIdx { get; }
    public ushort MaterialIdx { get; }
    
    public IReadOnlyList<Vertex> Vertices { get; }
    public IReadOnlyList<ushort> Indices { get; }
    public IReadOnlyList<SubMesh> SubMeshes { get; }
    public IReadOnlyList<string>? BoneTable { get; }
    
    
    public Mesh(MdlFile mdlFile, int meshIdx, ReadOnlySpan<Vertex> vertices, uint meshIndexOffset, ReadOnlySpan<ushort> indices, (string name, short id)[] modelAttributes)
    {
        MeshIdx = meshIdx;
        var mesh = mdlFile.Meshes[meshIdx];
        MaterialIdx = mesh.MaterialIndex;
        Vertices = vertices.ToArray();
        Indices = indices.ToArray().ToList();
        
        var subMeshes = new List<SubMesh>();
        for (var i = 0; i < mesh.SubMeshCount; ++i)
        {
            var submeshIdx = mesh.SubMeshIndex + i;
            var sm = new SubMesh(mdlFile.Submeshes[submeshIdx], meshIndexOffset, modelAttributes);
            subMeshes.Add(sm);
        }
        
        SubMeshes = subMeshes;
        
        if (mesh.BoneTableIndex != 255)
        {
            var boneList = new List<string>();
            var boneTable = mdlFile.BoneTables[mesh.BoneTableIndex];
            var stringReader = new SpanBinaryReader(mdlFile.StringTable);
            for (var i = 0; i < boneTable.BoneCount; ++i)
            {
                var boneIdx = boneTable.BoneIndex[i];
                var offset = mdlFile.BoneNameOffsets[boneIdx];
                var name = stringReader.ReadString((int)offset);
                boneList.Add(name);
            }
            
            BoneTable = boneList;
        }
        
        foreach (var index in indices)
        {
            if (index >= vertices.Length)
                throw new ArgumentException($"Mesh {meshIdx} has index {index}, but only {vertices.Length} vertices exist");
        }

        if (indices.Length != mesh.IndexCount)
            throw new ArgumentException($"Mesh {meshIdx} has {indices.Length} indices, but {mesh.IndexCount} were expected");
    }
}
