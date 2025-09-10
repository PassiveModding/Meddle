using System.Text.Json;
using System.Text.Json.Serialization;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;

namespace Meddle.Utils;

public static class ModelBuilder
{
    private static JsonSerializerOptions SerializerOptions => new()
    {
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    
    public static IReadOnlyList<MeshExport> BuildMeshes(
        Model model,
        IReadOnlyList<MaterialBuilder> materials,
        IReadOnlyList<BoneNodeBuilder> boneMap,
        (GenderRace fromDeform, GenderRace toDeform, RaceDeformer deformer)? raceDeformer)
    {
        var meshes = new List<MeshExport>();

        var modelPathName = Path.GetFileNameWithoutExtension(model.HandlePath.TrimHandlePath());
        foreach (var mesh in model.Meshes)
        {
            MeshBuilder meshBuilder;
            MaterialBuilder material;
            if (mesh.MaterialIdx >= materials.Count)
            {
                if (materials.Count != 0)
                {
                    Global.Logger.LogWarning("[{Path}] Mesh {MeshIdx} has invalid material index {MaterialIdx}",
                                             model.HandlePath,
                                             mesh.MeshIdx,
                                             mesh.MaterialIdx);
                    material = materials.FirstOrDefault(new MaterialBuilder($"{modelPathName}_{mesh.MeshIdx}_{mesh.MaterialIdx}_fallback"));
                }
                else
                {
                    material = new MaterialBuilder($"{modelPathName}_{mesh.MeshIdx}_{mesh.MaterialIdx}_empty");
                }
            }
            else
            {
                material = materials[mesh.MaterialIdx];
            }
            
            if (mesh.BoneTable != null)
            {
                meshBuilder = new MeshBuilder(mesh, boneMap, material, raceDeformer);
            }
            else
            {
                meshBuilder = new MeshBuilder(mesh, null, material, raceDeformer);
            }

            Global.Logger.LogDebug("[{Path}] Building mesh {MeshIdx}\n{Mesh}",
                                   model.HandlePath,
                                   mesh.MeshIdx,
                                   JsonSerializer.Serialize(new
                                   {
                                       Material = material.Name,
                                       GeometryType = meshBuilder.GeometryT.Name,
                                       MaterialType = meshBuilder.MaterialT.Name,
                                       SkinningType = meshBuilder.SkinningT.Name,
                                       Vertex = (Vertex?)(mesh.Vertices.Count == 0 ? null : mesh.Vertices[0]),
                                   }, SerializerOptions));
            
            if (mesh.SubMeshes.Count == 0)
            {
                var mb = meshBuilder.BuildMesh();
                mb.Name = model.HandlePath;
                meshes.Add(new MeshExport(mb, null, null));
                continue;
            }

            for (var i = 0; i < mesh.SubMeshes.Count; i++)
            {
                var modelSubMesh = mesh.SubMeshes[i];
                var subMesh = meshBuilder.BuildSubMesh(modelSubMesh);
                subMesh.Name = $"{model.HandlePath}_{i}";
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
