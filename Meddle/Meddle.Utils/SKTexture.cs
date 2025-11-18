using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Meddle.Utils;

public sealed class SkTexture
    {
        private byte[] Data { get; }

        public int Width { get; }
        public int Height { get; }
        public Vector2 Size => new(Width, Height);

        public SKBitmap Bitmap
        {
            get
            {
                var ret = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                ret.Erase(new(0));
                var p = ret.GetPixels(out var l);
                if (l != Data.Length)
                    throw new InvalidOperationException("Invalid length");
                Marshal.Copy(Data, 0, p, Data.Length);
                if (!ret.Bytes.SequenceEqual(Data))
                    throw new InvalidOperationException("Invalid copied data");
                return ret;
            }
        }
        
        public SkTexture(int width, int height)
        {
            Data = new byte[width * height * 4];
            Width = width;
            Height = height;
        }
        
        public SkTexture(SKBitmap bitmap) : this(bitmap.Width, bitmap.Height)
        {
            if (bitmap.ColorType != SKColorType.Rgba8888 || bitmap.AlphaType != SKAlphaType.Unpremul)
            {
                using var newBitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                using (var canvas = new SKCanvas(newBitmap))
                    canvas.DrawBitmap(bitmap, 0, 0);

                newBitmap.GetPixelSpan().CopyTo(Data);
            }
            else
            {
                bitmap.GetPixelSpan().CopyTo(Data);
            }
        }

        private Span<byte> GetPixelData(int x, int y) =>
            Data.AsSpan().Slice((Width * y + x) * 4, 4);

        public SKColor this[int x, int y]
        {
            get
            {
                var s = GetPixelData(x, y);
                return new(s[0], s[1], s[2], s[3]);
            }
            set
            {
                var s = GetPixelData(x, y);
                s[0] = value.Red;
                s[1] = value.Green;
                s[2] = value.Blue;
                s[3] = value.Alpha;
            }
        }

        public void Flush()
        {

        }
    }

