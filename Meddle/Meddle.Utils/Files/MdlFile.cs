using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace Meddle.Utils.Files;

public class MdlFile
{
    [StructLayout(LayoutKind.Sequential, Size = 68)]
    public unsafe struct ModelFileHeader
    {
        public uint Version;
        public uint StackSize;
        public uint RuntimeSize;
        public ushort VertexDeclarationCount;
        public ushort MaterialCount;
        public fixed uint VertexOffset[3];
        public fixed uint IndexOffset[3];
        public fixed uint VertexBufferSize[3];
        public fixed uint IndexBufferSize[3];
        public byte LodCount;
        public bool EnableIndexBufferStreaming;
        public bool EnableEdgeGeometry;
    }
    
    public ModelFileHeader FileHeader;
    public ModelResourceHandle.VertexDeclaration[] VertexDeclarations; // MeshCount total elements
    public ushort StringCount;
    public byte[] StringTable;
    public ModelResourceHandle.ModelHeader ModelHeader;
    public ModelResourceHandle.ElementId[] ElementIds;
    public ModelResourceHandle.Lod[] Lods;
    public ModelResourceHandle.ExtraLod[] ExtraLods;
    public ModelResourceHandle.Mesh[] Meshes;
    public uint[] AttributeNameOffsets;
    public ModelResourceHandle.TerrainShadowMesh[] TerrainShadowMeshes;
    public ModelResourceHandle.Submesh[] Submeshes;
    public ModelResourceHandle.TerrainShadowSubmesh[] TerrainShadowSubmeshes;
    public uint[] MaterialNameOffsets;
    public uint[] BoneNameOffsets;
    public ModelResourceHandle.BoneTable[] BoneTables;
    public ModelResourceHandle.Shape[] Shapes;
    public ModelResourceHandle.ShapeMesh[] ShapeMeshes;
    public ModelResourceHandle.ShapeValue[] ShapeValues;
    
    public uint SubmeshBoneMapByteSize;
    public ushort[] SubmeshBoneMap;
    
    public ModelResourceHandle.BoundingBox BoundingBoxes;
    public ModelResourceHandle.BoundingBox ModelBoundingBoxes;
    public ModelResourceHandle.BoundingBox WaterBoundingBoxes;
    public ModelResourceHandle.BoundingBox VerticalFogBoundingBoxes;
    public ModelResourceHandle.BoundingBox[] BoneBoundingBoxes;
    
    public MdlFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public MdlFile(ReadOnlySpan<byte> data)
    {
        var reader = new SpanBinaryReader(data);
        FileHeader = reader.Read<ModelFileHeader>();
        VertexDeclarations = reader.Read<ModelResourceHandle.VertexDeclaration>(FileHeader.VertexDeclarationCount).ToArray();
        StringCount = reader.ReadUInt16();
        reader.ReadUInt16();
        var stringSize = reader.ReadUInt32();
        StringTable = reader.Read<byte>((int)stringSize).ToArray();
        
        ModelHeader = reader.Read<ModelResourceHandle.ModelHeader>();
        ElementIds = reader.Read<ModelResourceHandle.ElementId>(ModelHeader.ElementIdCount).ToArray();
        Lods = reader.Read<ModelResourceHandle.Lod>(3).ToArray();
        
        // Extra log enabled
        if ((ModelHeader.Flags2 & 0x10) != 0)
        {
            ExtraLods = reader.Read<ModelResourceHandle.ExtraLod>(3).ToArray();
        }
        else
        {
            ExtraLods = Array.Empty<ModelResourceHandle.ExtraLod>();
        }
        
        Meshes = reader.Read<ModelResourceHandle.Mesh>(ModelHeader.MeshCount).ToArray();
        
        AttributeNameOffsets = reader.Read<uint>(ModelHeader.AttributeCount).ToArray();
        TerrainShadowMeshes = reader.Read<ModelResourceHandle.TerrainShadowMesh>(ModelHeader.TerrainShadowMeshCount).ToArray();
        Submeshes = reader.Read<ModelResourceHandle.Submesh>(ModelHeader.SubmeshCount).ToArray();
        TerrainShadowSubmeshes = reader.Read<ModelResourceHandle.TerrainShadowSubmesh>(ModelHeader.TerrainShadowSubmeshCount).ToArray();
        MaterialNameOffsets = reader.Read<uint>(ModelHeader.MaterialCount).ToArray();
        BoneNameOffsets = reader.Read<uint>(ModelHeader.BoneCount).ToArray();
        BoneTables = reader.Read<ModelResourceHandle.BoneTable>(ModelHeader.BoneTableCount).ToArray();
        Shapes = reader.Read<ModelResourceHandle.Shape>(ModelHeader.ShapeCount).ToArray();
        ShapeMeshes = reader.Read<ModelResourceHandle.ShapeMesh>(ModelHeader.ShapeMeshCount).ToArray();
        ShapeValues = reader.Read<ModelResourceHandle.ShapeValue>(ModelHeader.ShapeValueCount).ToArray();
        SubmeshBoneMapByteSize = reader.ReadUInt32();
        
        // Workaround for new models killing old impl
        if (SubmeshBoneMapByteSize < byte.MaxValue)
        {
            var size = SubmeshBoneMapByteSize / Unsafe.SizeOf<ushort>();
            SubmeshBoneMap = reader.Read<ushort>((int)size).ToArray();

            var padding = reader.Read<byte>();
            reader.Seek(padding, SeekOrigin.Current);

            BoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>();
            ModelBoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>();
            WaterBoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>();
            VerticalFogBoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>();
            BoneBoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>(ModelHeader.BoneCount).ToArray();
        }
    }
}
