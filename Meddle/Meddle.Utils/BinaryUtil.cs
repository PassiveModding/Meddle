using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Meddle.Utils;


public static class BinaryUtil
{
    public static void Write<T>(this BinaryWriter stream, T[] data) where T : struct
    {
        if (data.Length == 0)
            return;
        
        var size = Unsafe.SizeOf<T>();
        foreach (var item in data)
        {
            var buffer = MemoryMarshal.AsBytes(new Span<T>(new[] { item }));
            stream.Write(buffer);
        }
    }
    
    public static void Write<T>(this BinaryWriter stream, T data) where T : struct
    {
        var buffer = MemoryMarshal.AsBytes(new Span<T>(new[] { data }));
        stream.Write(buffer);
    }
    
    public static ReadOnlySpan<byte> ReadByteString(this BinaryReader stream, int length)
    {
        var buffer = stream.ReadBytes(length);
        if (buffer.Length < length)
            throw new EndOfStreamException();
        
        return buffer;
    }
    
    public static string ReadString(this BinaryReader stream, int length)
    {
        var buffer = stream.ReadByteString(length);
        return Encoding.UTF8.GetString(buffer);
    }
    
    public static int Remaining(this BinaryReader stream)
    {
        return (int)(stream.BaseStream.Length - stream.BaseStream.Position);
    }
    
    public static T[] Read<T>(this BinaryReader stream, int count) where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than 0.");
        if (count == 0)
            return Array.Empty<T>();
        
        T[] ret = new T[count];
        var size = Unsafe.SizeOf<T>();
        for (int i = 0; i < count; i++)
        {
            var buffer = stream.ReadBytes(size);
            if (buffer.Length < size)
                throw new EndOfStreamException();
            
            ret[i] = MemoryMarshal.Read<T>(buffer);
        }

        return ret;
    }
    
    public static T Read<T>(this BinaryReader stream) where T : struct
    {
        var size = Unsafe.SizeOf<T>();
        var buffer = stream.ReadBytes(size);
        if (buffer.Length < size)
            throw new EndOfStreamException();
        
        return MemoryMarshal.Read<T>(buffer);
    }
}
