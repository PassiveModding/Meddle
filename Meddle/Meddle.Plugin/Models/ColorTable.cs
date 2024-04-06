using System.Collections;
using System.Numerics;

namespace Meddle.Plugin.Models;

// https://github.com/Ottermandias/Penumbra.GameData/blob/45679aa32cc37b59f5eeb7cf6bf5a3ea36c626e0/Files/MtrlFile.ColorTable.cs
public unsafe struct ColorTable : IEnumerable<ColorTable.Row>
{
    public ColorTable(Half[] data)
    {
        fixed (byte* ptr = rowData)
        {
            for (var i = 0; i < NumRows; ++i)
            {
                var row = new Row();
                var span = row.AsHalves();
                for (var j = 0; j < 16; ++j)
                    span[j] = data[i * 16 + j];
                ((Row*)ptr)[i] = row;
            }
        }
    }

    public ColorTable()
    {
        for (var i = 0; i < NumRows; ++i)
            this[i] = Row.Default;
    }

    public const int NumRows = 16;
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
    {
        return GetEnumerator();
    }

    public unsafe struct Row
    {
        public const int Size = 32;
        private fixed ushort data[16];

        public static readonly Row Default = new()
        {
            Diffuse          = Vector3.One,
            Specular         = Vector3.One,
            SpecularStrength = 1.0f,
            Emissive         = Vector3.Zero,
            GlossStrength    = 20.0f,
            TileSet          = 0,
            MaterialRepeat   = new Vector2(16.0f),
            MaterialSkew     = Vector2.Zero,
        };

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
                data[8] = FromFloat(value.X);
                data[9] = FromFloat(value.Y);
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
                return new Span<Half>(ptr, 16);
            }
        }

        private readonly float ToFloat(int idx)
            => (float)BitConverter.UInt16BitsToHalf(data[idx]);

        private static ushort FromFloat(float x)
            => BitConverter.HalfToUInt16Bits((Half)x);
    }
}
