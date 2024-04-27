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
    
    public readonly ModelFileHeader FileHeader;
    public readonly ModelResourceHandle.VertexDeclaration[] VertexDeclarations; // MeshCount total elements
    public readonly ushort StringCount;
    public readonly byte[] StringTable;
    public readonly ModelResourceHandle.ModelHeader ModelHeader;
    public readonly ModelResourceHandle.ElementId[] ElementIds;
    public readonly ModelResourceHandle.Lod[] Lods;
    public readonly ModelResourceHandle.ExtraLod[] ExtraLods;
    public readonly ModelResourceHandle.Mesh[] Meshes;
    public readonly uint[] AttributeNameOffsets;
    public readonly ModelResourceHandle.TerrainShadowMesh[] TerrainShadowMeshes;
    public readonly ModelResourceHandle.Submesh[] Submeshes;
    public readonly ModelResourceHandle.TerrainShadowSubmesh[] TerrainShadowSubmeshes;
    public readonly uint[] MaterialNameOffsets;
    public readonly uint[] BoneNameOffsets;
    public readonly BoneTable[] BoneTables;
    public readonly ModelResourceHandle.Shape[] Shapes;
    public readonly ModelResourceHandle.ShapeMesh[] ShapeMeshes;
    public readonly ModelResourceHandle.ShapeValue[] ShapeValues;
    
    public readonly uint SubmeshBoneMapByteSize;
    public readonly ushort[] SubmeshBoneMap;
    
    public readonly ModelResourceHandle.BoundingBox BoundingBoxes;
    public readonly ModelResourceHandle.BoundingBox ModelBoundingBoxes;
    public readonly ModelResourceHandle.BoundingBox WaterBoundingBoxes;
    public readonly ModelResourceHandle.BoundingBox VerticalFogBoundingBoxes;
    public readonly ModelResourceHandle.BoundingBox[] BoneBoundingBoxes;
    
    public readonly byte[] RawData;
    public readonly int RemainingOffset;
    public ReadOnlySpan<byte> RemainingData => RawData.AsSpan(RemainingOffset);
    
    // combine both types
    public struct BoneTable
    {
        public ushort BoneCount;
        public ushort Unk1;
        public ushort? Unk2;
        public ushort[] BoneIndex;
    }
    
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

        BoneTables = new BoneTable[ModelHeader.BoneTableCount];
        if (FileHeader.Version <= 16777221)
        {
            for (var i = 0; i < ModelHeader.BoneTableCount; i++)
            {
                unsafe
                {
                    var table = reader.Read<ModelResourceHandle.BoneTable>();
                    BoneTables[i].BoneCount = table.BoneCount;
                    var indexes = new ushort[table.BoneCount];
                    for (var j = 0; j < table.BoneCount; j++)
                    {
                        indexes[j] = table.BoneIndex[j];
                    }
                    BoneTables[i].BoneIndex = indexes;
                }
            }
        }
        else
        {
            var boneTableIdx = new ushort[ModelHeader.BoneTableCount];
            var boneTableSizes = new ushort[ModelHeader.BoneTableCount];
            for (var i = 0; i < ModelHeader.BoneTableCount; i++)
            {
                boneTableIdx[i] = reader.ReadUInt16();
                boneTableSizes[i] = reader.ReadUInt16();
            }

            BoneTables = new BoneTable[ModelHeader.BoneTableCount];
            for (var i = 0; i < ModelHeader.BoneTableCount; i++)
            {
                BoneTables[i].Unk1 = boneTableIdx[i];
                BoneTables[i].BoneCount = boneTableSizes[i];
                BoneTables[i].BoneIndex = reader.Read<ushort>(boneTableSizes[i]).ToArray();
                if (boneTableSizes[i] % 2 != 0)
                {
                    // Probably padding keeping for now
                    BoneTables[i].Unk2 = reader.Read<ushort>();
                }
            }
        }
        
        Shapes = reader.Read<ModelResourceHandle.Shape>(ModelHeader.ShapeCount).ToArray();
        ShapeMeshes = reader.Read<ModelResourceHandle.ShapeMesh>(ModelHeader.ShapeMeshCount).ToArray();
        ShapeValues = reader.Read<ModelResourceHandle.ShapeValue>(ModelHeader.ShapeValueCount).ToArray();
        SubmeshBoneMapByteSize = reader.ReadUInt32();
        var size = SubmeshBoneMapByteSize / Unsafe.SizeOf<ushort>();
        SubmeshBoneMap = reader.Read<ushort>((int)size).ToArray();

        var padding = reader.Read<byte>();
        reader.Seek(padding, SeekOrigin.Current);

        BoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>();
        ModelBoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>();
        WaterBoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>();
        VerticalFogBoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>();
        BoneBoundingBoxes = reader.Read<ModelResourceHandle.BoundingBox>(ModelHeader.BoneCount).ToArray();
        
        RawData = data.ToArray();
        RemainingOffset = reader.Position;
    }
}
