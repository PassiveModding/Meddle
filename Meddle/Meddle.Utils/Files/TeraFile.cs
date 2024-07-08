using System.Numerics;
using System.Runtime.InteropServices;

namespace Meddle.Utils.Files;

public struct TeraFile
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PlatePos
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TeraFileHeader
    {
        public uint Version;
        public uint PlateCount;
        public uint PlateSize;
        public float ClipDistance;
        public float Unk;
        private unsafe fixed byte Padding[32];
    }
    
    public TeraFileHeader Header;
    public PlatePos[] Positions;
    public byte[] RawData;
    private readonly int remainingPos;
    public ReadOnlySpan<byte> Data => RawData.AsSpan();
    public ReadOnlySpan<byte> RemainingData => Data[^remainingPos..];
    
    public TeraFile(byte[] data) : this(new ReadOnlySpan<byte>(data))
    {
    }
    
    public TeraFile(ReadOnlySpan<byte> data)
    {
        var reader = new SpanBinaryReader(data);
        Header = reader.Read<TeraFileHeader>();
        Positions = reader.Read<PlatePos>((int)Header.PlateCount).ToArray();
        RawData = data.ToArray();
        remainingPos = reader.Remaining;
    }
    
    /// <summary>
    /// Retrieve the X and Z coordinates of the specified plate index. Note that
    /// the Y coordinate is unnecessary as bg plates each contain all necessary vertical
    /// data in their respective plate.
    /// </summary>
    /// <param name="plateIndex">The index of the bg plate to obtain the coordinates for.</param>
    /// <returns></returns>
    public Vector2 GetPlatePosition( int plateIndex )
    {
        var pos = Positions[ plateIndex ];
        return new Vector2( Header.PlateSize * ( pos.X + 0.5f ), Header.PlateSize * ( pos.Y + 0.5f ) );
    }
}
