using Meddle.Formats.Files.MdlFile;

namespace Meddle.Utils.Export;

public class ModelShape
{
    public unsafe ModelShape(Shape shape, string name, int lodIdx, ReadOnlySpan<ShapeMesh> shapeMeshes)
    {
        var shapeMeshCount = shape.ShapeMeshCount[lodIdx];
        var meshesForShape = new List<ShapeMesh>(shapeMeshCount);
        var offset = shape.ShapeMeshStartIndex[lodIdx];
        for (var j = 0; j < shapeMeshCount; ++j)
        {
            // null when no mesh in this model starts at the shape mesh's offset (seen in modded hair)
            var shapeMesh = shapeMeshes[j + offset];
            if (shapeMesh != null)
            {
                meshesForShape.Add(shapeMesh);
            }
        }
        
        Name = name;
        Meshes = meshesForShape;
    }
    public string Name { get; }
    public IReadOnlyList<ShapeMesh> Meshes { get; }
}
