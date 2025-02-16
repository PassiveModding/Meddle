using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Meddle.Utils;

public sealed class SKTexture
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
        
        public SKTexture(int width, int height)
        {
            Data = new byte[width * height * 4];
            Width = width;
            Height = height;
        }
        
        public SKTexture(SKBitmap bitmap) : this(bitmap.Width, bitmap.Height)
        {
            if (bitmap.ColorType != SKColorType.Rgba8888 || bitmap.AlphaType != SKAlphaType.Unpremul)
            {
                using var newBitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                using (var canvas = new SKCanvas(newBitmap))
                    canvas.DrawBitmap(bitmap, 0, 0);

                if (newBitmap.ByteCount != Data.Length)
                    throw new ArgumentException("Invalid byte count");
                newBitmap.Bytes.CopyTo(Data, 0);

                if (!newBitmap.Bytes.SequenceEqual(Data))
                    throw new InvalidOperationException("Invalid cloned data");
            }
            else
            {
                if (bitmap.ByteCount != Data.Length)
                    throw new ArgumentException("Invalid byte count");
                bitmap.Bytes.CopyTo(Data, 0);
            }
        }

        public SKTexture Copy()
        {
            var ret = new SKTexture(Width, Height);
            Data.CopyTo(ret.Data, 0);
            return ret;
        }
        
        public SKTexture Resize(int width, int height)
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bitmapCopy = Bitmap.Copy();
            var resize = bitmapCopy.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
            
            return new SKTexture(resize);
        }

        
        public SKColor SampleWrap(Vector2 uv) => SampleWrap(uv.X, uv.Y);
        public SKColor SampleWrap(float u, float v)
        {
            u %= 1;
            v %= 1;
            if (u < 0)
                u += 1;
            if (v < 0)
                v += 1;
            return Sample(u, v);
        }
        
        public SKColor Sample(Vector2 uv) => Sample(uv.X, uv.Y);
        public SKColor Sample(float u, float v)
        {
            var x = (int)(u * Width);
            var y = (int)(v * Height);
            return this[x, y];
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

