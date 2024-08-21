using System.Runtime.InteropServices;
using System.Text;

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
    
    public struct SpanMemoryReader
    {
        private readonly byte* ptr;
        private int offset;

        public SpanMemoryReader(byte* ptr)
        {
            this.ptr = ptr;
        }
        
        public T Read<T>() where T : unmanaged
        {
            var value = *(T*)(ptr + offset);
            offset += sizeof(T);
            return value;
        }
        
        public ReadOnlySpan<T> ReadSpan<T>(int count) where T : unmanaged
        {
            var span = new ReadOnlySpan<T>(ptr + offset, count);
            offset += count * sizeof(T);
            return span;
        }
        
        public string ReadString()
        {
            var str = GetStringFromNullTerminated(ptr + offset);
            offset += str.Length + 1;
            return str;
        }
        
        public void Skip(int count)
            => offset += count;
        
        public void Seek(int position)
            => offset = position;
        
        public void Reset()
            => offset = 0;
        
        public int Position => offset;
    }
}
