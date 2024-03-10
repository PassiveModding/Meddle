using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

// Penumbra ModelExport
namespace Meddle.Plugin.Models;

public class Mesh(IEnumerable<MeshData> meshes, GltfSkeleton? skeleton)
{
    public IEnumerable<MeshData> Meshes   { get; } = meshes;
    public GltfSkeleton?         Skeleton { get; } = skeleton;
}

public struct MeshData
{
    public IMeshBuilder<MaterialBuilder> Mesh;
    public string[]                      Attributes;
}
