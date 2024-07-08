using Meddle.Utils.Files.Structs.Model;

namespace Meddle.Utils.Export;

public class ModelShape
{
    public unsafe ModelShape(Shape shape, string name, int lodIdx, ReadOnlySpan<ShapeMesh> shapeMeshes)
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
