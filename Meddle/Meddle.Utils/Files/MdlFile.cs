using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meddle.Utils.Files.Structs.Model;

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
    public readonly VertexDeclaration[] VertexDeclarations; // MeshCount total elements
    public readonly ushort StringCount;
    public readonly byte[] StringTable;
    public readonly ModelHeader ModelHeader;
    public readonly ElementId[] ElementIds;
    public readonly Lod[] Lods;
    public readonly ExtraLod[] ExtraLods;
    public readonly Mesh[] Meshes;
    public readonly uint[] AttributeNameOffsets;
    public readonly TerrainShadowMesh[] TerrainShadowMeshes;
    public readonly Submesh[] Submeshes;
    public readonly TerrainShadowSubmesh[] TerrainShadowSubmeshes;
    public readonly uint[] MaterialNameOffsets;
    public readonly uint[] BoneNameOffsets;
    public readonly BoneTable[] BoneTables;
    public readonly Shape[] Shapes;
    public readonly ShapeMesh[] ShapeMeshes;
    public readonly ShapeValue[] ShapeValues;

    public readonly uint SubmeshBoneMapByteSize;
    public readonly ushort[] SubmeshBoneMap;

    public readonly BoundingBox BoundingBoxes;
    public readonly BoundingBox ModelBoundingBoxes;
    public readonly BoundingBox WaterBoundingBoxes;
    public readonly BoundingBox VerticalFogBoundingBoxes;
    public readonly BoundingBox[] BoneBoundingBoxes;

    private readonly byte[] rawData;
    public ReadOnlySpan<byte> RawData => rawData;

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
        VertexDeclarations = reader.Read<VertexDeclaration>(FileHeader.VertexDeclarationCount)
                                   .ToArray();
        StringCount = reader.ReadUInt16();
        reader.ReadUInt16();
        var stringSize = reader.ReadUInt32();
        StringTable = reader.Read<byte>((int)stringSize).ToArray();

        ModelHeader = reader.Read<ModelHeader>();
        ElementIds = reader.Read<ElementId>(ModelHeader.ElementIdCount).ToArray();
        Lods = reader.Read<Lod>(3).ToArray();

        // Extra log enabled
        if ((ModelHeader.Flags2 & 0x10) != 0)
        {
            ExtraLods = reader.Read<ExtraLod>(3).ToArray();
        }
        else
        {
            ExtraLods = Array.Empty<ExtraLod>();
        }

        Meshes = reader.Read<Mesh>(ModelHeader.MeshCount).ToArray();

        AttributeNameOffsets = reader.Read<uint>(ModelHeader.AttributeCount).ToArray();
        TerrainShadowMeshes = reader.Read<TerrainShadowMesh>(ModelHeader.TerrainShadowMeshCount)
                                    .ToArray();
        Submeshes = reader.Read<Submesh>(ModelHeader.SubmeshCount).ToArray();
        TerrainShadowSubmeshes =
            reader.Read<TerrainShadowSubmesh>(ModelHeader.TerrainShadowSubmeshCount).ToArray();
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

        Shapes = reader.Read<Shape>(ModelHeader.ShapeCount).ToArray();
        ShapeMeshes = reader.Read<ShapeMesh>(ModelHeader.ShapeMeshCount).ToArray();
        ShapeValues = reader.Read<ShapeValue>(ModelHeader.ShapeValueCount).ToArray();
        SubmeshBoneMapByteSize = reader.ReadUInt32();
        var size = SubmeshBoneMapByteSize / Unsafe.SizeOf<ushort>();
        SubmeshBoneMap = reader.Read<ushort>((int)size).ToArray();

        var padding = reader.Read<byte>();
        reader.Seek(padding, SeekOrigin.Current);

        BoundingBoxes = reader.Read<BoundingBox>();
        ModelBoundingBoxes = reader.Read<BoundingBox>();
        WaterBoundingBoxes = reader.Read<BoundingBox>();
        VerticalFogBoundingBoxes = reader.Read<BoundingBox>();
        BoneBoundingBoxes = reader.Read<BoundingBox>(ModelHeader.BoneCount).ToArray();
    }
}
