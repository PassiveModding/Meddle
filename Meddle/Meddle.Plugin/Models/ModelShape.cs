namespace Meddle.Plugin.Models;

public class ModelShape(string name, IReadOnlyList<ShapeMesh> meshes)
{
    public string Name { get; } = name;
    public IReadOnlyList<ShapeMesh> Meshes { get; } = meshes;
}
