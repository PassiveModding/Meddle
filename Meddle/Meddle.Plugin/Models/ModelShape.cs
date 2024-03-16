using Dalamud.Memory;
using CSShape = FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.ModelResourceHandle.Shape;

namespace Meddle.Plugin.Models;

public class ModelShape
{
    public unsafe ModelShape(CSShape shape, string name, int lodIdx, IReadOnlyList<ShapeMesh> shapeMeshes)
    {
        var shapeMeshCount = shape.ShapeMeshCount[lodIdx];
        var meshesForShape = new ShapeMesh[shapeMeshCount];
        var offset = shape.ShapeMeshStartIndex[lodIdx];
        for (var j = 0; j < shapeMeshCount; ++j)
        {
            meshesForShape[j] = shapeMeshes[j + offset];
        }
        
        Name = name;
        Meshes = meshesForShape;
    }
    public string Name { get; }
    public IReadOnlyList<ShapeMesh> Meshes { get; }
}
