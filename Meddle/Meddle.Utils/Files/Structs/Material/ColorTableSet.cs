using System.Numerics;
using System.Runtime.CompilerServices;
using SkiaSharp;

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
    public const int Size = LegacyNumRows * LegacyColorTableRow.Size;
    private readonly LegacyColorTableRow[] rows;
    private readonly ShortVec4[][] buffer; 
    public static readonly (int Width, int Height) TextureSize = (8, 16);
    public SkTexture ToTexture()
    {
        var texture = new SkTexture(TextureSize.Width, TextureSize.Height);
        for (int x = 0; x < TextureSize.Width; x++)
        {
            for (int y = 0; y < TextureSize.Height; y++)
            {
                texture[x, y] = buffer[y][x].ToSkColor();
            }
        }
        return texture;
    }
    public ReadOnlySpan<LegacyColorTableRow> Rows => new(rows);
    public LegacyColorTable(ref SpanBinaryReader reader)
    {
        var pos = reader.Position;
        rows = reader.Read<LegacyColorTableRow>(LegacyNumRows).ToArray();
        reader.Seek(pos, SeekOrigin.Begin);
        buffer = new ShortVec4[LegacyNumRows][];
        reader.Seek(pos, SeekOrigin.Begin);
        const int itemsPerRow = LegacyColorTableRow.Size / ShortVec4.Size;
        for (int i = 0; i < LegacyNumRows; i++)
        {
            var rowBuffer = reader.Read<ShortVec4>(itemsPerRow).ToArray();
            buffer[i] = rowBuffer;
        }
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
}

public readonly struct ColorTable
{
    public const int NumRows = 32;
    public const int Size = NumRows * ColorTableRow.Size;
    private readonly ColorTableRow[] rows;
    private readonly ShortVec4[][] buffer;
    public static readonly (int Width, int Height) TextureSize = (8, 32);
    
    public SkTexture ToTexture()
    {
        var texture = new SkTexture(TextureSize.Width, TextureSize.Height);
        for (int x = 0; x < TextureSize.Width; x++)
        {
            for (int y = 0; y < TextureSize.Height; y++)
            {
                texture[x, y] = buffer[y][x].ToSkColor();
            }
        }
        return texture;
    }
    
    public ReadOnlySpan<ColorTableRow> Rows => new(rows);
    public ColorTable(ref SpanBinaryReader reader)
    {
        var pos = reader.Position;
        rows = reader.Read<ColorTableRow>(NumRows).ToArray();
        buffer = new ShortVec4[NumRows][];
        reader.Seek(pos, SeekOrigin.Begin);
        const int itemsPerRow = ColorTableRow.Size / ShortVec4.Size;
        for (int i = 0; i < NumRows; i++)
        {
            var rowBuffer = reader.Read<ShortVec4>(itemsPerRow).ToArray();
            buffer[i] = rowBuffer;
        }
    }
    
    public object ToObject()
    {
        return new
        {
            Rows = Rows.ToArray().Select(r => new
            {
                r.Diffuse, r.GlossStrength, r.Scalar3,
                r.Specular, r.SpecularStrength, r.Scalar7,
                r.Emissive, r.Scalar11,
                r.SheenRate, r.SheenTint, r.SheenAptitude, r.Scalar15,
                r.Roughness, r.Scalar17, r.Metalness, r.Anisotropy,
                r.Scalar20, r.SphereMask, r.Scalar22, r.Scalar23,
                r.ShaderId, r.TileIndex, r.TileAlpha, r.SphereIndex,
                r.TileMatrix
            }).ToArray()
        };
    }
}
