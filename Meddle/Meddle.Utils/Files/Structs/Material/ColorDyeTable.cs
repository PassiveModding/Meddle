using System.Collections;

namespace Meddle.Utils.Files.Structs.Material;

public unsafe struct ColorDyeTable : IEnumerable<ColorDyeTable.Row>
{
    public struct Row
    {
        public const int  Size = 4;
        private      uint _data;

        public ushort Template
        {
            get => (ushort)(_data >> 5);
            set => _data = (_data & 0x1Fu) | ((uint)value << 5);
        }

        public bool Diffuse
        {
            get => (_data & 0x01) != 0;
            set => _data = value ? _data | 0x01u : _data & 0xFFFEu;
        }

        public bool Specular
        {
            get => (_data & 0x02) != 0;
            set => _data = value ? _data | 0x02u : _data & 0xFFFDu;
        }

        public bool Emissive
        {
            get => (_data & 0x04) != 0;
            set => _data = value ? _data | 0x04u : _data & 0xFFFBu;
        }

        public bool Gloss
        {
            get => (_data & 0x08) != 0;
            set => _data = value ? _data | 0x08u : _data & 0xFFF7u;
        }

        public bool SpecularStrength
        {
            get => (_data & 0x10) != 0;
            set => _data = value ? _data | 0x10u : _data & 0xFFEFu;
        }
    }

    public const  int  NumRows = 32;
    private fixed uint _rowData[NumRows];

    public ref Row this[int i]
    {
        get
        {
            fixed (uint* ptr = _rowData)
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

    public ReadOnlySpan<byte> AsBytes()
    {
        fixed (uint* ptr = _rowData)
        {
            return new ReadOnlySpan<byte>(ptr, NumRows * sizeof(ushort));
        }
    }

    internal ColorDyeTable(in LegacyColorDyeTable oldTable)
    {
        for (var i = 0; i < LegacyColorDyeTable.NumRows; ++i)
        {
            var     oldRow = oldTable[i];
            ref var row    = ref this[i];
            row.Template         = oldRow.Template;
            row.Diffuse          = oldRow.Diffuse;
            row.Specular         = oldRow.Specular;
            row.Emissive         = oldRow.Emissive;
            row.Gloss            = oldRow.Gloss;
            row.SpecularStrength = oldRow.SpecularStrength;
        }
    }
}
