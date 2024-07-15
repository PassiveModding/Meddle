using System.Runtime.InteropServices;
using System.Text;

namespace Meddle.Utils.Files;

public class PapFile
{
    public const uint PapMagic = 0x70617020; // "pap "
    public PapFileHeader FileHeader;
    public PapAnimation[] Animations;
    
    private byte[] _data;
    public ReadOnlySpan<byte> RawData => _data;
    public ReadOnlySpan<byte> HavokData => RawData.Slice((int)FileHeader.HavokOffset, (int)FileHeader.FooterOffset - (int)FileHeader.HavokOffset);
    public ReadOnlySpan<byte> FooterData => RawData.Slice((int)FileHeader.FooterOffset);
    
    public PapFile(byte[] data) : this((ReadOnlySpan<byte>)data) { }

    public PapFile(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
        var reader = new SpanBinaryReader(data);
        FileHeader = reader.Read<PapFileHeader>();
        Animations = reader.Read<PapAnimation>(FileHeader.AnimationCount).ToArray();
    }

    public unsafe struct PapAnimation
    {
        public fixed byte Name[32];
        public ushort Type;
        public short HavokIndex;
        public bool IsFace;
        
        public string GetName => GetNameString();
        
        private string GetNameString()
        {
            fixed (byte* ptr = Name)
            {
                return Encoding.UTF8.GetString(ptr, 32).TrimEnd('\0');
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PapFileHeader
    {
        public uint Magic;
        public uint Version;
        public ushort AnimationCount;
        public ushort ModelId;
        public SkeletonType ModelType;
        public byte Variant;
        public uint InfoOffset;
        public uint HavokOffset;
        public uint FooterOffset;
    }

    public enum SkeletonType : byte
    {
        Human = 0,
        Monster = 1,
        DemiHuman = 2,
        Weapon = 3,
    }
}
