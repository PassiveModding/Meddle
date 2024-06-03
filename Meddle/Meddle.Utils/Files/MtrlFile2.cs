using System.Numerics;
using System.Runtime.InteropServices;
using Meddle.Utils.Files.Structs.Material;

namespace Meddle.Utils.Files;

public class MtrlFile2
{
    public const uint MtrlMagic = 0x1030000;
    private readonly byte[] rawData;
    public ReadOnlySpan<byte> RawData => rawData;
    public ref MaterialFileHeader FileHeader => ref MemoryMarshal.Cast<byte, MaterialFileHeader>(rawData)[0];


    public MtrlFile2(byte[] data) : this((ReadOnlySpan<byte>)data) { }
    public MtrlFile2(ReadOnlySpan<byte> data)
    {
        rawData = data.ToArray();
    }
}
