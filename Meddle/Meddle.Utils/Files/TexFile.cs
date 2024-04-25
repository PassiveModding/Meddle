using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OtterTex;

namespace Meddle.Utils.Files;

public class TexFile
{
    [Flags]
    public enum Attribute : uint
    {
        DiscardPerFrame = 0x1,
        DiscardPerMap = 0x2,
        Managed = 0x4,
        UserManaged = 0x8,
        CpuRead = 0x10,
        LocationMain = 0x20,
        NoGpuRead = 0x40,
        AlignedSize = 0x80,
        EdgeCulling = 0x100,
        LocationOnion = 0x200,
        ReadWrite = 0x400,
        Immutable = 0x800,
        TextureRenderTarget = 0x100000,
        TextureDepthStencil = 0x200000,
        TextureType1D = 0x400000,
        TextureType2D = 0x800000,
        TextureType3D = 0x1000000,
        TextureTypeCube = 0x2000000,
        TextureTypeMask = 0x3C00000,
        TextureSwizzle = 0x4000000,
        TextureNoTiled = 0x8000000,
        TextureType2DArray = 0x10000000,
        TextureNoSwizzle = 0x80000000
    }

    [Flags]
    public enum TextureFormat
    {
        TypeShift = 0xC,
        TypeMask = 0xF000,
        ComponentShift = 0x8,
        ComponentMask = 0xF00,
        BppShift = 0x4,
        BppMask = 0xF0,
        EnumShift = 0x0,
        EnumMask = 0xF,
        TypeInteger = 0x1,
        TypeFloat = 0x2,
        TypeDxt = 0x3,
        TypeBc123 = 0x3,
        TypeDepthStencil = 0x4,
        TypeSpecial = 0x5,
        TypeBc57 = 0x6,

        Unknown = 0x0,

        // Integer types
        L8 = 0x1130,
        A8 = 0x1131,
        B4G4R4A4 = 0x1440,
        B5G5R5A1 = 0x1441,
        B8G8R8A8 = 0x1450,
        B8G8R8X8 = 0x1451,

        // Floating point types
        R32F = 0x2150,
        R16G16F = 0x2250,
        R32G32F = 0x2260,
        R16G16B16A16F = 0x2460,
        R32G32B32A32F = 0x2470,

        // Block compression types (DX11 names)
        BC1 = 0x3420,
        BC2 = 0x3430,
        BC3 = 0x3431,
        BC5 = 0x6230,
        BC7 = 0x6432
    }

    public TexHeader Header;
    public byte[] TextureBuffer;

    public TexFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public TexFile(ReadOnlySpan<byte> data)
    {
        var reader = new SpanBinaryReader(data);
        Header = reader.Read<TexHeader>();
        TextureBuffer = reader.Read<byte>(data.Length - HeaderLength).ToArray();
    }

    public int HeaderLength => Unsafe.SizeOf<TexHeader>();

    public int SliceSize(int mipmapIndex, out int width, out int height)
    {
        if (mipmapIndex < 0 || mipmapIndex >= Math.Max((int)Header.MipLevels, 1))
            throw new ArgumentOutOfRangeException(nameof(mipmapIndex), mipmapIndex, null);

        var bpp = 1 << ((int)(Header.Format & TextureFormat.BppMask) >> (int)TextureFormat.BppShift);
        width = Math.Max(1, Header.Width >> mipmapIndex);
        height = Math.Max(1, Header.Height >> mipmapIndex);
        switch ((TextureFormat)((int)(Header.Format & TextureFormat.TypeMask) >> (int)TextureFormat.TypeShift))
        {
            case TextureFormat.TypeBc123:
            case TextureFormat.TypeBc57:
                var nbw = Math.Max(1, (width + 3) / 4);
                var nbh = Math.Max(1, (height + 3) / 4);
                return nbw * nbh * bpp * 2;
            case TextureFormat.TypeInteger:
            case TextureFormat.TypeFloat:
            case TextureFormat.TypeDepthStencil:
            case TextureFormat.TypeSpecial:
                return width * height * bpp / 8;
            default:
                throw new NotSupportedException();
        }
    }

    public unsafe Span<byte> SliceSpan(
        int mipmapIndex, int sliceIndex, out int sliceSize, out int width, out int height)
    {
        sliceSize = SliceSize(mipmapIndex, out width, out height);
        if (mipmapIndex < 0 || mipmapIndex >= Math.Max((int)Header.MipLevels, 1))
            throw new ArgumentOutOfRangeException(nameof(mipmapIndex), mipmapIndex, null);

        switch (Header.Type & Attribute.TextureTypeMask)
        {
            case var _ when sliceIndex < 0:
            case Attribute.TextureType1D when sliceIndex != 0:
            case Attribute.TextureType2D when sliceIndex != 0:
            case Attribute.TextureType3D when sliceIndex >= Header.Depth:
            case Attribute.TextureTypeCube when sliceIndex >= 6:
            case Attribute.TextureType2DArray when sliceIndex >= Header.ArraySize:
                throw new ArgumentOutOfRangeException(nameof(sliceIndex), sliceIndex, null);
        }

        var offset = sliceIndex * sliceSize;
        var mipOffset = (int)(Header.OffsetToSurface[mipmapIndex] - Header.OffsetToSurface[0]);
        offset += mipOffset;
        if (offset >= TextureBuffer.Length)
        {
            return new Span<byte>();
        }

        var length = Math.Min(TextureBuffer.Length - offset, sliceSize);
        return TextureBuffer.AsSpan(offset, length);
    }

    public static TexDimension GetTexDimensionFromAttribute(Attribute attribute)
    {
        var dimension = attribute switch
        {
            Attribute.TextureType1D => TexDimension.Tex1D,
            Attribute.TextureType2D => TexDimension.Tex2D,
            Attribute.TextureType3D => TexDimension.Tex3D,
            Attribute.TextureType2DArray => TexDimension.Tex2D,
            Attribute.TextureTypeCube => TexDimension.Tex2D,
            _ => throw new NotImplementedException($"Unknown texture dimension: {attribute} [{attribute:X2}]")
        };

        return dimension;
    }

    public static DXGIFormat GetDxgiFormatFromTextureFormat(TextureFormat format)
    {
        var dxf = format switch
        {
            TextureFormat.Unknown => DXGIFormat.Unknown,
            TextureFormat.A8 => DXGIFormat.A8UNorm,
            TextureFormat.L8 => DXGIFormat.R8UNorm,
            TextureFormat.B4G4R4A4 => DXGIFormat.B4G4R4A4UNorm,
            TextureFormat.B5G5R5A1 => DXGIFormat.B5G5R5A1UNorm,
            TextureFormat.B8G8R8A8 => DXGIFormat.B8G8R8A8UNorm,
            TextureFormat.B8G8R8X8 => DXGIFormat.B8G8R8X8UNorm,
            TextureFormat.R32F => DXGIFormat.R32Float,
            TextureFormat.R16G16F => DXGIFormat.R16G16Float,
            TextureFormat.R32G32F => DXGIFormat.R32G32Float,
            TextureFormat.R16G16B16A16F => DXGIFormat.R16G16B16A16Float,
            TextureFormat.R32G32B32A32F => DXGIFormat.R32G32B32A32Float,
            TextureFormat.BC1 => DXGIFormat.BC1UNorm,
            TextureFormat.BC2 => DXGIFormat.BC2UNorm,
            TextureFormat.BC3 => DXGIFormat.BC3UNorm,
            TextureFormat.BC5 => DXGIFormat.BC5UNorm,
            TextureFormat.BC7 => DXGIFormat.BC7UNorm,
            _ => throw new NotImplementedException($"Unknown texture format: {format} [{format:X2}]")
        };

        return dxf;
    }

    [StructLayout(LayoutKind.Explicit, Size = 80)]
    public unsafe struct TexHeader
    {
        [FieldOffset(0)]
        public Attribute Type;

        [FieldOffset(4)]
        public TextureFormat Format;

        [FieldOffset(8)]
        public ushort Width;

        [FieldOffset(10)]
        public ushort Height;

        [FieldOffset(12)]
        public ushort Depth;

        [FieldOffset(14)]
        public byte MipLevels;

        [FieldOffset(15)]
        public byte ArraySize;

        [FieldOffset(16)]
        public fixed uint LodOffset[3];

        [FieldOffset(28)]
        public fixed uint OffsetToSurface[13];

        public int CalculatedArraySize => CalculateArraySize();
        public int CalculatedMips => CountMips();

        private int CalculateArraySize()
        {
            int arrSize;
            if (ArraySize == 0) arrSize = 1;
            else if (Type.HasFlag(Attribute.TextureTypeCube)) arrSize = 6;
            else arrSize = ArraySize;

            return arrSize;
        }

        private int CountMips()
        {
            var actualMips = 0;
            for (var i = 0; i < MipLevels; i++)
            {
                if (OffsetToSurface[i] > 0)
                {
                    actualMips++;
                }
                else
                {
                    break;
                }
            }

            return actualMips;
        }
    }
}
