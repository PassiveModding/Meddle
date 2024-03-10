using Dalamud.Plugin.Services;
using Lumina.Data.Parsing;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utility;
using Penumbra.GameData.Files;
using SharpGLTF.Materials;

namespace Meddle.Plugin.Services;

public class ModelService(MaterialService materialService, IPluginLog logger) : IService
{
    /// <summary> Export a model in preparation for usage in a glTF file. If provided, skeleton will be used to skin the resulting meshes where appropriate. </summary>
    public Model Export(in ExportConfig config, MdlFile mdl, GltfSkeleton skeleton, Dictionary<string, Material> rawMaterials, RaceDeformer? raceDeformer)
    {
        var materials = ConvertMaterials(mdl, rawMaterials);
        var meshes = ConvertMeshes(mdl, materials, skeleton, raceDeformer, config.GenerateMissingBones);
        return new Model(meshes, skeleton);
    }

    /// <summary> Convert a .mdl to a mesh (group) per LoD. </summary>
    private List<Mesh> ConvertMeshes(MdlFile mdl, MaterialBuilder[] materials, GltfSkeleton skeleton, RaceDeformer? raceDeformer, bool generateMissingBones)
    {

        // Only caring about the first LoD for now since we want a high quality export.
        var lod = mdl.Lods[0];
        return ConvertLodMeshes(lod, mdl, materials, skeleton, raceDeformer, generateMissingBones);
        /*         
        var meshes = new List<Mesh>();
         
        if (raceDeformer != null)
        {
            // ignore other lod meshes for deform
            var lod = mdl.Lods[0];
            return ConvertLodMeshes(config, lod, mdl, materials, skeleton, raceDeformer, notifier.WithContext("LoD 0"));
        }

        for (byte lodIndex = 0; lodIndex < mdl.LodCount; lodIndex++)
        {
            var lod = mdl.Lods[lodIndex];
            var lodMeshes = ConvertLodMeshes(config, lod, mdl, materials, skeleton, raceDeformer, notifier.WithContext($"LoD {lodIndex}"));
            meshes.AddRange(lodMeshes);
        }

        return meshes;*/
    }
    
    private List<Mesh> ConvertLodMeshes(MdlStructs.LodStruct lod, MdlFile mdl, MaterialBuilder[] materials, GltfSkeleton skeleton, RaceDeformer? raceDeformer, bool generateMissingBones)
    {
        var fullMeshes = new List<Mesh>();
        for (ushort meshOffset = 0; meshOffset < lod.MeshCount; meshOffset++)
        {
            var meshIndex = (ushort)(lod.MeshIndex + meshOffset);
            var export = new MeshExport(mdl, 0, meshIndex, materials, skeleton, raceDeformer);
            var meshes = export.BuildMeshes();
            //var meshes = MeshUtility.BuildMeshes(mdl, 0, meshIndex, materials[meshStruct.MaterialIndex], meshStruct, skeleton, raceDeformer, generateMissingBones);
            fullMeshes.Add(new Mesh(meshes, skeleton));
        }
        return fullMeshes;
    }

    /// <summary> Build materials for each of the material slots in the .mdl. </summary>
    private MaterialBuilder[] ConvertMaterials(MdlFile mdl, IReadOnlyDictionary<string, Material> rawMaterials)
        => mdl.Materials
            .Select(name => 
            {
                if (rawMaterials.TryGetValue(name, out var rawMaterial))
                {
                    logger.Debug($"Building material \"{name}\". {rawMaterial.Mtrl.ShaderPackage.Name}");
                    return rawMaterial switch 
                    {
                        CharacterMaterial material => materialService.BuildCharacterMaterial(material, name),
                        CharacterGlassMaterial material => materialService.BuildCharacterGlassMaterial(material, name),
                        IrisMaterial material => materialService.BuildIrisMaterial(material, name),
                        HairMaterial material => materialService.BuildHairMaterial(material, name),
                        SkinMaterial material => materialService.BuildSkinMaterial(material, name),
                        _ => materialService.BuildFallbackMaterial(rawMaterial, name)
                    };
                }

                logger.Warning($"Material \"{name}\" missing, using blank fallback.");
                return MaterialService.Unknown;
            })
            .ToArray();
}