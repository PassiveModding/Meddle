using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Meddle.Utils;

/// <summary>
/// Equivalent to <see cref="BinaryReader"/>, but for <see cref="ReadOnlySpan{Byte}"/>.
/// </summary>
/// <remarks>
/// This differs from <see cref="BinaryReader"/> in that "array" reads will throw if the requested amount of data is not fully available,
/// and that it only works in terms of <see cref="ReadOnlySpan{T}"/> (use <see cref="ReadOnlySpan{T}.ToArray"/> if needed).
/// </remarks>
public unsafe ref struct SpanBinaryReader
{
    private readonly ref byte _start;
    private ref          byte _pos;

    private SpanBinaryReader(ref byte start, int length)
    {
        _start    = ref start;
        _pos      = ref _start;
        Length    = length;
        Remaining = Length;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public SpanBinaryReader(ReadOnlySpan<byte> span)
        : this(ref MemoryMarshal.GetReference(span), span.Length)
    { }

    public int Position
        => (int)Unsafe.ByteOffset(ref _start, ref _pos);

    public readonly int Length;

    public int Remaining { get; private set; }

    public int Count
        => Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public T Read<T>() where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        if (Remaining < size) 
            throw new EndOfStreamException($"Requested {size} bytes for {typeof(T).Name}, but only {Remaining} bytes remain.");

        var ret = Unsafe.ReadUnaligned<T>(ref _pos);
        _pos      =  ref Unsafe.Add(ref _pos, size);
        Remaining -= size;
        return ret;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ReadOnlySpan<T> Read<T>(int num) where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>() * num;
        if (Remaining < size)
            throw new EndOfStreamException($"Requested {size} bytes for {typeof(T).Name} x {num}, but only {Remaining} bytes remain.");

        var ptr = Unsafe.AsPointer(ref _pos);
        _pos      =  ref Unsafe.Add(ref _pos, size);
        Remaining -= size;
        return new ReadOnlySpan<T>(ptr, num);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void Seek(int offset, SeekOrigin origin)
    {
        if (origin == SeekOrigin.Begin)
        {
            if (offset < 0 || offset > Length)
                throw new ArgumentOutOfRangeException($"Offset: {offset}, Length: {Length}");
            _pos      =  ref Unsafe.Add(ref _start, offset);
            Remaining =  Length - offset;
        }
        else if (origin == SeekOrigin.Current)
        {
            if (offset < 0 || offset > Remaining)
                throw new ArgumentOutOfRangeException($"Offset: {offset}, Remaining: {Remaining}");
            _pos      =  ref Unsafe.Add(ref _pos, offset);
            Remaining -= offset;
        }
        else if (origin == SeekOrigin.End)
        {
            if (offset < 0 || offset > Length)
                throw new ArgumentOutOfRangeException($"Offset: {offset}, Length: {Length}");
            _pos      =  ref Unsafe.Add(ref _start, Length - offset);
            Remaining =  offset;
        }
        else
        {
            throw new ArgumentOutOfRangeException($"Origin: {origin}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public byte ReadByte()
        => Read<byte>();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public short ReadInt16()
        => Read<short>();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public int ReadInt32()
        => Read<int>();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public long ReadInt64()
        => Read<long>();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ushort ReadUInt16()
        => Read<ushort>();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public uint ReadUInt32()
        => Read<uint>();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ulong ReadUInt64()
        => Read<ulong>();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public nint ReadIntPtr()
        => Read<nint>();

    /// <summary>
    /// Create a slice of the reader from <paramref name="position"/> to
    /// <paramref name="position"/> + <paramref name="count"/> without changing the current position.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public readonly SpanBinaryReader SliceFrom(int position, int count)
    {
        if (position < 0 || count < 0)
            throw new ArgumentOutOfRangeException($"Position: {position}, Count: {count}");
        if (position + count > Length)
            throw new EndOfStreamException($"Requested {count} bytes from position {position}, but only {Length - position} bytes remain.");

        return new SpanBinaryReader(ref Unsafe.Add(ref _pos, position), count);
    }

    /// <summary>
    /// Create a slice of size <paramref name="count"/> of the reader from the current position
    /// while incrementing the current position by count.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public SpanBinaryReader SliceFromHere(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException($"Count: {count}");
        if (Remaining < count)
            throw new EndOfStreamException($"Requested {count} bytes, but only {Remaining} bytes remain.");

        var ret = new SpanBinaryReader(ref _pos, count);
        Remaining -= count;
        _pos      =  ref Unsafe.Add(ref _pos, count);
        return ret;
    }

    /// <summary> Read a null-terminated byte string from a given offset based off the start. Does not increment the position. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public readonly ReadOnlySpan<byte> ReadByteString(int offset = 0)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException($"Offset: {offset}");
        if (Length < offset)
            throw new EndOfStreamException($"Requested {offset} bytes, but only {Length} bytes remain.");

        var span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref _start, offset), Length - offset);
        var idx  = span.IndexOf<byte>(0);
        if (idx < 0)
            throw new EndOfStreamException($"No null-terminator found in byte string at offset {offset}.");

        return span[..idx];
    }

    /// <summary> Read a byte string of known length from a given offset based off the start. Does not increment the position. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public readonly ReadOnlySpan<byte> ReadByteString(int offset, int length)
    {
        if (offset < 0 || length < 0)
            throw new ArgumentOutOfRangeException($"Offset: {offset}, Length: {length}");
        if (Length < offset + length)
            throw new EndOfStreamException($"Requested {length} bytes from offset {offset}, but only {Length - offset} bytes remain.");

        return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref _start, offset), length);
    }

    /// <summary> Read a null-terminated byte string from a given offset based off the start and convert it to a C# string. Does not increment the position. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public readonly string ReadString(int offset = 0)
        => Encoding.UTF8.GetString(ReadByteString(offset));

    /// <summary> Read a byte string of known length from a given offset based off the start and convert it to a C# string. Does not increment the position. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public readonly string ReadString(int offset, int length)
        => Encoding.UTF8.GetString(ReadByteString(offset, length));
}
