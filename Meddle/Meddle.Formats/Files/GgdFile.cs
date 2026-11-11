using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Meddle.Formats.Files;

/// <summary>
/// Parses a GrassGridData (.ggd) file, e.g. <c>bg/.../grass/019_001_022_l.ggd</c>.
/// One file per grass cell + LOD tier; cells are indexed by the zone's <see cref="GzdFile"/>.
/// </summary>
public class GgdFile
{
    public const uint GgdMagic = 0x67676420;    // " dgg"
    public const uint RecordMagic = 0x00736764; // "dgs\0"

    public GgdHeader Header;
    /// <summary>Per grass type (0..7) bend amount, /255.</summary>
    public byte[] BendParams = new byte[8];
    /// <summary>Per grass type (0..7) bend scale, /255*0.5+1.</summary>
    public byte[] ScaleParams = new byte[8];
    public GgdRecord[] Records;

    [JsonIgnore]
    public byte[] RawData;
    [JsonIgnore]
    public ReadOnlySpan<byte> Data => RawData.AsSpan();

    public GgdFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public GgdFile(ReadOnlySpan<byte> data)
    {
        RawData = data.ToArray();
        var reader = new SpanBinaryReader(data);
        Header = reader.Read<GgdHeader>();
        if (Header.Magic != GgdMagic)
            throw new InvalidDataException($"Invalid ggd magic 0x{Header.Magic:X8}");
        if (Header.RecordCount > 8)
            throw new InvalidDataException($"Record count {Header.RecordCount} exceeds 8");
        if (Header.Version >= 0x2000500)
        {
            reader.Read<byte>(8).CopyTo(BendParams);
            reader.Read<byte>(8).CopyTo(ScaleParams);
        }

        // Only version 0x2000402 and >= 0x2000700 store the record as-is (100 bytes);
        // 0x2000401/0x2000600 use packed 68-byte and older files 64-byte records
        if (Header.Version < 0x2000700 && Header.Version != 0x2000402)
            throw new NotSupportedException($"Old ggd version 0x{Header.Version:X}, not supported");

        Records = new GgdRecord[Header.RecordCount];
        for (var i = 0; i < Header.RecordCount; i++)
        {
            reader.Seek((int)Header.RecordOffset(i), SeekOrigin.Begin);
            Records[i] = GgdRecord.Read(ref reader);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct GgdHeader
    {
        public uint Magic;          // " dgg"
        public uint Version;
        public ushort RecordCount;  // max 8
        public ushort Mask;         // set to 0x555 when Version <= 0x2000700
        public fixed uint RecordOffsets[8];
        //   blade scale = lerp(ScaleMin.X, ScaleMax.X, rand) * instance.ScaleMul
        //   blade angle = lerp(RotMin.X, RotMax.X, rand) radians
        public Half3 ScaleMin;
        public Half3 ScaleMax;
        public Half3 RotMin;
        public Half3 RotMax;
        public Vector3 BasePosition; // instance positions are relative to this

        public uint RecordOffset(int i) => RecordOffsets[i];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Half3
    {
        public Half X;
        public Half Y;
        public Half Z;

        public override string ToString() => $"<{X}, {Y}, {Z}>";
    }
}

public class GgdRecord
{
    public GgdRecordHeader Header;
    /// <summary>Instances in stream order: blade LOD0, LOD1, LOD2, then model slots 0..31.</summary>
    public required GgdInstance[] Instances;

    public static GgdRecord Read(ref SpanBinaryReader reader)
    {
        var header = reader.Read<GgdRecordHeader>();

        if (header.Magic != GgdFile.RecordMagic)
            throw new InvalidDataException($"Invalid dgs record magic 0x{header.Magic:X8}");
        var instances = reader.Read<GgdInstance>(header.TotalInstances).ToArray();
        return new GgdRecord
        {
            Header = header,
            Instances = instances
        };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct GgdRecordHeader
    {
        public uint Magic;      // "dgs\0"
        public Vector3 AabbMin; // world space
        public Vector3 AabbMax;
        /// <summary>Blade instance count per LOD; blades are 18/12/8 verts for LOD 0/1/2.</summary>
        public fixed ushort LodCounts[3];
        /// <summary>Instance count per model slot; slot index maps into GzdFile.ModelPaths.</summary>
        public fixed ushort ModelCounts[32];
        public ushort Padding;

        public ushort LodCount(int i) => LodCounts[i];
        public ushort ModelCount(int i) => ModelCounts[i];

        public int TotalInstances
        {
            get
            {
                var total = 0;
                for (var i = 0; i < 3; i++) total += LodCounts[i];
                for (var i = 0; i < 32; i++) total += ModelCounts[i];
                return total;
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GgdInstance
{
    public GgdFile.Half3 Position; // relative to GgdHeader.BasePosition
    public Half RotX;       // half4 quaternion
    public Half RotY;
    public Half RotZ;
    public Half RotW;
    public Half ScaleB;
    public Half ScaleA;
    public Half ColorR;     // 0..1, *255 -> vertex color byte
    public Half ColorG;
    public byte TypeIndex;  // grass type 0..7, selects GgdFile.Bend/ScaleParams
    public byte Param;      // /255
    public byte ScaleMul;   // /255, multiplies random blade scale and wind sway
    public byte Flags;      // -> vertex byte 16
}


