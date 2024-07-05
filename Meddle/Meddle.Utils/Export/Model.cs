using System.Text.Json;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Meddle.Utils.Files;
using Meddle.Utils.Models;

namespace Meddle.Utils.Export;

public unsafe class Model
{
    public string HandlePath { get; private set; }
    public string? ResolvedPath { get; private set; }
    public string Path => ResolvedPath ?? HandlePath;
    public GenderRace RaceCode { get; private set; }

    public IReadOnlyList<Material?> Materials { get; private set; }
    public IReadOnlyList<Mesh> Meshes { get; private set; }
    public IReadOnlyList<ModelShape> Shapes { get; private set; }
    public uint ShapesMask { get; private set; }
    public uint AttributesMask { get; private set; }
    public IReadOnlyList<string> EnabledShapes { get; private set; }
    public IReadOnlyList<string> EnabledAttributes { get; private set; }
    
    public record MdlGroup(string Path, MdlFile MdlFile, Material.MtrlGroup[] MtrlFiles);

    public Model(MdlGroup mdlGroup)
    {
        HandlePath = mdlGroup.Path;
        RaceCode = RaceDeformer.ParseRaceCode(Path);
        
        if (mdlGroup.MtrlFiles.Length != mdlGroup.MdlFile.ModelHeader.MaterialCount)
            throw new ArgumentException($"Material count mismatch: {mdlGroup.MdlFile.ModelHeader.MaterialCount} != {mdlGroup.MtrlFiles.Length}");
        
        // NOTE: Does not check for validity on files matching the mdlfile material names
        Materials = mdlGroup.MtrlFiles.Select(x => new Material(x)).ToArray();
        
        InitFromFile(mdlGroup.MdlFile);
    }
    
    public Model(MdlFile file, string handlePath,
                 IReadOnlyDictionary<string, ShpkFile> shpkFiles, 
                 IReadOnlyDictionary<string, MtrlFile> materialFiles, 
                 IReadOnlyDictionary<string, TexFile> textureFiles)
    {
        HandlePath = handlePath;
        RaceCode = RaceDeformer.ParseRaceCode(Path);
        
        var materials = new Material[file.ModelHeader.MaterialCount];
        var materialNames = file.GetMaterialNames();
        for (var i = 0; i < file.ModelHeader.MaterialCount; ++i)
        {
            var materialName = materialNames[(int)file.MaterialNameOffsets[i]];
            if (!materialFiles.TryGetValue(materialName, out var mtrlFile))
                throw new ArgumentException($"Material {materialName} not found");
            
            // first file ending in name
            var shaderPackage = shpkFiles.FirstOrDefault(x => x.Key.EndsWith(mtrlFile.GetShaderPackageName())).Value;
            materials[i] = new Material(mtrlFile, materialName, shaderPackage, textureFiles);
        }
        
        Materials = materials;
        
        InitFromFile(file);
    }
    
        private void InitFromFile(MdlFile file)
    {
        const int lodIdx = 0;
        var lod = file.Lods[lodIdx];
        var meshRanges = new List<Range>
        {
            lod.MeshIndex..(lod.MeshIndex + lod.MeshCount),
            lod.WaterMeshIndex..(lod.WaterMeshIndex + lod.WaterMeshCount),
            lod.ShadowMeshIndex..(lod.ShadowMeshIndex + lod.ShadowMeshCount),
            lod.TerrainShadowMeshIndex..(lod.TerrainShadowMeshIndex + lod.TerrainShadowMeshCount),
            lod.VerticalFogMeshIndex..(lod.VerticalFogMeshIndex + lod.VerticalFogMeshCount),
        };
        if (file.ExtraLods.Length > 0)
        {
            var extraLod = file.ExtraLods[lodIdx];
            meshRanges.AddRange(new[]
            {
                extraLod.LightShaftMeshIndex..(extraLod.LightShaftMeshIndex + extraLod.LightShaftMeshCount),
                extraLod.GlassMeshIndex..(extraLod.GlassMeshIndex + extraLod.GlassMeshCount),
                extraLod.MaterialChangeMeshIndex..(extraLod.MaterialChangeMeshIndex + extraLod.MaterialChangeMeshCount),
                extraLod.CrestChangeMeshIndex..(extraLod.CrestChangeMeshIndex + extraLod.CrestChangeMeshCount),
            });
        }
        
        // consolidate ranges
        var meshes = new List<Mesh>();
        var vertexBuffer = 
            file.RawData.Slice((int)file.FileHeader.VertexOffset[lodIdx], (int)file.FileHeader.VertexBufferSize[lodIdx]);
        var indexBuffer = 
            file.RawData.Slice((int)file.FileHeader.IndexOffset[lodIdx], (int)file.FileHeader.IndexBufferSize[lodIdx]);
        
        foreach (var range in meshRanges.AsConsolidated())
        {
            for (var i = range.Start.Value; i < range.End.Value; i++)
            {
                var mesh = file.Meshes[i];
                var meshVertexDecls = file.VertexDeclarations[i];
                var meshVertices = new Vertex[mesh.VertexCount];
                
                // Index data
                var ushortIndexReader = new SpanBinaryReader(indexBuffer);
                ushortIndexReader.Read<ushort>((int)mesh.StartIndex);
                var meshIndices = ushortIndexReader.Read<ushort>((int)mesh.IndexCount).ToArray();
                
                // Vertex data
                foreach (var element in meshVertexDecls.Elements)
                {
                    if (element.Stream == 255)
                        break;

                    var streamIdx = element.Stream;
                    var stride = mesh.VertexBufferStride[streamIdx];
                    var bufferOffset = mesh.VertexBufferOffset[streamIdx];
                    var offsetVertexStreamBuffer =
                        vertexBuffer.Slice((int)bufferOffset, stride * mesh.VertexCount);

                    for (var j = 0; j < meshVertices.Length; ++j)
                    {
                        var buf = offsetVertexStreamBuffer.Slice(j * stride, stride);
                        Vertex.Apply(ref meshVertices[j], buf, element);
                    }
                }
                
                foreach (var index in meshIndices)
                {
                    if (index >= meshVertices.Length)
                        throw new ArgumentException($"Mesh {i} has index {index}, but only {meshVertices.Length} vertices exist");
                }

                if (meshIndices.Length != mesh.IndexCount)
                    throw new ArgumentException($"Mesh {i} has {meshIndices.Length} indices, but {mesh.IndexCount} were expected");

                
                meshes.Add(new Mesh(file, i, meshVertices, mesh.StartIndex, meshIndices));
            }
        }
        
        Meshes = meshes;
        
        var shapeMeshes = new ShapeMesh[file.ModelHeader.ShapeMeshCount];
        for (var i = 0; i < shapeMeshes.Length; i++)
        {
            var shapeMesh = file.ShapeMeshes[i];
            var meshMatch = Meshes.FirstOrDefault(x => file.Meshes[x.MeshIdx].StartIndex == shapeMesh.MeshIndexOffset);
            if (meshMatch == null)
                continue;
            shapeMeshes[i] = new ShapeMesh(file.ShapeValues, shapeMesh, meshMatch, i);
        }

        var shapes = new ModelShape[file.ModelHeader.ShapeCount];
        var stringReader = new SpanBinaryReader(file.StringTable);
        for (var i = 0; i < file.ModelHeader.ShapeCount; i++)
        {
            var shape = file.Shapes[i];
            var name = stringReader.ReadString((int)shape.StringOffset);
            shapes[i] = new ModelShape(shape, name, lodIdx, shapeMeshes);
        }
        
        Shapes = shapes;
    }
    
}
