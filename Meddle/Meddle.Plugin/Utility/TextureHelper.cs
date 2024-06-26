using Meddle.Plugin.Models;
using SkiaSharp;
using OtterTex;

namespace Meddle.Plugin.Utility;

public static class TextureHelper
{
    public readonly struct TextureResource(DXGIFormat format, int width, int height, int stride, int mipLevels, int arraySize, TexDimension dimension, D3DResourceMiscFlags miscFlags, byte[] data)
    {
        public DXGIFormat Format { get; init; } = format;
        public int Width { get; init; } = width;
        public int Height { get; init; } = height;
        public int Stride { get; init; } = stride;
        public int MipLevels { get; init; } = mipLevels;
        public int ArraySize { get; init; } = arraySize;
        public TexDimension Dimension { get; init; } = dimension;
        public D3DResourceMiscFlags MiscFlags { get; init; } = miscFlags;
        public byte[] Data { get; init; } = data;

        public SKTexture ToTexture((int width, int height)? resize = null)
        {
            var bitmap = ToBitmap(this);
            if (resize != null)
            {
                bitmap = bitmap.Resize(new SKImageInfo(resize.Value.width, resize.Value.height), SKFilterQuality.High);
            }
            
            return new SKTexture(bitmap);
        }
    }

    private static readonly object LockObj = new();
    public static unsafe SKBitmap ToBitmap(TextureResource resource)
    {
        lock (LockObj)
        {
            var meta = FromResource(resource);
            var image = ScratchImage.Initialize(meta);

            // copy data - ensure destination not too short
            fixed (byte* data = image.Pixels)
            {
                var span = new Span<byte>(data, image.Pixels.Length);
                if (resource.Data.Length > span.Length)
                {
                    // As far as I can tell this only happens when placeholder textures are used anyways
                    Service.Log.Warning($"Data too large for scratch image. " +
                                        $"{resource.Data.Length} > {span.Length} " +
                                        $"{resource.Width}x{resource.Height} {resource.Format}");
                }
                else
                {
                    resource.Data.AsSpan().CopyTo(span);
                }
            }
            
            image.GetRGBA(out var rgba);

            var bitmap = new SKBitmap(rgba.Meta.Width, rgba.Meta.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            bitmap.Erase(new SKColor(0));
            fixed (byte* data = rgba.Pixels)
            {
                bitmap.InstallPixels(bitmap.Info, (nint)data, rgba.Meta.Width * 4);
            }
            
            // I have trust issues
            var copy = new SKBitmap(bitmap.Info);
            bitmap.CopyTo(copy, bitmap.Info.ColorType);
            
            return copy;
        }
    }

    private static TexMeta FromResource(TextureResource resource)
    {
        var meta = new TexMeta
        {
            Height = resource.Height,
            Width = resource.Width,
            Depth = 1, // 3D textures would have other values, but we're only handling kernelTexture->D3D11Texture2D
            MipLevels = resource.MipLevels,
            ArraySize = resource.ArraySize,
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

    /*
    public static TextureResource FromTexFile(TexFile file)
    {
        var format = file.Header.Format switch
        {
            LuminaFormat.L8 => Format.R8_UNorm,
            LuminaFormat.A8 => Format.A8_UNorm,
            LuminaFormat.B4G4R4A4 => Format.B4G4R4A4_UNorm,
            LuminaFormat.B5G5R5A1 => Format.B5G5R5A1_UNorm,
            LuminaFormat.B8G8R8A8 => Format.B8G8R8A8_UNorm,
            LuminaFormat.B8G8R8X8 => Format.B8G8R8X8_UNorm,
            LuminaFormat.R16G16B16A16F => Format.R16G16B16A16_Float,
            LuminaFormat.R32G32B32A32F => Format.R32G32B32A32_Float,
            LuminaFormat.BC1 => Format.BC1_UNorm,
            LuminaFormat.BC2 => Format.BC2_UNorm,
            LuminaFormat.BC3 => Format.BC3_UNorm,
            LuminaFormat.BC5 => Format.BC5_UNorm,
            LuminaFormat.BC7 => Format.BC7_UNorm,
            _ => throw new NotSupportedException($"Unsupported format {file.Header.Format}")
        };

        var bpp = file.Header.Format switch
        {
            LuminaFormat.L8 => 1,
            LuminaFormat.A8 => 1,
            LuminaFormat.B4G4R4A4 => 2,
            LuminaFormat.B5G5R5A1 => 2,
            LuminaFormat.B8G8R8A8 => 4,
            LuminaFormat.B8G8R8X8 => 4,
            LuminaFormat.R16G16B16A16F => 8,
            LuminaFormat.R32G32B32A32F => 16,
            LuminaFormat.BC1 => 8,
            LuminaFormat.BC2 => 16,
            LuminaFormat.BC3 => 16,
            LuminaFormat.BC5 => 16,
            LuminaFormat.BC7 => 16,
            _ => throw new NotSupportedException($"Unsupported format {file.Header.Format}")
        };

        int stride;
        if (file.Header.Format is LuminaFormat.BC1 or LuminaFormat.BC2 or LuminaFormat.BC3 or LuminaFormat.BC5 or LuminaFormat.BC7)
            stride = bpp * Math.Max(1, (file.Header.Width + 3) / 4);
        else
            stride = bpp * file.Header.Width;

        return new TextureResource(format, file.Header.Width, file.Header.Height, stride, file.Data);
    }
     
    public static byte[] ToBGRA8(TextureResource resource)
    {
        var ret = new byte[resource.Width * resource.Height * 4];
        var retBuffer = MemoryMarshal.Cast<byte, uint>(ret.AsSpan());
        switch (resource.Format)
        {
            // Integer types
            case Format.R8_UNorm: // => TexFile.TextureFormat.L8 (Unsupported)
                FromL8(resource, retBuffer);
                break;
            case Format.A8_UNorm: // => TexFile.TextureFormat.A8
                FromA8(resource, retBuffer);
                break;
            case Format.B4G4R4A4_UNorm: // => TexFile.TextureFormat.B4G4R4A4 (Unsupported)
                FromB4G4R4A4(resource, retBuffer);
                break;
            case Format.B5G5R5A1_UNorm: // => TexFile.TextureFormat.B5G5R5A1 (Unsupported)
                FromB5G5R5A1(resource, retBuffer);
                break;
            case Format.B8G8R8A8_UNorm: // => TexFile.TextureFormat.B8G8R8A8
                FromB8G8R8A8(resource, retBuffer);
                break;
            case Format.B8G8R8X8_UNorm: // => TexFile.TextureFormat.B8G8R8X8
                FromB8G8R8X8(resource, retBuffer);
                break;

            // Floating point types
            case Format.R16G16B16A16_Float: // => TexFile.TextureFormat.R16G16B16A16F
                FromR16G16B16A16F(resource, retBuffer);
                break;
            case Format.R32G32B32A32_Float: // => TexFile.TextureFormat.R32G32B32A32F
                FromR32G32B32A32F(resource, retBuffer);
                break;

            // Block compression types
            case Format.BC1_UNorm: // => TexFile.TextureFormat.BC1
            case Format.BC2_UNorm: // => TexFile.TextureFormat.BC2
            case Format.BC3_UNorm: // => TexFile.TextureFormat.BC3
            case Format.BC5_UNorm: // => TexFile.TextureFormat.BC5
            case Format.BC7_UNorm: // => TexFile.TextureFormat.BC7 (Unsupported)
                FromBC(resource, retBuffer);
                break;

            default:
                throw new NotImplementedException($"Unsupported format {resource.Format}");
        }
        return ret;
    }

    public static unsafe SKBitmap ToBitmap(TextureResource resource)
    {
        var s = Stopwatch.StartNew();

        var bitmap = new SKBitmap(resource.Width, resource.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(new(0));
        var direct = false;
        if (GetImageInfo(resource.Format) is { } format)
        {
            direct = true;
            fixed (byte* data = resource.Data)
                bitmap.InstallPixels(format.WithSize(resource.Width, resource.Height), (nint)data, resource.Stride);
        }
        else
        {
            format = GetImageInfo(Format.B8G8R8A8_UNorm)!.Value;
            
            var buffer = ToBGRA8(resource);
            fixed (byte* data = buffer)
                bitmap.InstallPixels(format.WithSize(resource.Width, resource.Height), (nint)data,  resource.Width * 4);
        }
        s.Stop();
        Service.Log.Debug($"ToBitmap ({(direct ? "SkiaSharp" : "Software")}) took {s.Elapsed.TotalMilliseconds}ms for {resource.Width}x{resource.Height} {resource.Format}");
        
        // make copy so we can dispose the original
        var copy = new SKBitmap(bitmap.Info);
        bitmap.CopyTo(copy, bitmap.Info.ColorType);
        
        return copy;
    }

    private static SKImageInfo? GetImageInfo(Format format) =>
        format switch
        {
            // Integer types
            Format.R8_UNorm => new(0, 0, SKColorType.Gray8, SKAlphaType.Opaque),
            Format.A8_UNorm => new(0, 0, SKColorType.Alpha8, SKAlphaType.Unpremul),
            Format.B4G4R4A4_UNorm => null, //new(0, 0, SKColorType.Bgra4444, SKAlphaType.Unpremul),
            Format.B5G5R5A1_UNorm => null, //new(0, 0, SKColorType.Bgra5551, SKAlphaType.Unpremul),
            Format.B8G8R8A8_UNorm => new(0, 0, SKColorType.Bgra8888, SKAlphaType.Unpremul),
            Format.B8G8R8X8_UNorm => new(0, 0, SKColorType.Bgra8888, SKAlphaType.Opaque),

            // Floating point types
            Format.R16G16B16A16_Float => new(0, 0, SKColorType.RgbaF16, SKAlphaType.Unpremul),
            Format.R32G32B32A32_Float => new(0, 0, SKColorType.RgbaF32, SKAlphaType.Unpremul),
            _ => null
        };

    private static unsafe void FromL8(TextureResource resource, SKBitmap bitmap)
    {
        fixed (byte* data = resource.Data)
            bitmap.InstallPixels(new()
            {
                Width = resource.Width,
                Height = resource.Height,
                ColorType = SKColorType.Gray8,
                AlphaType = SKAlphaType.Opaque
            }, (nint)data, resource.Stride);
    }

    private static void FromL8(TextureResource resource, Span<uint> output)
    {
        var input = resource.Data.AsSpan();
        for (var y = 0; y < resource.Height; ++y)
        {
            var src = input.Slice(y * resource.Stride, resource.Stride);
            var dst = output.Slice(y * resource.Width, resource.Width);
            for (var x = 0; x < resource.Width; ++x)
                dst[x] = 0xFF000000U | (0x10101U * src[x]);
        }
    }

    private static void FromA8(TextureResource resource, Span<uint> output)
    {
        var input = resource.Data.AsSpan();
        for (var y = 0; y < resource.Height; ++y)
        {
            var src = input.Slice(y * resource.Stride, resource.Stride);
            var dst = output.Slice(y * resource.Width, resource.Width);
            for (var x = 0; x < resource.Width; ++x)
            {
                dst[x] = 0x1000000U * src[x];
            }
        }
    }

    private static void FromB4G4R4A4(TextureResource resource, Span<uint> output)
    {
        var input = resource.Data.AsSpan();
        for (var y = 0; y < resource.Height; ++y)
        {
            var src = MemoryMarshal.Cast<byte, ushort>(input.Slice(y * resource.Stride, resource.Stride));
            var dst = output.Slice(y * resource.Width, resource.Width);
            for (var x = 0; x < resource.Width; ++x)
            {
                dst[x] = (uint)(17U * (
                        (((src[x] >>  0) & 0xF) <<  0) |
                        (((src[x] >>  4) & 0xF) <<  8) |
                        (((src[x] >>  8) & 0xF) << 16) |
                        (((src[x] >> 12) & 0xF) << 24)
                    ));
            }
        }
    }

    private static void FromB5G5R5A1(TextureResource resource, Span<uint> output)
    {
        var input = resource.Data.AsSpan();
        for (var y = 0; y < resource.Height; ++y)
        {
            var src = MemoryMarshal.Cast<byte, ushort>(input.Slice(y * resource.Stride, resource.Stride));
            var dst = output.Slice(y * resource.Width, resource.Width);
            for (var x = 0; x < resource.Width; ++x)
            {
                var a = (uint)(src[x] & 0x8000);
                var r = (uint)(src[x] & 0x7C00);
                var g = (uint)(src[x] & 0x03E0);
                var b = (uint)(src[x] & 0x001F);

                var rgb = (r << 9) | (g << 6) | (b << 3);
                dst[x] = (a * 0x1FE00) | rgb | ((rgb >> 5) & 0x070707);
            }
        }
    }

    private static void FromB8G8R8A8(TextureResource resource, Span<uint> output)
    {
        var input = resource.Data.AsSpan();
        for (var y = 0; y < resource.Height; ++y)
        {
            var src = MemoryMarshal.Cast<byte, uint>(input.Slice(y * resource.Stride, resource.Stride));
            var dst = output.Slice(y * resource.Width, resource.Width);
            src[..dst.Length].CopyTo(dst);
        }
    }

    private static void FromB8G8R8X8(TextureResource resource, Span<uint> output)
    {
        var input = resource.Data.AsSpan();
        for (var y = 0; y < resource.Height; ++y)
        {
            var src = MemoryMarshal.Cast<byte, uint>(input.Slice(y * resource.Stride, resource.Stride));
            var dst = output.Slice(y * resource.Width, resource.Width);
            for (var x = 0; x < resource.Width; ++x)
                dst[x] = 0xFF000000U | src[x];
        }
    }

    private static void FromR16G16B16A16F(TextureResource resource, Span<uint> output)
    {
        var input = resource.Data.AsSpan();
        for (var y = 0; y < resource.Height; ++y)
        {
            var src = MemoryMarshal.Cast<byte, Half>(input.Slice(y * resource.Stride, resource.Stride));
            var dst = MemoryMarshal.Cast<uint, byte>(output.Slice(y * resource.Width, resource.Width));
            for (var x = 0; x < resource.Width; ++x)
            {
                var i = x * 4;
                dst[i + 0] = (byte)Math.Round((float)src[i + 2] * byte.MaxValue);
                dst[i + 1] = (byte)Math.Round((float)src[i + 1] * byte.MaxValue);
                dst[i + 2] = (byte)Math.Round((float)src[i + 0] * byte.MaxValue);
                dst[i + 3] = (byte)Math.Round((float)src[i + 3] * byte.MaxValue);
            }
        }
    }

    private static void FromR32G32B32A32F(TextureResource resource, Span<uint> output)
    {
        var input = resource.Data.AsSpan();
        for (var y = 0; y < resource.Height; ++y)
        {
            var src = MemoryMarshal.Cast<byte, float>(input.Slice(y * resource.Stride, resource.Stride));
            var dst = MemoryMarshal.Cast<uint, byte>(output.Slice(y * resource.Width, resource.Width));
            for (var x = 0; x < resource.Width; ++x)
            {
                var i = x * 4;
                dst[i + 0] = (byte)Math.Round(src[i + 2] * byte.MaxValue);
                dst[i + 1] = (byte)Math.Round(src[i + 1] * byte.MaxValue);
                dst[i + 2] = (byte)Math.Round(src[i + 0] * byte.MaxValue);
                dst[i + 3] = (byte)Math.Round(src[i + 3] * byte.MaxValue);
            }
        }
    }

    private static void FromBC(TextureResource resource, Span<uint> output)
    {
        //var bpb = resource.Format == Format.BC1_UNorm ? 8 : 16;
        //var widthBlocks = Math.Max(1, (resource.Width + 3) / 4);
        //var heightBlocks = Math.Max(1, (resource.Height + 3) / 4);
        //var rowPitch = widthBlocks * bpb;
        //var slicePitch = rowPitch * heightBlocks;
        
        //if (rowPitch != resource.Stride && heightBlocks != 1)
        //    throw new ArgumentException($"Input data must be {slicePitch};{rowPitch} ({input.Length}) bytes long. ({resource.Width};{resource.Height};{resource.Stride})", nameof(input));
        var flags = resource.Format switch
        {
            Format.BC1_UNorm => SquishOptions.DXT1,
            Format.BC2_UNorm => SquishOptions.DXT3,
            Format.BC3_UNorm => SquishOptions.DXT5,
            _ => throw new NotSupportedException("Decoding BC5/BC7 data is currently not supported."),
        };
        Squish.DecompressImage(resource.Data, resource.Width, resource.Height, flags).CopyTo(MemoryMarshal.Cast<uint, byte>(output));
    }
    */
}
