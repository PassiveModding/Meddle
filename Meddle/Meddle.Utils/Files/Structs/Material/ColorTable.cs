using System.Collections;
using System.Numerics;

namespace Meddle.Utils.Files.Structs.Material;

public unsafe struct ColorTable : IEnumerable<ColorTable.Row>
{
    public struct Row
    {
        public const int NumHalves = 32;
        public const int Size      = NumHalves * 2;

        private fixed ushort _data[NumHalves];

        public static readonly Row Default = new();

        public Vector3 Diffuse
        {
            readonly get => new(ToFloat(0), ToFloat(1), ToFloat(2));
            set
            {
                _data[0] = FromFloat(value.X);
                _data[1] = FromFloat(value.Y);
                _data[2] = FromFloat(value.Z);
            }
        }

        public Vector3 Specular
        {
            readonly get => new(ToFloat(4), ToFloat(5), ToFloat(6));
            set
            {
                _data[4] = FromFloat(value.X);
                _data[5] = FromFloat(value.Y);
                _data[6] = FromFloat(value.Z);
            }
        }

        public Vector3 Emissive
        {
            readonly get => new(ToFloat(8), ToFloat(9), ToFloat(10));
            set
            {
                _data[8]  = FromFloat(value.X);
                _data[9]  = FromFloat(value.Y);
                _data[10] = FromFloat(value.Z);
            }
        }

        public Vector2 MaterialRepeat
        {
            readonly get => new(ToFloat(12), ToFloat(15));
            set
            {
                _data[12] = FromFloat(value.X);
                _data[15] = FromFloat(value.Y);
            }
        }

        public Vector2 MaterialSkew
        {
            readonly get => new(ToFloat(13), ToFloat(14));
            set
            {
                _data[13] = FromFloat(value.X);
                _data[14] = FromFloat(value.Y);
            }
        }

        public float SpecularStrength
        {
            readonly get => ToFloat(3);
            set => _data[3] = FromFloat(value);
        }

        public float GlossStrength
        {
            readonly get => ToFloat(7);
            set => _data[7] = FromFloat(value);
        }

        public ushort TileSet
        {
            readonly get => (ushort)(ToFloat(11) * 64f);
            set => _data[11] = FromFloat((value + 0.5f) / 64f);
        }

        public readonly Span<Half> AsHalves()
        {
            fixed (ushort* ptr = _data)
            {
                return new Span<Half>(ptr, NumHalves);
            }
        }

        private readonly float ToFloat(int idx)
            => (float)BitConverter.UInt16BitsToHalf(_data[idx]);

        private static ushort FromFloat(float x)
            => BitConverter.HalfToUInt16Bits((Half)x);
    }

    public const  int  NumUsedRows = 16;
    public const  int  NumRows     = 32;
    private fixed byte _rowData[NumRows * Row.Size];

    public ref Row this[int i]
    {
        get
        {
            fixed (byte* ptr = _rowData)
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
        fixed (byte* ptr = _rowData)
        {
            return new ReadOnlySpan<byte>(ptr, NumRows * Row.Size);
        }
    }

    public readonly Span<Half> AsHalves()
    {
        fixed (byte* ptr = _rowData)
        {
            return new Span<Half>((Half*)ptr, NumRows * 16);
        }
    }

    public void SetDefault()
    {
        for (var i = 0; i < NumRows; ++i)
            this[i] = Row.Default;
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
