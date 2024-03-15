namespace Meddle.Plugin.Models;

public class ShapeMesh(Mesh mesh, IReadOnlyList<(ushort BaseIndicesIndex, ushort ReplacingVertexIndex)> values)
{
    public Mesh Mesh { get; } = mesh;
    public IReadOnlyList<(ushort BaseIndicesIndex, ushort ReplacedVertexIndex)> Values { get; } = values;
}
