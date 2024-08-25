using Meddle.Utils.Export;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;

namespace Meddle.Utils;

public static class ModelBuilder
{
    public static IReadOnlyList<MeshExport> BuildMeshes(
        Model model,
        IReadOnlyList<MaterialBuilder> materials,
        IReadOnlyList<BoneNodeBuilder> boneMap,
        (GenderRace fromDeform, GenderRace toDeform, RaceDeformer deformer)? raceDeformer)
    {
        var meshes = new List<MeshExport>();

        foreach (var mesh in model.Meshes)
        {
            MeshBuilder meshBuilder;
            var material = materials[mesh.MaterialIdx];
            if (mesh.BoneTable != null)
            {
                var jointIdMapping = new List<int>();
                var jointLut = boneMap
                               .Select((joint, i) => (joint.BoneName, i))
                               .ToArray();
                foreach (var boneName in mesh.BoneTable!)
                {
                    var match = jointLut.FirstOrDefault(x => x.BoneName.Equals(boneName, StringComparison.Ordinal));
                    if (match == default)
                        throw new Exception($"Bone {boneName} on {model.HandlePath} not found in bone map.");
                    jointIdMapping.Add(match.i);
                }

                meshBuilder = new MeshBuilder(mesh, jointIdMapping.ToArray(), material, raceDeformer);
            }
            else
            {
                meshBuilder = new MeshBuilder(mesh, null, material, raceDeformer);
            }

            meshBuilder.BuildVertices();
            var modelPathName = Path.GetFileNameWithoutExtension(model.Path);

            if (mesh.SubMeshes.Count == 0)
            {
                var mb = meshBuilder.BuildMesh();
                mb.Name = $"{modelPathName}_{mesh.MeshIdx}_{material.Name}";
                meshes.Add(new MeshExport(mb, null, null));
                continue;
            }

            for (var i = 0; i < mesh.SubMeshes.Count; i++)
            {
                var modelSubMesh = mesh.SubMeshes[i];
                var subMesh = meshBuilder.BuildSubMesh(modelSubMesh);
                subMesh.Name = $"{modelPathName}_{mesh.MeshIdx}_{material.Name}.{i}";
                if (modelSubMesh.Attributes.Count > 0)
                {
                    subMesh.Name += $";{string.Join(";", modelSubMesh.Attributes)}";
                }


                var subMeshStart = (int)modelSubMesh.IndexOffset;
                var subMeshEnd = subMeshStart + (int)modelSubMesh.IndexCount;

                var shapeNames = meshBuilder.BuildShapes(model.Shapes, subMesh, subMeshStart, subMeshEnd);

                meshes.Add(new MeshExport(subMesh, modelSubMesh, shapeNames.ToArray()));
            }
        }

        return meshes;
    }

    public record MeshExport(
        IMeshBuilder<MaterialBuilder> Mesh,
        SubMesh? Submesh,
        IReadOnlyList<string>? Shapes);
}
