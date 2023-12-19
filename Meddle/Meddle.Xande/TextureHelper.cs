using Dalamud.Logging;
using FFXIVClientStructs.Havok;
using Lumina.Data.Parsing.Tex;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.DXGI;

namespace Meddle.Xande;

public static class TextureHelper
{
    public readonly record struct TextureResource(Format Format, int Width, int Height, int Stride, byte[] Data);

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
        PluginLog.Log($"ToBitmap ({(direct ? "DIRECT" : "indirect")}) took {s.Elapsed.TotalMilliseconds}ms");
        return bitmap;
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
}
