using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Meddle.Utils;


public static class BinaryReaderUtil
{
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
