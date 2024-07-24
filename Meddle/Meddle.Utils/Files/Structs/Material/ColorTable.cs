using System.Numerics;

namespace Meddle.Utils.Files.Structs.Material;

public unsafe struct ColorTable
{
    public ushort[] Data;
    public const int RowSize = 32;
    public const int NumRows = 32;
    public const int LegacyRowSize = 16;
    public const int LegacyNumRows = 16;

    public (ColorTableRow row0, ColorTableRow row1) GetPair(int weight)
    {
        var weightArr = new byte[]
        {
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
            0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF
        };

        var nearestPair = weightArr.MinBy(v => Math.Abs(v - weight));
        var pairIdx = Array.IndexOf(weightArr, nearestPair) * 2;
        var pair0 = GetRow(pairIdx);
        var pair1 = GetRow(pairIdx + 1);

        return (pair0, pair1);
    }

    public ColorTableRow GetBlendedPair(int weight, int blend)
    {
        var (row0, row1) = GetPair(weight);
        var prioRow = weight < 128 ? row1 : row0;

        var blendAmount = blend / 255f;
        var row = new ColorTableRow
        {
            Diffuse = Vector3.Clamp(Vector3.Lerp(row1.Diffuse, row0.Diffuse, blendAmount), Vector3.Zero, Vector3.One),
            Specular =
                Vector3.Clamp(Vector3.Lerp(row1.Specular, row0.Specular, blendAmount), Vector3.Zero, Vector3.One),
            Emissive =
                Vector3.Clamp(Vector3.Lerp(row1.Emissive, row0.Emissive, blendAmount), Vector3.Zero, Vector3.One),
            MaterialRepeat = prioRow.MaterialRepeat,
            MaterialSkew = prioRow.MaterialSkew,
            SpecularStrength = float.Clamp(float.Lerp(row1.SpecularStrength, row0.SpecularStrength, blendAmount), 0, 1),
            GlossStrength = float.Clamp(float.Lerp(row1.GlossStrength, row0.GlossStrength, blendAmount), 0, 1),
            TileIndex = prioRow.TileIndex
        };

        return row;
    }

    public ref ColorTableRow GetRow(int idx)
    {
        fixed (ushort* ptr = Data)
        {
            return ref ((ColorTableRow*)ptr)[idx];
        }
    }

    public static ColorTable Load(ref SpanBinaryReader reader)
    {
        var table = new ColorTable
        {
            Data = reader.Read<ushort>(RowSize * NumRows).ToArray()
        };

        return table;
    }

    public static ColorTable Default()
    {
        var table = new ColorTable
        {
            Data = new ushort[RowSize * NumRows]
        };

        return table;
    }

    public ColorTable LoadLegacy(ref SpanBinaryReader dataSetReader)
    {
        var buf = dataSetReader.Read<ushort>(LegacyRowSize * LegacyNumRows);
        var table = new ColorTable
        {
            Data = new ushort[RowSize * NumRows]
        };

        for (var i = 0; i < buf.Length; i++)
        {
            table.Data[i] = buf[i];
        }

        return table;
    }

    public ColorTable DefaultLegacy()
    {
        var table = new ColorTable
        {
            Data = new ushort[RowSize * NumRows]
        };

        return table;
    }
}

// Old Color Table blending
public struct TableRow
{
    public int Stepped;
    public int Previous;
    public int Next;
    public float Weight;

    public static TableRow GetTableRowIndices(float index)
    {
        var vBase = index * 15f;
        var vOffFilter = index * 7.5f % 1.0f;
        var smoothed = float.Lerp(vBase, float.Floor(vBase + 0.5f), vOffFilter * 2);
        var stepped = float.Floor(smoothed + 0.5f);

        return new TableRow
        {
            Stepped = (int)stepped,
            Previous = (int)MathF.Floor(smoothed),
            Next = (int)MathF.Ceiling(smoothed),
            Weight = smoothed % 1
        };
    }
}
