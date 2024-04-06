using Dalamud.Plugin.Services;
using Meddle.Plugin.Enums;
using Meddle.Plugin.Models;
using Meddle.Plugin.Xande;
using SharpGLTF.Materials;
using RaceDeformer = Meddle.Plugin.Xande.RaceDeformer;

namespace Meddle.Plugin.Services;

public class ModelBuilder(IPluginLog log)
{
    public IReadOnlyList<MeshExport> BuildMeshes(Model model, 
                                                 IReadOnlyList<MaterialBuilder> materials, 
                                                 IReadOnlyList<BoneNodeBuilder> boneMap, 
                                                 (GenderRace targetDeform, RaceDeformer deformer)? raceDeformer)
    {
        var meshes = new List<MeshExport>();
        (RaceDeformer deformer, ushort from, ushort to)? deform = null;
        if (raceDeformer != null)
        {
            var rd = raceDeformer.Value;
            deform = (rd.deformer, (ushort)model.RaceCode, (ushort)rd.targetDeform);
        }

        foreach (var mesh in model.Meshes)
        {
            var useSkinning = mesh.BoneTable != null;
            log.Debug($"Building mesh {model.Path}_{mesh.MeshIdx} with skinning: {useSkinning}");
            MeshBuilder meshBuilder;
            var material = materials[mesh.MaterialIdx];
            if (useSkinning)
            {
                var jointIdMapping = new List<int>();
                var jointLut = boneMap
                               .Select((joint, i) => (joint.BoneName, i))
                               .ToArray();
                foreach (var boneName in mesh.BoneTable!)
                {
                    jointIdMapping.Add(jointLut.First(x => x.BoneName.Equals(boneName, StringComparison.Ordinal)).i);
                }

                meshBuilder = new MeshBuilder(mesh, true, jointIdMapping.ToArray(), material, deform);
            }
            else
            {
                meshBuilder = new MeshBuilder(mesh, false, Array.Empty<int>(), material, deform);
            }
            
            meshBuilder.BuildVertices();
            
            if (mesh.SubMeshes.Count == 0)
            {
                var mb = meshBuilder.BuildMesh();
                meshes.Add(new MeshExport(mb, useSkinning, null, null));
                continue;
            }
            
            for (var i = 0; i < mesh.SubMeshes.Count; i++)
            {
                var modelSubMesh = mesh.SubMeshes[i];
                var subMesh = meshBuilder.BuildSubMesh(modelSubMesh);
                subMesh.Name = $"{model.Path}_{mesh.MeshIdx}.{i}";
                if (modelSubMesh.Attributes.Count > 0)
                {
                    subMesh.Name += $";{string.Join(";", modelSubMesh.Attributes)}";
                }
                
                log.Debug($"Building submesh {subMesh.Name}");

                var subMeshStart = (int)modelSubMesh.IndexOffset;
                var subMeshEnd = subMeshStart + (int)modelSubMesh.IndexCount;

                var shapeNames = meshBuilder.BuildShapes(model.Shapes, subMesh, subMeshStart, subMeshEnd);

                meshes.Add(new MeshExport(subMesh, useSkinning, modelSubMesh, shapeNames.ToArray()));
            }
        }
        
        return meshes;
    }
}
