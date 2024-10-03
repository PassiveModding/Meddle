using System.Numerics;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Microsoft.Extensions.Logging;
using OtterTex;
using SkiaSharp;

namespace Meddle.Utils.Helpers;

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
    
    public static TextureResource ToResource(this TexFile file)
    {
        var h = file.Header;
        D3DResourceMiscFlags flags = 0;
        if (h.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            flags |= D3DResourceMiscFlags.TextureCube;
        return new TextureResource(
            TexFile.GetDxgiFormatFromTextureFormat(h.Format), 
            h.Width, 
            h.Height, 
            h.CalculatedMips, 
            h.CalculatedArraySize, 
            TexFile.GetTexDimensionFromAttribute(h.Type), 
            flags, 
            file.TextureBuffer);
    }
    
    public static ReadOnlySpan<byte> ImageAsPng(Image image)
    {
        unsafe
        {
            fixed (byte* data = image.Span)
            {
                using var bitmap = new SKBitmap();
                var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

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
    
    public static unsafe SKTexture ToTexture(this Image img, Vector2? resize = null)
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
            bitmap = bitmap.Resize(new SKImageInfo((int)resize.Value.X, (int)resize.Value.Y, SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High);
        }
        
        return new SKTexture(bitmap);
    }
    
    public static SKTexture ToTexture(this TextureResource resource, Vector2 size)
    {
        if (resource.Width == (int)size.X && resource.Height == (int)size.Y)
        {
            return resource.ToTexture();
        }
        
        var bitmap = resource.ToBitmap();
        bitmap = bitmap.Resize(new SKImageInfo((int)size.X, (int)size.Y, SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High);
        return new SKTexture(bitmap);
    }
    
    public static SKTexture ToTexture(this TextureResource resource, (int width, int height)? resize = null)
    {
        var bitmap = resource.ToBitmap();
        
        if (resize != null)
        {
            bitmap = bitmap.Resize(new SKImageInfo(resize.Value.width, resize.Value.height, SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High);
        }
        
        return new SKTexture(bitmap);
    }
    
    public static unsafe SKBitmap ToBitmap(this TextureResource resource)
    {
        var meta = FromResource(resource);
        var image = ScratchImage.Initialize(meta);

        // copy data - ensure destination not too short
        fixed (byte* data = image.Pixels)
        {
            var span = new Span<byte>(data, image.Pixels.Length);
            if (resource.Data.Length > span.Length)
            {
                resource.Data[..span.Length].CopyTo(span);
                Global.Logger.LogDebug("Data too large for scratch image. " +
                                       "{Length} > {Length2} " +
                                       "{Width}x{Height} {Format}\n" +
                                       "Trimmed to fit.",
                                       resource.Data.Length, span.Length, resource.Width, 
                                       resource.Height, resource.Format);
            }
            else
            {
                resource.Data.CopyTo(span);
            }
        }
        
        image.GetRGBA(out var rgba);

        var bitmap = new SKBitmap(rgba.Meta.Width, rgba.Meta.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(new SKColor(0));
        fixed (byte* data = rgba.Pixels)
        {
            bitmap.InstallPixels(bitmap.Info, (nint)data, rgba.Meta.Width * 4);
        }
        // bitmap.SetImmutable();
        //
        // return bitmap.Copy() ?? throw new InvalidOperationException("Failed to copy bitmap.");
        var copy = new SKBitmap(rgba.Meta.Width, rgba.Meta.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixelBuf = copy.GetPixelSpan().ToArray();
        bitmap.GetPixelSpan().CopyTo(pixelBuf);
        fixed (byte* data = pixelBuf)
        {
            copy.InstallPixels(copy.Info, (nint)data, copy.Info.RowBytes);
        }
            
        return copy;
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
