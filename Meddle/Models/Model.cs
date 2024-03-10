using SharpGLTF.Scenes;

// Penumbra ModelExport
namespace Meddle.Plugin.Models;

public struct Model(List<Mesh> meshes, GltfSkeleton? skeleton)
{
    public List<Mesh>    Meshes   { get; } = meshes;
    public GltfSkeleton? Skeleton { get; } = skeleton;
}
