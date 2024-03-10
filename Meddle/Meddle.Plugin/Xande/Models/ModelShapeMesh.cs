using Lumina.Models.Models;

namespace Meddle.Plugin.Xande.Models;

public unsafe class ModelShapeMesh
{
    public Mesh AssociatedMesh { get; set; }
    public List<(ushort BaseIndicesIndex, ushort ReplacingVertexIndex)> Values { get; set; }

    public ModelShapeMesh(ShapeMesh shapeMesh)
    {
        // TODO: associate mesh by id or by actual reference!
        AssociatedMesh = new(shapeMesh.AssociatedMesh);
        Values = new();
        foreach (var value in shapeMesh.Values)
            Values.Add(value);
    }
}
