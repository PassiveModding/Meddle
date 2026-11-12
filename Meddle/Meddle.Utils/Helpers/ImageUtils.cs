using Meddle.Formats.Files;
using Meddle.SqPack;
using Meddle.Utils.Export;
using Microsoft.Extensions.Logging;
using OtterTex;
using SkiaSharp;

namespace Meddle.Utils.Helpers;

public static class ImageUtils
{
    public static TexDimension ToTexDimension(this TexFile.Attribute attribute)
    {
        var dimension = attribute switch
        {
            TexFile.Attribute.TextureType1D => TexDimension.Tex1D,
            TexFile.Attribute.TextureType2D => TexDimension.Tex2D,
            TexFile.Attribute.TextureType3D => TexDimension.Tex3D,
            TexFile.Attribute.TextureType2DArray => TexDimension.Tex2D,
            TexFile.Attribute.TextureTypeCube => TexDimension.Tex2D,
            _ => throw new NotImplementedException($"Unknown texture dimension: {attribute} [{attribute:X2}]")
        };

        return dimension;
    }

    public static DXGIFormat ToDxgiFormat(this TexFile.TextureFormat format)
    {
        var dxf = format switch
        {
            TexFile.TextureFormat.Unknown => DXGIFormat.Unknown,
            TexFile.TextureFormat.L8_UNORM => DXGIFormat.R8UNorm,
            TexFile.TextureFormat.A8_UNORM => DXGIFormat.A8UNorm,
            TexFile.TextureFormat.R8_UNORM => DXGIFormat.R8UNorm,
            TexFile.TextureFormat.R8_UINT => DXGIFormat.R8UInt,
            TexFile.TextureFormat.R16_UINT => DXGIFormat.R16UInt,
            TexFile.TextureFormat.R32_UINT => DXGIFormat.R32UInt,
            TexFile.TextureFormat.R8G8_UNORM => DXGIFormat.R8G8UNorm,
            TexFile.TextureFormat.B4G4R4A4_UNORM => DXGIFormat.B4G4R4A4UNorm,
            TexFile.TextureFormat.B5G5R5A1_UNORM => DXGIFormat.B5G5R5A1UNorm,
            TexFile.TextureFormat.B8G8R8A8_UNORM => DXGIFormat.B8G8R8A8UNorm,
            TexFile.TextureFormat.B8G8R8X8_UNORM => DXGIFormat.B8G8R8X8UNorm,
            TexFile.TextureFormat.R16F => DXGIFormat.R16Float,
            TexFile.TextureFormat.R32F => DXGIFormat.R32Float,
            TexFile.TextureFormat.R16G16F => DXGIFormat.R16G16Float,
            TexFile.TextureFormat.R32G32F => DXGIFormat.R32G32Float,
            TexFile.TextureFormat.R11G11B10F => DXGIFormat.R11G11B10Float,
            TexFile.TextureFormat.R16G16B16A16F => DXGIFormat.R16G16B16A16Float,
            TexFile.TextureFormat.R32G32B32A32F => DXGIFormat.R32G32B32A32Float,
            TexFile.TextureFormat.BC1_UNORM => DXGIFormat.BC1UNorm,
            TexFile.TextureFormat.BC2_UNORM => DXGIFormat.BC2UNorm,
            TexFile.TextureFormat.BC3_UNORM => DXGIFormat.BC3UNorm,
            TexFile.TextureFormat.D16_UNORM => DXGIFormat.D16UNorm,
            TexFile.TextureFormat.D24_UNORM_S8_UINT => DXGIFormat.D24UNormS8UInt,
            // TexFile.TextureFormat.D16_UNORM_2 => DXGIFormat.D16UNorm,
            // TexFile.TextureFormat.D24_UNORM_S8_UINT_2 => DXGIFormat.D24UNormS8UInt,
            TexFile.TextureFormat.BC4_UNORM => DXGIFormat.BC4UNorm,
            TexFile.TextureFormat.BC5_UNORM => DXGIFormat.BC5UNorm,
            TexFile.TextureFormat.BC6H_SF16 => DXGIFormat.BC6HSF16,
            TexFile.TextureFormat.BC7_UNORM => DXGIFormat.BC7UNorm,
            TexFile.TextureFormat.R16_UNORM => DXGIFormat.R16UNorm,
            TexFile.TextureFormat.R16G16_UNORM => DXGIFormat.R16G16UNorm,
            // TexFile.TextureFormat.R10G10B10A2_UNORM_2 => DXGIFormat.R10G10B10A2UNorm,
            TexFile.TextureFormat.R10G10B10A2_UNORM => DXGIFormat.R10G10B10A2UNorm,
            // TexFile.TextureFormat.D24_UNORM_S8_UINT_3 => DXGIFormat.D24UNormS8UInt,

            _ => throw new NotImplementedException($"Unknown texture format: {format}")
        };

        return dxf;
    }


    public static TextureResource ToResource(this TexFile file)
    {
        var h = file.Header;
        D3DResourceMiscFlags flags = 0;
        if (h.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            flags |= D3DResourceMiscFlags.TextureCube;
        return new TextureResource(
            h.Format.ToDxgiFormat(),
            h.Width,
            h.Height,
            h.CalculatedMips,
            h.CalculatedArraySize,
            h.Type.ToTexDimension(),
            flags,
            file.TextureBuffer);
    }

    public static byte[] RgbaAsPng(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height}, got {rgba.Length}.", nameof(rgba));

        unsafe
        {
            fixed (byte* data = rgba)
            {
                using var bitmap = new SKBitmap();
                var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

                bitmap.InstallPixels(info, (IntPtr)data, width * 4);

                using var str = new SKDynamicMemoryWStream();
                bitmap.Encode(str, SKEncodedImageFormat.Png, 100);

                using var png = str.DetachAsData();
                return png.ToArray();
            }
        }
    }

    public static TexMeta GetTexMeta(TexFile tex)
    {
        D3DResourceMiscFlags miscFlags = 0;
        if (tex.Header.Type == TexFile.Attribute.TextureTypeCube)
        {
            miscFlags = D3DResourceMiscFlags.TextureCube;
        }

        var meta = new TexMeta
        {
            Width = tex.Header.Width,
            Height = tex.Header.Height,
            Depth = tex.Header.Depth,
            MipLevels = tex.Header.CalculatedMips,
            ArraySize = tex.Header.CalculatedArraySize,
            Format = tex.Header.Format.ToDxgiFormat(),
            Dimension = tex.Header.Type.ToTexDimension(),
            MiscFlags = miscFlags,
            MiscFlags2 = 0,
        };

        return meta;
    }

    public static (byte[] Data, int Width, int Height) GetRgbaImage(TexFile tex, int arrayLevel, int mipLevel, int slice)
    {
        var meta = GetTexMeta(tex);
        if (tex.Header.Type is TexFile.Attribute.TextureType2DArray or TexFile.Attribute.TextureTypeCube)
        {
            // workaround due to ffxiv texture array weirdness
            var texSlice = tex.SliceSpan(mipLevel, arrayLevel, out _, out var sliceWidth, out var sliceHeight);
            meta.Width = sliceWidth;
            meta.Height = sliceHeight;
            meta.ArraySize = 1;
            meta.MipLevels = 1;
            if (tex.Header.Type == TexFile.Attribute.TextureTypeCube)
            {
                meta.MiscFlags = 0;
            }

            return ReadRgba(meta, texSlice, 0, 0, false);
        }

        return ReadRgba(meta, tex.TextureBuffer, mipLevel, slice, false);
    }

    private static (byte[] Data, int Width, int Height) ReadRgba(
        TexMeta meta, ReadOnlySpan<byte> source, int mipLevel, int slice, bool trimOversized)
    {
        var si = ScratchImage.Initialize(meta);
        ScratchImage? rgba = null;
        try
        {
            unsafe
            {
                fixed (byte* data = si.Pixels)
                {
                    var dest = new Span<byte>(data, si.Pixels.Length);
                    if (trimOversized && source.Length > dest.Length)
                    {
                        Global.Logger.LogDebug("Data too large for scratch image. {Length} > {Length2} {Width}x{Height} {Format}. Trimmed to fit.",
                                               source.Length, dest.Length, meta.Width, meta.Height, meta.Format);
                        source = source[..dest.Length];
                    }

                    source.CopyTo(dest);
                }
            }

            si.GetRGBA(out var converted);
            rgba = converted;
            var img = rgba.GetImage(mipLevel, 0, slice);
            if (img.Format != DXGIFormat.R8G8B8A8UNorm)
                throw new ArgumentException("Image must be in RGBA format.");

            return (img.Span.ToArray(), (int)img.Width, (int)img.Height);
        }
        finally
        {
            if (rgba != null && !ReferenceEquals(rgba, si)) Release(rgba);
            Release(si);
        }
    }

    private static void Release(ScratchImage image)
    {
        image.Dispose();
        GC.SuppressFinalize(image);
    }


    public static SkTexture ToTexture(this TextureResource resource, (int width, int height)? resize = null)
    {
        using var bitmap = resource.ToBitmap();

        if (resize != null)
        {
            using var resized = bitmap.Resize(new SKImageInfo(resize.Value.width, resize.Value.height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                                   new SKSamplingOptions(SKCubicResampler.Mitchell));
            return new SkTexture(resized);
        }

        return new SkTexture(bitmap);
    }

    public static SKBitmap ToBitmap(this TextureResource resource)
    {
        var (data, width, height) = ReadRgba(FromResource(resource), resource.Data, 0, 0, true);
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        data.CopyTo(bitmap.GetPixelSpan());
        return bitmap;
    }

    public static TexMeta FromResource(TextureResource resource)
    {
        var meta = new TexMeta
        {
            Height = (int)resource.Height,
            Width = (int)resource.Width,
            Depth = 1, // 3D textures would have other values, but we're only handling kernelTexture->D3D11Texture2D
            MipLevels = (int)resource.MipLevels,
            ArraySize = (int)resource.ArraySize,
            Format = resource.Format,
            Dimension = resource.Dimension,
            MiscFlags = resource.MiscFlags.HasFlag(D3DResourceMiscFlags.TextureCube) ? D3DResourceMiscFlags.TextureCube : 0,
            MiscFlags2 = 0,
        };
        
        return meta;
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
