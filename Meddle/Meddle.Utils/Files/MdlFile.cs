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

    public enum MdlVersion : uint
    {
        V5 = 0x01000005,
        V6 = 0x01000006
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

    private readonly byte[] rawData;
    public readonly int RemainingOffset;
    public ReadOnlySpan<byte> RawData => rawData;
    public ReadOnlySpan<byte> RemainingData => rawData.AsSpan(RemainingOffset);

    public struct BoneTable
    {
        public uint BoneCount;
        public ushort[] BoneIndex;
    }

    public MdlFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    private static BoneTable ReadV5BoneTable(ref SpanBinaryReader reader)
    {
        return new BoneTable
        {
            BoneIndex = reader.Read<ushort>(64).ToArray(),
            BoneCount = reader.Read<uint>()
        };
    }

    private static BoneTable ReadV6BoneTable(ref SpanBinaryReader reader)
    {
        var table = new BoneTable();
        var startPos = reader.Position;
        var offset = reader.ReadUInt16();
        var size = reader.ReadUInt16();
        var retPos = reader.Position;
        reader.Seek(startPos + offset * 4, SeekOrigin.Begin);
        table.BoneIndex = reader.Read<ushort>(size).ToArray();
        table.BoneCount = (uint)table.BoneIndex.Length;
        reader.Seek(retPos, SeekOrigin.Begin);
        return table;
    }

    public MdlFile(ReadOnlySpan<byte> data)
    {
        rawData = data.ToArray();
        var reader = new SpanBinaryReader(data);
        FileHeader = reader.Read<ModelFileHeader>();
        VertexDeclarations = reader.Read<ModelResourceHandle.VertexDeclaration>(FileHeader.VertexDeclarationCount)
                                   .ToArray();
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
        TerrainShadowMeshes = reader.Read<ModelResourceHandle.TerrainShadowMesh>(ModelHeader.TerrainShadowMeshCount)
                                    .ToArray();
        Submeshes = reader.Read<ModelResourceHandle.Submesh>(ModelHeader.SubmeshCount).ToArray();
        TerrainShadowSubmeshes =
            reader.Read<ModelResourceHandle.TerrainShadowSubmesh>(ModelHeader.TerrainShadowSubmeshCount).ToArray();
        MaterialNameOffsets = reader.Read<uint>(ModelHeader.MaterialCount).ToArray();
        BoneNameOffsets = reader.Read<uint>(ModelHeader.BoneCount).ToArray();

        BoneTables = new BoneTable[ModelHeader.BoneTableCount];
        if (FileHeader.Version == (uint)MdlVersion.V5)
        {
            for (var i = 0; i < ModelHeader.BoneTableCount; i++)
            {
                BoneTables[i] = ReadV5BoneTable(ref reader);
            }
        }
        else if (FileHeader.Version == (uint)MdlVersion.V6)
        {
            for (var i = 0; i < ModelHeader.BoneTableCount; i++)
            {
                BoneTables[i] = ReadV6BoneTable(ref reader);
            }

            reader.Seek(ModelHeader.BoneTableArrayCountTotal * 2, SeekOrigin.Current);
        }
        else
        {
            throw new NotSupportedException($"Unsupported mdl version {FileHeader.Version}");
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

        var runtimePadding = FileHeader.RuntimeSize +
                             Unsafe.SizeOf<ModelFileHeader>() +
                             FileHeader.StackSize - reader.Position;
        reader.Seek((int)runtimePadding, SeekOrigin.Current);
        RemainingOffset = reader.Position;
    }
}
