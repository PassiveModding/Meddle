using BCnEncoder.Decoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using SixLabors.ImageSharp;
using SkiaSharp;
using Buffer = System.Buffer;

namespace Meddle.Utils.Helpers;

public static class ImageUtils
{
    public static TextureResource ToResource(this TexFile file)
    {
        var h = file.Header;
        return new TextureResource(
            TexFile.GetDxgiFormatFromTextureFormat(h.Format),
            h.Width,
            h.Height,
            h.CalculatedMips,
            h.CalculatedArraySize,
            TexFile.GetTexDimensionFromAttribute(h.Type),
            h.Type.HasFlag(TexFile.Attribute.TextureTypeCube),
            file.TextureBuffer);
    }

    public static SKBitmap GetTexData(TexFile tex, int arrayLevel, int mipLevel, int slice)
    {
        // var meta = GetTexMeta(tex);
        // ScratchImage si;
        // Image img;
        // if (tex.Header.Type == TexFile.Attribute.TextureType2DArray)
        // {
        //     // workaround due to ffxiv texture array weirdness
        //     var texSlice = tex.SliceSpan(mipLevel, arrayLevel, out var sliceSize, out var sliceWidth,
        //                                  out var sliceHeight);
        //     meta.Width = sliceWidth;
        //     meta.Height = sliceHeight;
        //     meta.ArraySize = 1;
        //     meta.MipLevels = 1;
        //
        //     si = ScratchImage.Initialize(meta);
        //     unsafe
        //     {
        //         fixed (byte* data = si.Pixels)
        //         {
        //             var span = new Span<byte>(data, si.Pixels.Length);
        //             texSlice.CopyTo(span);
        //         }
        //     }
        //
        //     si.GetRGBA(out var rgba);
        //     img = rgba.GetImage(0, 0, 0);
        // }
        // else if (tex.Header.Type == TexFile.Attribute.TextureTypeCube)
        // {
        //     meta.ArraySize = 6;
        //     meta.MiscFlags = D3DResourceMiscFlags.TextureCube;
        //
        //     si = ScratchImage.Initialize(meta);
        //     unsafe
        //     {
        //         fixed (byte* data = si.Pixels)
        //         {
        //             var span = new Span<byte>(data, si.Pixels.Length);
        //             tex.TextureBuffer.CopyTo(span);
        //         }
        //     }
        //
        //     si.GetRGBA(out var rgba);
        //     img = rgba.GetImage(0, arrayLevel, 0);
        // }
        // else
        // {
        //     si = ScratchImage.Initialize(meta);
        //     unsafe
        //     {
        //         fixed (byte* data = si.Pixels)
        //         {
        //             var span = new Span<byte>(data, si.Pixels.Length);
        //             tex.TextureBuffer.CopyTo(span);
        //         }
        //     }
        //
        //     si.GetRGBA(out var rgba);
        //     img = rgba.GetImage(mipLevel, 0, slice);
        // }
        //
        // return img;
        var resource = tex.ToResource();
        if (resource is {ArraySize: > 1, IsCube: false})
        {
            var texSlice = tex.SliceSpan(mipLevel, arrayLevel, out var sliceSize, out var sliceWidth,
                                         out var sliceHeight);
            
            var singleResource = new TextureResource(
                resource.Format,
                (uint)sliceWidth,
                (uint)sliceHeight,
                1,
                1,
                resource.Dimension,
                false,
                texSlice.ToArray());
            resource = singleResource;
        }
        else if (resource.IsCube)
        {
            throw new NotImplementedException("Cube map extraction not implemented yet.");
        }
        
        var bitmap = resource.ToBitmap();
        return bitmap;
    }

    public static byte[] ImageAsPng(this SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    
    public static byte[] GetRawRgbaData(TexFile tex, int arrayLevel, int mipLevel, int slice)
    {
        var img = GetTexData(tex, arrayLevel, mipLevel, slice);
        var pixels = img.GetPixels();
        var data = new byte[img.Width * img.Height * 4];
        unsafe
        {
            var span = new Span<byte>((void*)pixels, data.Length);
            span.CopyTo(data);
        }
        return data;
    }

    public static SkTexture ToTexture(this TextureResource resource)
    {
        var bitmap = resource.ToBitmap();
        return new SkTexture(bitmap);
    }

    public static unsafe SKBitmap ToBitmap(this TextureResource resource)
    {
        var decoder = new BcDecoder();
        using var dataStream = new MemoryStream(resource.Data);

        ColorRgba32[] image;
        if (resource.Format.IsCompressedFormat())
        {
            image = decoder.DecodeRaw(dataStream, (int)resource.Width, (int)resource.Height, resource.Format.ToCompressionFormat());
        }
        else
        {
            throw new NotImplementedException("Uncompressed format decoding not implemented yet.");
        }
        var bitmap = new SKBitmap((int)resource.Width, (int)resource.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = bitmap.GetPixels();
        var span = new Span<byte>((void*)pixels, bitmap.RowBytes * bitmap.Height);
        for (var y = 0; y < resource.Height; y++)
        {
            for (var x = 0; x < resource.Width; x++)
            {
                var color = image[y * resource.Width + x];
                var index = y * bitmap.RowBytes + x * 4;
                span[index] = color.r;
                span[index + 1] = color.g;
                span[index + 2] = color.b;
                span[index + 3] = color.a;
            }
        }
        return bitmap;
    }

    public static CompressionFormat ToCompressionFormat(this DxgiFormat format)
    {
        return format switch
        {
            DxgiFormat.DxgiFormatBc1Typeless or
            DxgiFormat.DxgiFormatBc1Unorm or
            DxgiFormat.DxgiFormatBc1UnormSrgb => CompressionFormat.Bc1,
            DxgiFormat.DxgiFormatBc2Typeless or
            DxgiFormat.DxgiFormatBc2Unorm or
            DxgiFormat.DxgiFormatBc2UnormSrgb => CompressionFormat.Bc2,
            DxgiFormat.DxgiFormatBc3Typeless or
            DxgiFormat.DxgiFormatBc3Unorm or
            DxgiFormat.DxgiFormatBc3UnormSrgb => CompressionFormat.Bc3,
            DxgiFormat.DxgiFormatBc4Typeless or
            DxgiFormat.DxgiFormatBc4Unorm or
            DxgiFormat.DxgiFormatBc4Snorm => CompressionFormat.Bc4, 
            DxgiFormat.DxgiFormatBc5Typeless or
            DxgiFormat.DxgiFormatBc5Unorm or
            DxgiFormat.DxgiFormatBc5Snorm => CompressionFormat.Bc5,
            DxgiFormat.DxgiFormatBc6HTypeless or
            DxgiFormat.DxgiFormatBc6HUf16 => CompressionFormat.Bc6U,
            DxgiFormat.DxgiFormatBc6HSf16 => CompressionFormat.Bc6S,
            DxgiFormat.DxgiFormatBc7Typeless or
            DxgiFormat.DxgiFormatBc7Unorm or
            DxgiFormat.DxgiFormatBc7UnormSrgb => CompressionFormat.Bc7,
            DxgiFormat.DxgiFormatAtcExt => CompressionFormat.Atc,
            DxgiFormat.DxgiFormatAtcExplicitAlphaExt => CompressionFormat.AtcExplicitAlpha,
            DxgiFormat.DxgiFormatAtcInterpolatedAlphaExt => CompressionFormat.AtcInterpolatedAlpha,

            _ => throw new NotSupportedException($"Unsupported format: {format}"),
        };
    }
    
    public static byte[] AdjustStride(int oldStride, int newStride, int height, byte[] data)
    {
        if (data.Length != oldStride * height)
            throw new ArgumentException("Data length must match stride * height.", nameof(data));

        if (oldStride == newStride)
            return data;
        if (oldStride < newStride)
            throw new ArgumentException("New stride must be smaller than old stride.", nameof(newStride));

        var newData = new byte[newStride * height];
        for (var y = 0; y < height; ++y)
            Buffer.BlockCopy(data, y * oldStride, newData, y * newStride, newStride);
        return newData;
    }
}
