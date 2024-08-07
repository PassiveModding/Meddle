﻿using Meddle.Utils.Export;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;

namespace Meddle.Utils;

public static class ModelBuilder
{
    public static IReadOnlyList<MeshExport> BuildMeshes(
        Model model,
        IReadOnlyList<MaterialBuilder> materials,
        IReadOnlyList<BoneNodeBuilder> boneMap,
        (GenderRace targetDeform, RaceDeformer deformer)? raceDeformer,
        bool disableSkinning = false)
    {
        var meshes = new List<MeshExport>();
        (RaceDeformer deformer, ushort from, ushort to)? deform = null;
        if (raceDeformer != null && model.RaceCode != GenderRace.Unknown)
        {
            var rd = raceDeformer.Value;
            deform = (rd.deformer, (ushort)model.RaceCode, (ushort)rd.targetDeform);
        }

        foreach (var mesh in model.Meshes)
        {
            var useSkinning = mesh.BoneTable != null;
            MeshBuilder meshBuilder;
            var material = materials[mesh.MaterialIdx];
            if (useSkinning && !disableSkinning)
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

                meshBuilder = new MeshBuilder(mesh, jointIdMapping.ToArray(), material, deform);
            }
            else
            {
                meshBuilder = new MeshBuilder(mesh, null, material, deform);
            }

            meshBuilder.BuildVertices();
            var modelPathName = Path.GetFileNameWithoutExtension(model.Path);

            if (mesh.SubMeshes.Count == 0)
            {
                var mb = meshBuilder.BuildMesh();
                mb.Name = $"{modelPathName}_{mesh.MeshIdx}_{material.Name}";
                meshes.Add(new MeshExport(mb, useSkinning, null, null));
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

                meshes.Add(new MeshExport(subMesh, useSkinning, modelSubMesh, shapeNames.ToArray()));
            }
        }

        return meshes;
    }

    public record MeshExport(
        IMeshBuilder<MaterialBuilder> Mesh,
        bool UseSkinning,
        SubMesh? Submesh,
        IReadOnlyList<string>? Shapes);
}
