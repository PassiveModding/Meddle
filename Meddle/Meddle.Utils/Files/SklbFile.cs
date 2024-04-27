using System.Runtime.InteropServices;

namespace Meddle.Utils.Files;

public class SklbFile
{
    public const int SklbMagic = 0x736B6C62; // "sklb"
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SklbHeader
    {
        public enum SklbVersion : uint
        {
            V1100 = 0x31313030, // "1100"
            V1110 = 0x31313130, // "1110"
            V1200 = 0x31323030, // "1200"
            V1300 = 0x31333030, // "1300"
            V1301 = 0x31333031, // "1301"
        }
        
        public uint Magic;
        public SklbVersion Version;
        
        public bool OldHeader => IsOldHeader(Version);

        public static bool IsOldHeader(SklbVersion version)
        {
            return version switch
            {
                SklbVersion.V1100 => true,
                SklbVersion.V1110 => true,
                SklbVersion.V1200 => true,
                SklbVersion.V1300 => false,
                SklbVersion.V1301 => false,
                _                 => throw new InvalidDataException($"Unknown version 0x{version:X}"),
            };
        }
    }
    
    public SklbHeader Header;
    private readonly uint skeletonOffset;
    private readonly byte[] rawData;
    public ReadOnlySpan<byte> RawData => rawData;
    public ReadOnlySpan<byte> RawHeader => rawData.AsSpan(0, (int)skeletonOffset);
    public ReadOnlySpan<byte> Skeleton => rawData.AsSpan((int)skeletonOffset);
    
    public SklbFile(byte[] data) : this((ReadOnlySpan<byte>)data) {}
    
    public SklbFile(ReadOnlySpan<byte> data)
    {
        rawData = data.ToArray();
        var reader = new SpanBinaryReader(data);
        Header = reader.Read<SklbHeader>();
        if (Header.Magic != SklbMagic)
            throw new InvalidDataException("Invalid sklb magic");
        
        // Skeleton offset directly follows the layer offset.
        if (Header.OldHeader)
        {
            reader.ReadInt16();
            skeletonOffset = reader.ReadUInt16();
        }
        else
        {
            reader.ReadUInt32();
            skeletonOffset = reader.ReadUInt32();
        }
    }
}
