using System.Text;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Utils;

public static unsafe class SpanMemoryUtils
{
    public static ReadOnlySpan<byte> CreateReadOnlySpanFromNullTerminated(byte* ptr)
    {
        if (ptr == null)
            return ReadOnlySpan<byte>.Empty;

        var length = 0;
        while (ptr[length] != 0)
            length++;

        return new ReadOnlySpan<byte>(ptr, length);
    }
    
    public static string GetStringFromNullTerminated(byte* ptr)
        => Encoding.UTF8.GetString(CreateReadOnlySpanFromNullTerminated(ptr));
    
    public static T Read<T>(this Pointer<byte> ptr, ref int offset) where T : unmanaged
    {
        var value = *(T*)(ptr.Value + offset);
        offset += sizeof(T);
        return value;
    }
    
    public static ReadOnlySpan<T> ReadSpan<T>(this Pointer<byte> ptr, int count, ref int offset) where T : unmanaged
    {
        var span = new ReadOnlySpan<T>(ptr.Value + offset, count);
        offset += count * sizeof(T);
        return span;
    }
}
