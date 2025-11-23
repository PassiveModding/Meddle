using System.Runtime.InteropServices;
using BCnEncoder.Shared.ImageFiles;

// ReSharper disable InconsistentNaming

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

        // Client::Graphics::Kernel::TextureFormat
        L8_UNORM = 0x1130,
        A8_UNORM = 0x1131,
        R8_UNORM = 0x1132,
        R8_UINT = 0x1133,
        R16_UINT = 0x1140,
        R32_UINT = 0x1150,
        R8G8_UNORM = 0x1240,
        B4G4R4A4_UNORM = 0x1440,
        B5G5R5A1_UNORM = 0x1441,
        B8G8R8A8_UNORM = 0x1450,
        B8G8R8X8_UNORM = 0x1451,
        R16F = 0x2140,
        R32F = 0x2150,
        R16G16F = 0x2250,
        R32G32F = 0x2260,
        R11G11B10F = 0x2350,
        R16G16B16A16F = 0x2460,
        R32G32B32A32F = 0x2470,
        BC1_UNORM = 0x3420,
        BC2_UNORM = 0x3430,
        BC3_UNORM = 0x3431,
        D16_UNORM = 0x4140,
        D24_UNORM_S8_UINT = 0x4250,
        D16_UNORM_2 = 0x5140,
        D24_UNORM_S8_UINT_2 = 0x5150,
        BC4_UNORM = 0x6120,
        BC5_UNORM = 0x6230,
        BC6H_SF16 = 0x6330,
        BC7_UNORM = 0x6432,
        R16_UNORM = 0x7140,
        R16G16_UNORM = 0x7250,
        R10G10B10A2_UNORM_2 = 0x7350,
        R10G10B10A2_UNORM = 0x7450,
        D24_UNORM_S8_UINT_3 = 0x8250
    }

    public readonly TexHeader Header;
    public readonly byte[] TextureBuffer;

    public TexFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public TexFile(ReadOnlySpan<byte> data)
    {
        var reader = new BinaryReader(new MemoryStream(data.ToArray()));
        Header = reader.Read<TexHeader>();
        TextureBuffer = reader.Read<byte>(reader.Remaining()).ToArray();
    }

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

    public unsafe ReadOnlySpan<byte> SliceSpan(
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

    public static D3D10ResourceDimension GetTexDimensionFromAttribute(Attribute attribute)
    {
        var dimension = attribute switch
        {
            Attribute.TextureType1D => D3D10ResourceDimension.D3D10ResourceDimensionTexture1D,
            Attribute.TextureType2D => D3D10ResourceDimension.D3D10ResourceDimensionTexture2D,
            Attribute.TextureType3D => D3D10ResourceDimension.D3D10ResourceDimensionTexture3D,
            Attribute.TextureType2DArray => D3D10ResourceDimension.D3D10ResourceDimensionTexture2D,
            Attribute.TextureTypeCube => D3D10ResourceDimension.D3D10ResourceDimensionTexture2D,
            _ => throw new NotImplementedException($"Unknown texture dimension: {attribute} [{attribute:X2}]")
        };

        return dimension;
    }

    public static DxgiFormat GetDxgiFormatFromTextureFormat(TextureFormat format)
    {
        var dxf = format switch
        {
            TextureFormat.Unknown => DxgiFormat.DxgiFormatUnknown,
            TextureFormat.L8_UNORM => DxgiFormat.DxgiFormatR8Unorm,
            TextureFormat.A8_UNORM => DxgiFormat.DxgiFormatA8Unorm,
            TextureFormat.R8_UNORM => DxgiFormat.DxgiFormatR8Unorm,
            TextureFormat.R8_UINT => DxgiFormat.DxgiFormatR8Uint,
            TextureFormat.R16_UINT => DxgiFormat.DxgiFormatR16Uint,
            TextureFormat.R32_UINT => DxgiFormat.DxgiFormatR32Uint,
            TextureFormat.R8G8_UNORM => DxgiFormat.DxgiFormatR8G8Unorm,
            TextureFormat.B4G4R4A4_UNORM => DxgiFormat.DxgiFormatB4G4R4A4Unorm,
            TextureFormat.B5G5R5A1_UNORM => DxgiFormat.DxgiFormatB5G5R5A1Unorm,
            TextureFormat.B8G8R8A8_UNORM => DxgiFormat.DxgiFormatB8G8R8A8Unorm,
            TextureFormat.B8G8R8X8_UNORM => DxgiFormat.DxgiFormatB8G8R8X8Unorm,
            TextureFormat.R16F => DxgiFormat.DxgiFormatR16Float,
            TextureFormat.R32F => DxgiFormat.DxgiFormatR32Float,
            TextureFormat.R16G16F => DxgiFormat.DxgiFormatR16G16Float,
            TextureFormat.R32G32F => DxgiFormat.DxgiFormatR32G32Float,
            TextureFormat.R11G11B10F => DxgiFormat.DxgiFormatR11G11B10Float,
            TextureFormat.R16G16B16A16F => DxgiFormat.DxgiFormatR16G16B16A16Float,
            TextureFormat.R32G32B32A32F => DxgiFormat.DxgiFormatR32G32B32A32Float,
            TextureFormat.BC1_UNORM => DxgiFormat.DxgiFormatBc1Unorm,
            TextureFormat.BC2_UNORM => DxgiFormat.DxgiFormatBc2Unorm,
            TextureFormat.BC3_UNORM => DxgiFormat.DxgiFormatBc3Unorm,
            TextureFormat.D16_UNORM => DxgiFormat.DxgiFormatD16Unorm,
            TextureFormat.D24_UNORM_S8_UINT => DxgiFormat.DxgiFormatD24UnormS8Uint,
            // TextureFormat.D16_UNORM_2 => DXGIFormat.D16UNorm,
            // TextureFormat.D24_UNORM_S8_UINT_2 => DXGIFormat.D24UNormS8UInt,
            TextureFormat.BC4_UNORM => DxgiFormat.DxgiFormatBc4Unorm,
            TextureFormat.BC5_UNORM => DxgiFormat.DxgiFormatBc5Unorm,
            TextureFormat.BC6H_SF16 => DxgiFormat.DxgiFormatBc6HSf16,
            TextureFormat.BC7_UNORM => DxgiFormat.DxgiFormatBc7Unorm,
            TextureFormat.R16_UNORM => DxgiFormat.DxgiFormatR16Unorm,
            TextureFormat.R16G16_UNORM => DxgiFormat.DxgiFormatR16G16Unorm,
            // TextureFormat.R10G10B10A2_UNORM_2 => DXGIFormat.R10G10B10A2UNorm,
            TextureFormat.R10G10B10A2_UNORM => DxgiFormat.DxgiFormatR10G10B10A2Unorm,
            // TextureFormat.D24_UNORM_S8_UINT_3 => DXGIFormat.D24UNormS8UInt,
            
            // TextureFormat.TypeShift => expr,
            // TextureFormat.TypeMask => expr,
            // TextureFormat.ComponentShift => expr,
            // TextureFormat.ComponentMask => expr,
            // TextureFormat.BppShift => expr,
            // TextureFormat.BppMask => expr,
            // TextureFormat.EnumMask => expr,
            // TextureFormat.TypeInteger => expr,
            // TextureFormat.TypeFloat => expr,
            // TextureFormat.TypeDxt => expr,
            // TextureFormat.TypeSpecial => expr,
            // TextureFormat.TypeBc57 => expr,
            // TextureFormat.D16_UNORM_2 => expr,
            // TextureFormat.D24_UNORM_S8_UINT_2 => expr,
            // TextureFormat.R10G10B10A2_UNORM_2 => expr,
            // TextureFormat.D24_UNORM_S8_UINT_3 => expr,
            _ => throw new NotImplementedException($"Unknown texture format: {format}")
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
        public fixed uint OffsetToSurface[OffsetToSurfaceLength];
        
        public const int OffsetToSurfaceLength = 13;

        public ushort CalculatedArraySize => CalculateArraySize();
        public ushort CalculatedMips => CountMips();

        private ushort CalculateArraySize()
        {
            ushort arrSize;
            if (Type.HasFlag(Attribute.TextureTypeCube)) arrSize = 6;
            else if (ArraySize == 0) arrSize = 1;
            else arrSize = ArraySize;

            return arrSize;
        }

        private ushort CountMips()
        {
            ushort actualMips = 0;
            for (var i = 0; i < MipLevels && i < OffsetToSurfaceLength; i++)
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
