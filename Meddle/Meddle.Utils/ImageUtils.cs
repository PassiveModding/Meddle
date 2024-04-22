using Meddle.Utils.Files;
using OtterTex;
using SkiaSharp;

namespace Meddle.Utils;

public static class ImageUtils
{
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
            var texSlice = tex.SliceSpan(mipLevel, arrayLevel, out var sliceSize, out var sliceWidth, out var sliceHeight);
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
}
