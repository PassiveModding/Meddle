using System.Collections;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumina.Data.Files;
using Meddle.Plugin.Utility;
using CSTexture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Meddle.Plugin.Models;

// https://github.com/Ottermandias/Penumbra.GameData/blob/45679aa32cc37b59f5eeb7cf6bf5a3ea36c626e0/Files/MtrlFile.ColorTable.cs
public unsafe struct ColorTable : IEnumerable<ColorTable.Row>
{
    public ColorTable(CSTexture* colorTable)
    {
        var data = DXHelper.ExportTextureResource(colorTable);
        if ((TexFile.TextureFormat)colorTable->TextureFormat != TexFile.TextureFormat.R16G16B16A16F)
            throw new ArgumentException($"Color table is not R16G16B16A16F ({(TexFile.TextureFormat)colorTable->TextureFormat})");
        if (colorTable->Width != 4 || colorTable->Height != 16)
            throw new ArgumentException($"Color table is not 4x16 ({colorTable->Width}x{colorTable->Height})");

        var stridedData = TextureHelper.AdjustStride(data.Stride, (int)colorTable->Width * 8, (int)colorTable->Height, data.Data);
        
        var table = MemoryMarshal.Cast<byte, ushort>(stridedData.AsSpan()).ToArray();
        this = FromData(table);
    }

    public ColorTable(ushort[] data)
    {
        this = FromData(data);
    }

    public ColorTable()
    {
        for (var i = 0; i < NumRows; ++i)
            this[i] = Row.Default;
    }
    
    private static ColorTable FromData(ushort[] data)
    {
        if (data.Length != NumRows * 16)
            throw new ArgumentException($"Color table is not 16 rows of 16 shorts ({data.Length})");

        var table = new ColorTable();
        for (var i = 0; i < NumRows; ++i)
        {
            var row = new ushort[16];
            for (var j = 0; j < 16; ++j)
                row[j] = data[i * 16 + j];
            table[i] = new Row(row);
        }
        
        return table;
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
    
    public (Row prev, Row next, TableRow row) Lookup(float index)
    {
        var row = TableRow.GetTableRowIndices(index);
        return (this[row.Previous], this[row.Next], row);
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
    
    public struct Row
    {
        public const int Size = 32;
        private fixed ushort data[16];
        
        public Row(ushort[] data)
        {
            for (var i = 0; i < 16; ++i)
                this.data[i] = data[i];
        }

        public static readonly Row Default = new()
        {
            Diffuse          = Vector3.One,
            FresnelValue0         = Vector3.One,
            SpecularMask = 1.0f,
            Emissive         = Vector3.Zero,
            Shininess    = 20.0f,
            TileW          = 0,
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
        
        public float SpecularMask
        {
            readonly get => ToFloat(3);
            set => data[3] = FromFloat(value);
        }
        
        public Vector3 FresnelValue0
        {
            readonly get => new(ToFloat(4), ToFloat(5), ToFloat(6));
            set
            {
                data[4] = FromFloat(value.X);
                data[5] = FromFloat(value.Y);
                data[6] = FromFloat(value.Z);
            }
        }
        
        public float Shininess
        {
            readonly get => ToFloat(7);
            set => data[7] = FromFloat(value);
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
        
        public ushort TileW
        {
            readonly get => (ushort)(ToFloat(11) * 64f);
            set => data[11] = FromFloat((value + 0.5f) / 64f);
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
