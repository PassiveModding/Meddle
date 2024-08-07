﻿using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Models;
using OtterTex;
using SkiaSharp;

namespace Meddle.Utils;

public static class ImageUtils
{
    public static int GetStride(this TexFile.TextureFormat format, int width)
    {
        return format switch
        {
            TexFile.TextureFormat.BC1 => (width + 3) / 4 * 8,
            TexFile.TextureFormat.BC2 => (width + 3) / 4 * 16,
            TexFile.TextureFormat.BC3 => (width + 3) / 4 * 16,
            TexFile.TextureFormat.BC5 => width * 2,
            TexFile.TextureFormat.BC7 => (width + 3) / 4 * 16,
            _ => width * 4,
        };
    }
    
    public static ReadOnlySpan<byte> ImageAsPng(Image image)
    {
        unsafe
        {
            fixed (byte* data = image.Span)
            {
                using var bitmap = new SKBitmap();
                var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

                bitmap.InstallPixels(info, (IntPtr)data, image.Width * 4);

                var str = new SKDynamicMemoryWStream();
                bitmap.Encode(str, SKEncodedImageFormat.Png, 100);

                return str.DetachAsData().AsSpan();
            }
        }
    }
    
    public static TexMeta GetTexMeta(TextureResource resource)
    {
        var meta = new TexMeta
        {
            Width = resource.Width,
            Height = resource.Height,
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
            Format = TexFile.GetDxgiFormatFromTextureFormat(tex.Header.Format),
            Dimension = TexFile.GetTexDimensionFromAttribute(tex.Header.Type),
            MiscFlags = miscFlags,
            MiscFlags2 = 0,
        };

        return meta;
    }

    public static Image GetTexData(TexFile tex, int arrayLevel, int mipLevel, int slice)
    {
        var meta = GetTexMeta(tex);
        ScratchImage si;
        Image img;
        if (tex.Header.Type == TexFile.Attribute.TextureType2DArray)
        {
            // workaround due to ffxiv texture array weirdness
            var texSlice = tex.SliceSpan(mipLevel, arrayLevel, out var sliceSize, out var sliceWidth,
                                         out var sliceHeight);
            meta.Width = sliceWidth;
            meta.Height = sliceHeight;
            meta.ArraySize = 1;
            meta.MipLevels = 1;

            si = ScratchImage.Initialize(meta);
            unsafe
            {
                fixed (byte* data = si.Pixels)
                {
                    var span = new Span<byte>(data, si.Pixels.Length);
                    texSlice.CopyTo(span);
                }
            }

            si.GetRGBA(out var rgba);
            img = rgba.GetImage(0, 0, 0);
        }
        else if (tex.Header.Type == TexFile.Attribute.TextureTypeCube)
        {
            meta.ArraySize = 6;
            meta.MiscFlags = D3DResourceMiscFlags.TextureCube;

            si = ScratchImage.Initialize(meta);
            unsafe
            {
                fixed (byte* data = si.Pixels)
                {
                    var span = new Span<byte>(data, si.Pixels.Length);
                    tex.TextureBuffer.CopyTo(span);
                }
            }

            si.GetRGBA(out var rgba);
            img = rgba.GetImage(0, arrayLevel, 0);
        }
        else
        {
            si = ScratchImage.Initialize(meta);
            unsafe
            {
                fixed (byte* data = si.Pixels)
                {
                    var span = new Span<byte>(data, si.Pixels.Length);
                    tex.TextureBuffer.CopyTo(span);
                }
            }

            si.GetRGBA(out var rgba);
            img = rgba.GetImage(mipLevel, 0, slice);
        }

        return img;
    }
    
    public static unsafe SKTexture ToTexture(this Image img, (int width, int height)? resize = null)
    {
        if (img.Format != DXGIFormat.R8G8B8A8UNorm)
            throw new ArgumentException("Image must be in RGBA format.", nameof(img));
        // assum RGBA
        var data = img.Span;
        var bitmap = new SKBitmap(img.Width, img.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        fixed (byte* ptr = data)
        {
            bitmap.InstallPixels(new SKImageInfo(img.Width, img.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul), (IntPtr)ptr, img.Width * 4);
        }
        
        if (resize != null)
        {
            bitmap = bitmap.Resize(new SKImageInfo(resize.Value.width, resize.Value.height, SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High);
        }
        
        return new SKTexture(bitmap);
    }
    
    public static SKTexture ToTexture(this Texture tex, (int width, int height)? resize = null)
    {
        var bitmap = tex.ToBitmap();
        if (resize != null)
        {
            bitmap = bitmap.Resize(new SKImageInfo(resize.Value.width, resize.Value.height, SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High);
        }
        
        return new SKTexture(bitmap);
    }

    private static readonly object LockObj = new();
    private static unsafe SKBitmap ToBitmap(this Texture resource)
    {
        lock (LockObj)
        {
            var image = ScratchImage.Initialize(resource.Meta);

            // copy data - ensure destination not too short
            fixed (byte* data = image.Pixels)
            {
                var span = new Span<byte>(data, image.Pixels.Length);
                if (resource.Resource.Data.Length > span.Length)
                {
                    // As far as I can tell this only happens when placeholder textures are used anyway
                    Console.WriteLine($"Data too large for scratch image. " +
                                      $"{resource.Resource.Data.Length} > {span.Length} " +
                                      $"{resource.Meta.Width}x{resource.Meta.Height} {resource.Meta.Format}");
                }
                else
                {
                    resource.Resource.Data.AsSpan().CopyTo(span);
                }
            }
            
            image.GetRGBA(out var rgba);

            var bitmap = new SKBitmap(rgba.Meta.Width, rgba.Meta.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            bitmap.Erase(new SKColor(0));
            fixed (byte* data = rgba.Pixels)
            {
                bitmap.InstallPixels(bitmap.Info, (nint)data, rgba.Meta.Width * 4);
            }

            // return copy, retaining unpremultiplied alpha
            var copy = new SKBitmap(rgba.Meta.Width, rgba.Meta.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var pixelBuf = copy.GetPixelSpan().ToArray();
            bitmap.GetPixelSpan().CopyTo(pixelBuf);
            fixed (byte* data = pixelBuf)
            {
                copy.InstallPixels(copy.Info, (nint)data, copy.Info.RowBytes);
            }
            
            return copy;
        }
    }
    
    public static unsafe SKBitmap ToBitmap(this TextureResource resource)
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
                    Console.WriteLine($"Data too large for scratch image. " +
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

    public static TexMeta FromResource(TextureResource resource)
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
}
