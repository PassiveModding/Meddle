using Meddle.Utils.Export;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;

namespace Meddle.Utils;

public record MeshExport(IMeshBuilder<MaterialBuilder> Mesh, bool UseSkinning, SubMesh? Submesh, IReadOnlyList<string>? Shapes);
