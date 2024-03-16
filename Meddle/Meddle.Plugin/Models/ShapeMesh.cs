using FFXIVClientStructs.Interop;
using CSShapeMesh = FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.ModelResourceHandle.ShapeMesh;
using CSShapeValue = FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.ModelResourceHandle.ShapeValue;

namespace Meddle.Plugin.Models;

public class ShapeMesh
{
    public unsafe ShapeMesh(Pointer<CSShapeValue> shapeValues, CSShapeMesh shapeMesh, Mesh mesh, int i) : 
        this(shapeValues.Value, shapeMesh, mesh, i)
    {
    }
    
    public unsafe ShapeMesh(CSShapeValue* shapeValues, CSShapeMesh shapeMesh, Mesh mesh, int i)
    {
        var values = new List<(ushort BaseIndicesIndex, ushort ReplacingVertexIndex)>();
        var range = Enumerable.Range((int)shapeMesh.ShapeValueOffset, (int)shapeMesh.ShapeValueCount);
        foreach (var idx in range)
        {
            var baseIndicesIndex = shapeValues[idx].BaseIndicesIndex;
            var replacingVertexIndex = shapeValues[idx].ReplacingVertexIndex;
            values.Add((baseIndicesIndex, replacingVertexIndex));
        }
        
        Mesh = mesh;
        Values = values;
    }
    public Mesh Mesh { get; }
    public IReadOnlyList<(ushort BaseIndicesIndex, ushort ReplacedVertexIndex)> Values { get; }
}
