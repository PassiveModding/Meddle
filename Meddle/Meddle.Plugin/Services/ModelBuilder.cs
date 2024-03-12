﻿using Dalamud.Plugin.Services;
using Meddle.Plugin.Models;
using Meddle.Plugin.Xande;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using RaceDeformer = Meddle.Plugin.Xande.RaceDeformer;

namespace Meddle.Plugin.Services;

public class ModelBuilder
{
    public IPluginLog Log { get; }

    public ModelBuilder(IPluginLog log)
    {
        Log = log;
    }
    
    public IEnumerable<(IMeshBuilder<MaterialBuilder> mesh, bool useSkinning, SubMesh? submesh)> BuildMeshes(Model model, 
                               IReadOnlyList<MaterialBuilder> materials, 
                               IReadOnlyList<BoneNodeBuilder> boneMap, 
                               (ushort targetDeform, RaceDeformer deformer)? raceDeformer)
    {
        var meshes = new List<(IMeshBuilder<MaterialBuilder> mesh, bool useSkinning, SubMesh? submesh)>();
        (RaceDeformer deformer, ushort from, ushort to)? deform = null;
        if (raceDeformer != null)
        {
            var rd = raceDeformer.Value;
            deform = (rd.deformer, model.RaceCode!.Value, rd.targetDeform);
        }

        foreach (var mesh in model.Meshes)
        {
            var useSkinning = mesh.BoneTable != null;
            Log.Debug($"Building mesh {model.HandlePath}_{mesh.MeshIdx} with skinning: {useSkinning}");
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
            
            if (mesh.Submeshes.Count == 0)
            {
                var mb = meshBuilder.BuildMesh();
                meshes.Add((mb, useSkinning, null));
                continue;
            }
            
            for (var i = 0; i < mesh.Submeshes.Count; i++)
            {
                var modelSubMesh = mesh.Submeshes[i];
                var subMesh = meshBuilder.BuildSubmesh(modelSubMesh);
                subMesh.Name = $"{model.HandlePath}_{mesh.MeshIdx}.{i}";
                if (modelSubMesh.Attributes.Count > 0)
                {
                    subMesh.Name += $";{string.Join(";", modelSubMesh.Attributes)}";
                }
                
                Log.Debug($"Building submesh {subMesh.Name}");

                var subMeshStart = (int)modelSubMesh.IndexOffset;
                var subMeshEnd = subMeshStart + (int)modelSubMesh.IndexCount;
                meshBuilder.BuildShapes(model.Shapes, subMesh, subMeshStart, subMeshEnd);

                meshes.Add((subMesh, useSkinning, modelSubMesh));
            }
        }
        
        return meshes;
    }
}