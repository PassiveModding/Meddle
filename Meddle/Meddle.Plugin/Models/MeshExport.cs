using SharpGLTF.Geometry;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Models;

public record MeshExport(IMeshBuilder<MaterialBuilder> Mesh, bool UseSkinning, SubMesh? Submesh, IReadOnlyList<string>? Shapes);
