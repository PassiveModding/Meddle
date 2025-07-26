using System.Numerics;

namespace Meddle.Utils.Files.Structs.Material;

public interface IColorTableSet;

public struct ColorTableSet : IColorTableSet
{
    public ColorTable ColorTable;
    public ColorDyeTable? ColorDyeTable;
    
    public object ToObject()
    {
        return new
        {
            ColorTable = ColorTable.ToObject(),
            ColorDyeTable = ColorDyeTable?.ToObject()
        };
    }
}

public struct LegacyColorTableSet : IColorTableSet
{
    public LegacyColorTable ColorTable;
    public LegacyColorDyeTable? ColorDyeTable;
    
    public object ToObject()
    {
        return new
        {
            ColorTable = ColorTable.ToObject(),
            ColorDyeTable = ColorDyeTable?.ToObject()
        };
    }
}

public readonly struct LegacyColorDyeTable
{
    private readonly LegacyColorDyeTableRow[] rows;
    public ReadOnlySpan<LegacyColorDyeTableRow> Rows => new(rows);

    public LegacyColorDyeTable(ref SpanBinaryReader reader)
    {
        rows = reader.Read<LegacyColorDyeTableRow>(LegacyColorTable.LegacyNumRows).ToArray();
    }
    
    public object ToObject()
    {
        return new
        {
            Rows = Rows.ToArray().Select(r => new
            {
                Template = r.Template,
                Diffuse = r.DiffuseColor,
                Specular = r.SpecularColor,
                Emissive = r.EmissiveColor,
                Gloss = r.Shininess,
                SpecularStrength = r.SpecularMask,
                
            }).ToArray()
        };
    }
}

public readonly struct ColorDyeTable
{
    private readonly ColorDyeTableRow[] rows;
    public ReadOnlySpan<ColorDyeTableRow> Rows => new(rows);

    public ColorDyeTable(ref SpanBinaryReader reader)
    {
        rows = reader.Read<ColorDyeTableRow>(ColorTable.NumRows).ToArray();
    }

    public object ToObject()
    {
        return new
        {
            Rows = Rows.ToArray().Select(r => new
            {
                Template = r.Template,
                Channel = r.Channel,
                Diffuse = r.DiffuseColor,
                Specular = r.SpecularColor,
                Emissive = r.EmissiveColor,
                Scalar3 = r.Scalar3,
                Metalness = r.Metalness,
                Roughness = r.Roughness,
                SheenRate = r.SheenRate,
                SheenTintRate = r.SheenTintRate,
                SheenAperture = r.SheenAperture,
                Anisotropy = r.Anisotropy,
                SphereMapIndex = r.SphereMapIndex,
                SphereMapMask = r.SphereMapMask,
            }).ToArray()
        };
    }
}


public readonly struct LegacyColorTable
{
    public const int LegacyNumRows = 16;
    private readonly LegacyColorTableRow[] rows;
    public ReadOnlySpan<LegacyColorTableRow> Rows => new(rows);
    public LegacyColorTable(ref SpanBinaryReader reader)
    {
        rows = reader.Read<LegacyColorTableRow>(LegacyNumRows).ToArray();
    }
    
    public object ToObject()
    {
        return new
        {
            Rows = Rows.ToArray().Select(r => new
            {
                Diffuse = r.Diffuse,
                Specular = r.Specular,
                SpecularStrength = r.SpecularStrength,
                Emissive = r.Emissive,
                GlossStrength = r.GlossStrength,
                MaterialRepeat = r.MaterialRepeat,
                MaterialSkew = r.MaterialSkew,
                TileIndex = r.TileIndex
            }).ToArray()
        };
    }
    
    // normal pixel A channel on legacy normal as a float from 0-1
    public LegacyColorTableRow GetBlendedPair(float normalPixelW)
    {
        var indices = TableRow.GetTableRowIndices(normalPixelW);
        var row0 = Rows[indices.Previous];
        var row1 = Rows[indices.Next];
        var stepped = Rows[indices.Stepped];

        return new LegacyColorTableRow
        {
            Diffuse = Vector3.Clamp(Vector3.Lerp(row0.Diffuse, row1.Diffuse, indices.Weight), Vector3.Zero, Vector3.One),
            Specular = Vector3.Clamp(Vector3.Lerp(row0.Specular, row1.Specular, indices.Weight), Vector3.Zero, Vector3.One),
            SpecularStrength = float.Lerp(row0.SpecularStrength, row1.SpecularStrength, indices.Weight),
            Emissive = Vector3.Clamp(Vector3.Lerp(row0.Emissive, row1.Emissive, indices.Weight), Vector3.Zero, Vector3.One),
            GlossStrength = float.Lerp(row0.GlossStrength, row1.GlossStrength, indices.Weight),
            MaterialRepeat = stepped.MaterialRepeat,
            MaterialSkew = stepped.MaterialSkew,
            TileIndex = stepped.TileIndex
        };
    }
    
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
}

public readonly struct ColorTable
{
    public const int NumRows = 32;
    private readonly ColorTableRow[] rows;
    public ReadOnlySpan<ColorTableRow> Rows => new(rows);
    public ColorTable(ref SpanBinaryReader reader)
    {
        rows = reader.Read<ColorTableRow>(NumRows).ToArray();
    }

    public (ColorTableRow row0, ColorTableRow row1) GetPair(int weight)
    {
        var weightArr = new byte[]
        {
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
            0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF
        };

        var nearestPair = weightArr.MinBy(v => Math.Abs(v - weight));
        var pairIdx = Array.IndexOf(weightArr, nearestPair) * 2;
        var pair0 = rows[pairIdx];
        var pair1 = rows[pairIdx + 1];

        return (pair0, pair1);
    }
    
    public object ToObject()
    {
        return new
        {
            Rows = Rows.ToArray().Select(r => new
            {
                r.Diffuse,
                r.Specular,
                r.Emissive,
                r.SheenRate,
                r.SheenTint,
                r.SheenAptitude,
                r.Roughness,
                r.Metalness,
                r.Anisotropy,
                r.SphereMask,
                r.ShaderId,
                r.TileIndex,
                r.TileAlpha,
                r.SphereIndex,
                r.TileMatrix,
                r.GlossStrength,
                r.SpecularStrength,
            }).ToArray()
        };
    }
    
    // public ColorTableRow GetBlendedPair(int weight, int blend)
    // {
    //     var (row0, row1) = GetPair(weight);
    //     var prioRow = weight < 128 ? row1 : row0;
    //
    //     var blendAmount = blend / 255f;
    //     var row = new ColorTableRow
    //     {
    //         Diffuse = Vector3.Clamp(Vector3.Lerp(row1.Diffuse, row0.Diffuse, blendAmount), Vector3.Zero, Vector3.One),
    //         Specular = Vector3.Clamp(Vector3.Lerp(row1.Specular, row0.Specular, blendAmount), Vector3.Zero, Vector3.One),
    //         Emissive = Vector3.Clamp(Vector3.Lerp(row1.Emissive, row0.Emissive, blendAmount), Vector3.Zero, Vector3.One),
    //         SheenRate = float.Lerp(row1.SheenRate, row0.SheenRate, blendAmount),
    //         SheenTint = float.Lerp(row1.SheenTint, row0.SheenTint, blendAmount),
    //         SheenAptitude = float.Lerp(row1.SheenAptitude, row0.SheenAptitude, blendAmount),
    //         Roughness = float.Lerp(row1.Roughness, row0.Roughness, blendAmount),
    //         Metalness = float.Lerp(row1.Metalness, row0.Metalness, blendAmount),
    //         Anisotropy = float.Lerp(row1.Anisotropy, row0.Anisotropy, blendAmount),
    //         SphereMask = float.Lerp(row1.SphereMask, row0.SphereMask, blendAmount),
    //         GlossStrength = float.Lerp(row1.GlossStrength, row0.GlossStrength, blendAmount),
    //         SpecularStrength = float.Lerp(row1.SpecularStrength, row0.SpecularStrength, blendAmount),
    //         
    //         TileIndex = prioRow.TileIndex
    //     };
    //
    //     return row;
    // }
}
