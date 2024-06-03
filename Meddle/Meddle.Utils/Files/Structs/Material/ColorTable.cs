using System.Collections;
using System.Numerics;

namespace Meddle.Utils.Files.Structs.Material;

public unsafe struct ColorTable : IEnumerable<ColorTable.Row>
{
    public struct Row
    {
        public const int NumHalves = 32;
        public const int Size      = NumHalves * 2;

        private fixed ushort data[NumHalves];

        public static readonly Row Default;

        public Vector3 Diffuse
        {
            readonly get => new(ToFloat(0), ToFloat(1), ToFloat(2));
            set
            {
                data[0] = FromFloat(value.X);
                data[1] = FromFloat(value.Y);
                data[2] = FromFloat(value.Z);
            }
        }

        public Vector3 Specular
        {
            readonly get => new(ToFloat(4), ToFloat(5), ToFloat(6));
            set
            {
                data[4] = FromFloat(value.X);
                data[5] = FromFloat(value.Y);
                data[6] = FromFloat(value.Z);
            }
        }

        public Vector3 Emissive
        {
            readonly get => new(ToFloat(8), ToFloat(9), ToFloat(10));
            set
            {
                data[8]  = FromFloat(value.X);
                data[9]  = FromFloat(value.Y);
                data[10] = FromFloat(value.Z);
            }
        }

        public Vector2 MaterialRepeat
        {
            readonly get => new(ToFloat(12), ToFloat(15));
            set
            {
                data[12] = FromFloat(value.X);
                data[15] = FromFloat(value.Y);
            }
        }

        public Vector2 MaterialSkew
        {
            readonly get => new(ToFloat(13), ToFloat(14));
            set
            {
                data[13] = FromFloat(value.X);
                data[14] = FromFloat(value.Y);
            }
        }

        public float SpecularStrength
        {
            readonly get => ToFloat(3);
            set => data[3] = FromFloat(value);
        }

        public float GlossStrength
        {
            readonly get => ToFloat(7);
            set => data[7] = FromFloat(value);
        }

        public ushort TileSet
        {
            readonly get => (ushort)(ToFloat(11) * 64f);
            set => data[11] = FromFloat((value + 0.5f) / 64f);
        }

        public readonly Span<Half> AsHalves()
        {
            fixed (ushort* ptr = data)
            {
                return new Span<Half>(ptr, NumHalves);
            }
        }

        private readonly float ToFloat(int idx)
            => (float)BitConverter.UInt16BitsToHalf(data[idx]);

        private static ushort FromFloat(float x)
            => BitConverter.HalfToUInt16Bits((Half)x);
    }

    public const  int  NumRows     = 32;
    private fixed byte rowData[NumRows * Row.Size];

    public ref Row this[int i]
    {
        get
        {
            fixed (byte* ptr = rowData)
            {
                return ref ((Row*)ptr)[i];
            }
        }
    }

    public IEnumerator<Row> GetEnumerator()
    {
        for (var i = 0; i < NumRows; ++i)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public readonly ReadOnlySpan<byte> AsBytes()
    {
        fixed (byte* ptr = rowData)
        {
            return new ReadOnlySpan<byte>(ptr, NumRows * Row.Size);
        }
    }

    public readonly Span<Half> AsHalves()
    {
        fixed (byte* ptr = rowData)
        {
            return new Span<Half>((Half*)ptr, NumRows * 16);
        }
    }

    public void SetDefault()
    {
        for (var i = 0; i < NumRows; ++i)
            this[i] = Row.Default;
    }
    
    internal LegacyColorTable ToLegacy()
    {
        var oldTable = new LegacyColorTable();
        for (var i = 0; i < LegacyColorTable.NumRows; ++i)
        {
            ref readonly var newRow = ref this[i];
            ref var          oldRow = ref oldTable[i];
            oldRow.Diffuse          = newRow.Diffuse;
            oldRow.Specular         = newRow.Specular;
            oldRow.Emissive         = newRow.Emissive;
            oldRow.MaterialRepeat   = newRow.MaterialRepeat;
            oldRow.MaterialSkew     = newRow.MaterialSkew;
            oldRow.SpecularStrength = newRow.SpecularStrength;
            oldRow.GlossStrength    = newRow.GlossStrength;
            oldRow.TileSet          = newRow.TileSet;
        }

        return oldTable;
    }

    internal ColorTable(in LegacyColorTable oldTable)
    {
        for (var i = 0; i < LegacyColorTable.NumRows; ++i)
        {
            ref readonly var oldRow = ref oldTable[i];
            ref var          row    = ref this[i];
            row.Diffuse          = oldRow.Diffuse;
            row.Specular         = oldRow.Specular;
            row.Emissive         = oldRow.Emissive;
            row.MaterialRepeat   = oldRow.MaterialRepeat;
            row.MaterialSkew     = oldRow.MaterialSkew;
            row.SpecularStrength = oldRow.SpecularStrength;
            row.GlossStrength    = oldRow.GlossStrength;
            row.TileSet          = oldRow.TileSet;
        }
    }
}
