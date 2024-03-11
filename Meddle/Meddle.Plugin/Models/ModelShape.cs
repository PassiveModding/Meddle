using System.Text.Json.Serialization;
using Lumina.Models.Models;

namespace Meddle.Plugin.Models;

public class ModelShape
{
    public string Name { get; set; }
    [JsonIgnore]
    public List<ModelShapeMesh> Meshes { get; set; }

    public ModelShape(Shape shape)
    {
        Name = shape.Name;
        Meshes = new();
        foreach (var mesh in shape.Meshes)
            Meshes.Add(new(mesh));
    }
}
