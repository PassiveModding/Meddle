using System.Numerics;

namespace Meddle.Utils.Files.Structs.Material;

public unsafe struct ColorTable
{
    public ushort[] Data;
    public const int LegacyRowSize = 16;
    public const int RowSize = 32;
    public const int LegacyNumRows = 16;
    public const int NumRows = 32;

    public (ColorRow row0, ColorRow row1) GetPair(int weight)
    {
        var weightArr = new byte[] { 
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 
            0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF 
        };
        
        var nearestPair = weightArr.MinBy(v => Math.Abs(v - weight));
        var pairIdx = Array.IndexOf(weightArr, nearestPair) * 2;
        var pair0 = GetRow(pairIdx);
        var pair1 = GetRow(pairIdx + 1);
        
        return (pair0, pair1);
    }

    public ColorRow GetBlendedPair(int weight, int blend)
    {
        var (row1, row0) = GetPair(weight);
        var prioRow = weight < 128 ? row0 : row1;
        var row = new ColorRow
        {
            Diffuse = Vector3.Lerp(row0.Diffuse, row1.Diffuse, blend / 255f),
            Specular = Vector3.Lerp(row0.Specular, row1.Specular, blend / 255f),
            Emissive = Vector3.Lerp(row0.Emissive, row1.Emissive, blend / 255f),
            MaterialRepeat = prioRow.MaterialRepeat,
            MaterialSkew = prioRow.MaterialSkew,
            SpecularStrength = float.Lerp(row0.SpecularStrength, row1.SpecularStrength, blend / 255f),
            GlossStrength = float.Lerp(row0.GlossStrength, row1.GlossStrength, blend / 255f),
            TileSet = prioRow.TileSet
        };
        return row;
    }

    public ref ColorRow GetRow(int idx)
    {
        fixed (ushort* ptr = Data)
        {
            return ref ((ColorRow*)ptr)[idx];
        }
    }
    
    public ref LegacyColorRow GetLegacyRow(int idx)
    {
        fixed (ushort* ptr = Data)
        {
            return ref ((LegacyColorRow*)ptr)[idx];
        }
    }

    public static ColorTable LoadLegacy(ref SpanBinaryReader reader)
    {
        var table = new ColorTable
        {
            Data = reader.Read<ushort>(LegacyRowSize * LegacyNumRows).ToArray()
        };

        return table;
    }

    public static ColorTable DefaultLegacy()
    {
        var table = new ColorTable
        {
            Data = new ushort[LegacyRowSize * LegacyNumRows]
        };

        return table;
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
}

public unsafe struct LegacyColorRow
{
    public fixed ushort Data[ColorTable.LegacyRowSize];

    public Vector3 Diffuse
    {
        readonly get => new(ToFloat(0), ToFloat(1), ToFloat(2));
        set
        {
            Data[0] = FromFloat(value.X);
            Data[1] = FromFloat(value.Y);
            Data[2] = FromFloat(value.Z);
        }
    }

    public Vector3 Specular
    {
        readonly get => new(ToFloat(4), ToFloat(5), ToFloat(6));
        set
        {
            Data[4] = FromFloat(value.X);
            Data[5] = FromFloat(value.Y);
            Data[6] = FromFloat(value.Z);
        }
    }

    public Vector3 Emissive
    {
        readonly get => new(ToFloat(8), ToFloat(9), ToFloat(10));
        set
        {
            Data[8] = FromFloat(value.X);
            Data[9] = FromFloat(value.Y);
            Data[10] = FromFloat(value.Z);
        }
    }

    public Vector2 MaterialRepeat
    {
        readonly get => new(ToFloat(12), ToFloat(15));
        set
        {
            Data[12] = FromFloat(value.X);
            Data[15] = FromFloat(value.Y);
        }
    }

    public Vector2 MaterialSkew
    {
        readonly get => new(ToFloat(13), ToFloat(14));
        set
        {
            Data[13] = FromFloat(value.X);
            Data[14] = FromFloat(value.Y);
        }
    }

    public float SpecularStrength
    {
        readonly get => ToFloat(3);
        set => Data[3] = FromFloat(value);
    }

    public float GlossStrength
    {
        readonly get => ToFloat(7);
        set => Data[7] = FromFloat(value);
    }

    public ushort TileSet
    {
        readonly get => (ushort)(ToFloat(11) * 64f);
        set => Data[11] = FromFloat((value + 0.5f) / 64f);
    }

    private readonly float ToFloat(int idx)
        => (float)BitConverter.UInt16BitsToHalf(Data[idx]);

    private static ushort FromFloat(float x)
        => BitConverter.HalfToUInt16Bits((Half)x);
}

public unsafe struct ColorRow
{
    public fixed ushort Data[ColorTable.RowSize];

    public Vector3 Diffuse
    {
        readonly get => new(ToFloat(0), ToFloat(1), ToFloat(2));
        set
        {
            Data[0] = FromFloat(value.X);
            Data[1] = FromFloat(value.Y);
            Data[2] = FromFloat(value.Z);
        }
    }

    public Vector3 Specular
    {
        readonly get => new(ToFloat(4), ToFloat(5), ToFloat(6));
        set
        {
            Data[4] = FromFloat(value.X);
            Data[5] = FromFloat(value.Y);
            Data[6] = FromFloat(value.Z);
        }
    }

    public Vector3 Emissive
    {
        readonly get => new(ToFloat(8), ToFloat(9), ToFloat(10));
        set
        {
            Data[8] = FromFloat(value.X);
            Data[9] = FromFloat(value.Y);
            Data[10] = FromFloat(value.Z);
        }
    }

    public Vector2 MaterialRepeat
    {
        readonly get => new(ToFloat(12), ToFloat(15));
        set
        {
            Data[12] = FromFloat(value.X);
            Data[15] = FromFloat(value.Y);
        }
    }

    public Vector2 MaterialSkew
    {
        readonly get => new(ToFloat(13), ToFloat(14));
        set
        {
            Data[13] = FromFloat(value.X);
            Data[14] = FromFloat(value.Y);
        }
    }

    public float SpecularStrength
    {
        readonly get => ToFloat(3);
        set => Data[3] = FromFloat(value);
    }

    public float GlossStrength
    {
        readonly get => ToFloat(7);
        set => Data[7] = FromFloat(value);
    }

    public ushort TileSet
    {
        readonly get => (ushort)(ToFloat(11) * 64f);
        set => Data[11] = FromFloat((value + 0.5f) / 64f);
    }

    private readonly float ToFloat(int idx)
        => (float)BitConverter.UInt16BitsToHalf(Data[idx]);

    private static ushort FromFloat(float x)
        => BitConverter.HalfToUInt16Bits((Half)x);
}

public struct TableRow
{
    public int   Stepped;
    public int   Previous;
    public int   Next;
    public float Weight;
        
    public static TableRow GetTableRowIndices(float index)
    {
        var vBase = index * 15f;
        var vOffFilter = (index * 7.5f) % 1.0f;
        var smoothed = float.Lerp(vBase, float.Floor(vBase + 0.5f), vOffFilter * 2);
        var stepped = float.Floor(smoothed + 0.5f);

        return new TableRow
        {
            Stepped  = (int)stepped,
            Previous = (int)MathF.Floor(smoothed),
            Next     = (int)MathF.Ceiling(smoothed),
            Weight   = smoothed % 1,
        };
    }
}
