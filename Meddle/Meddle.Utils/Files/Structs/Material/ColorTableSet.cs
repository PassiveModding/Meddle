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
    private readonly byte[] buffer;
    public static readonly (int Width, int Height) TextureSize = (8, 16);
    public SkTexture ToTexture()
    {
        var texture = new SkTexture(TextureSize.Width, TextureSize.Height);
        for (int x = 0; x < TextureSize.Width; x++)
        {
            for (int y = 0; y < TextureSize.Height; y++)
            {
                // get exact byte index in the buffer
                const int bytesPerPixel = 8;
                var byteIndex = (y * TextureSize.Width + x) * bytesPerPixel;
                var color = ColorTableUtil.GetColor(byteIndex, buffer);
                texture[x, y] = color;
            }
        }
        return texture;
    }
    public ReadOnlySpan<LegacyColorTableRow> Rows => new(rows);
    public LegacyColorTable(ref SpanBinaryReader reader)
    {
        var pos = reader.Position;
        rows = reader.Read<LegacyColorTableRow>(LegacyNumRows).ToArray();
        buffer = reader.ReadByteString(pos, Size).ToArray();
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

public static class ColorTableUtil
{
    public static SKColor GetColor(int offset, byte[] buf)
    {
        byte r = GetRgbChannelVal(offset, buf);
        byte g = GetRgbChannelVal(offset + 2, buf);
        byte b = GetRgbChannelVal(offset + 4, buf);
        byte a = GetRgbChannelVal(offset + 6, buf);
        return new SKColor(r, g, b, a);
    }

    public static byte GetRgbChannelVal(int offset, byte[] buf)
    {
        byte[] ushortBuf = [ buf[offset], buf[offset + 1] ];
        ushort sVal = BitConverter.ToUInt16(ushortBuf, 0);
        float fVal = (float)BitConverter.UInt16BitsToHalf(sVal);
        return (byte)(fVal * 255f);
    }
}

public readonly struct ColorTable
{
    public const int NumRows = 32;
    public const int Size = NumRows * ColorTableRow.Size;
    private readonly ColorTableRow[] rows;
    private readonly byte[] buffer;
    public static readonly (int Width, int Height) TextureSize = (8, 32);
    public SkTexture ToTexture()
    {
        // row is 64 bytes
        var texture = new SkTexture(TextureSize.Width, TextureSize.Height);
        for (int x = 0; x < TextureSize.Width; x++)
        {
            for (int y = 0; y < TextureSize.Height; y++)
            {
                var byteIndex = (y * TextureSize.Width + x) * 8;
                var color = ColorTableUtil.GetColor(byteIndex, buffer);
                texture[x, y] = color;
            }
        }
        return texture;
    }
    
    public ReadOnlySpan<ColorTableRow> Rows => new(rows);
    public ColorTable(ref SpanBinaryReader reader)
    {
        var pos = reader.Position;
        rows = reader.Read<ColorTableRow>(NumRows).ToArray();
        buffer = reader.ReadByteString(pos, Size).ToArray();
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
}
