using Lumina.Models.Models;

namespace Meddle.Xande.Utility;

public static class ModelExtensions {
    public static string[]? BoneTable( this Mesh mesh ) {
        var rawMesh = mesh.Parent.File!.Meshes[mesh.MeshIndex];
        if( rawMesh.BoneTableIndex == 255 ) { return null; }

        var rawTable = mesh.Parent.File!.BoneTables[rawMesh.BoneTableIndex];
        return rawTable.BoneIndex.Take( rawTable.BoneCount ).Select( b => mesh.Parent.StringOffsetToStringMap[( int )mesh.Parent.File!.BoneNameOffsets[b]] ).ToArray();
    }

    public static Vertex VertexByIndex( this Mesh mesh, int index ) => mesh.Vertices[mesh.Indices[index]];
}