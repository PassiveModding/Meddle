using Meddle.Utils.Files;

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
    public IReadOnlyList<(string name, short id)> AttributeMasks { get; private set; }
    public uint EnabledAttributeMask { get; private set; }
    public IReadOnlyList<(string name, short id)> ShapeMasks { get; private set; }
    public uint EnabledShapeMask { get; private set; }
    
    public static IEnumerable<string> GetEnabledValues(uint mask, IReadOnlyList<(string, short)> values)
    {
        foreach (var (name, id) in values)
        {
            if (((1 << id) & mask) != 0)
                yield return name;
        }
    }
    
    public record MdlGroup(string Path, MdlFile MdlFile, Material.MtrlGroup[] MtrlFiles, ShapeAttributeGroup? ShapeAttributeGroup);
    public record ShapeAttributeGroup
    {
        public ShapeAttributeGroup(uint EnabledShapeMask, uint EnabledAttributeMask, (string name, short id)[] ShapeMasks, (string name, short id)[] AttributeMasks)
        {
            this.EnabledShapeMask = EnabledShapeMask;
            this.EnabledAttributeMask = EnabledAttributeMask;
            this.ShapeMasks = ShapeMasks;
            this.AttributeMasks = AttributeMasks;
        }
        public uint EnabledShapeMask { get; init; }
        public uint EnabledAttributeMask { get; init; }
        public (string name, short id)[] ShapeMasks { get; init; }
        public (string name, short id)[] AttributeMasks { get; init; }
    }

    public Model(MdlGroup mdlGroup)
    {
        HandlePath = mdlGroup.Path;
        RaceCode = RaceDeformer.ParseRaceCode(Path);
        
        if (mdlGroup.MtrlFiles.Length != mdlGroup.MdlFile.ModelHeader.MaterialCount)
            throw new ArgumentException($"Material count mismatch: {mdlGroup.MdlFile.ModelHeader.MaterialCount} != {mdlGroup.MtrlFiles.Length}");
        
        // NOTE: Does not check for validity on files matching the mdlfile material names
        Materials = mdlGroup.MtrlFiles.Select(x => new Material(x)).ToArray();
        
        AttributeMasks = mdlGroup.ShapeAttributeGroup?.AttributeMasks ?? Array.Empty<(string, short)>();
        EnabledAttributeMask = mdlGroup.ShapeAttributeGroup?.EnabledAttributeMask ?? 0;
        ShapeMasks = mdlGroup.ShapeAttributeGroup?.ShapeMasks ?? Array.Empty<(string, short)>();
        EnabledShapeMask = mdlGroup.ShapeAttributeGroup?.EnabledShapeMask ?? 0;
        
        InitFromFile(mdlGroup.MdlFile);
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
                foreach (var element in meshVertexDecls.GetElements())
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

                
                meshes.Add(new Mesh(file, i, meshVertices, mesh.StartIndex, meshIndices, AttributeMasks.ToArray()));
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
