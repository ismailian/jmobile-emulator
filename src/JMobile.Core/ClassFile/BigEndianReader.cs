using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace JMobile.Core.ClassFile;

/// <summary>
/// A forward-only cursor over a class file. Class files are big-endian, so every read
/// goes through <see cref="BinaryPrimitives"/> rather than reinterpreting memory.
/// </summary>
/// <remarks>
/// This is a ref struct wrapping a span: parsing a class allocates nothing beyond the
/// strings and arrays that outlive the parse itself.
/// </remarks>
public ref struct BigEndianReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position;

    public readonly int Position => _position;

    public readonly int Length => _data.Length;

    public readonly int Remaining => _data.Length - _position;

    public void Seek(int position)
    {
        if ((uint)position > (uint)_data.Length)
            throw new ClassFormatException($"Seek to {position} is outside the class file.");
        _position = position;
    }

    public void Skip(int count)
    {
        EnsureAvailable(count);
        _position += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadU1()
    {
        EnsureAvailable(1);
        return _data[_position++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadS1() => (sbyte)ReadU1();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadU2()
    {
        EnsureAvailable(2);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(_data[_position..]);
        _position += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadS2()
    {
        EnsureAvailable(2);
        short value = BinaryPrimitives.ReadInt16BigEndian(_data[_position..]);
        _position += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadS4()
    {
        EnsureAvailable(4);
        int value = BinaryPrimitives.ReadInt32BigEndian(_data[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadU4()
    {
        EnsureAvailable(4);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(_data[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadS8()
    {
        EnsureAvailable(8);
        long value = BinaryPrimitives.ReadInt64BigEndian(_data[_position..]);
        _position += 8;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        EnsureAvailable(count);
        var result = _data.Slice(_position, count).ToArray();
        _position += count;
        return result;
    }

    /// <summary>
    /// Decodes a modified-UTF8 constant. The encoding differs from standard UTF-8 in two
    /// ways: an embedded NUL is written as the two-byte sequence C0 80, and characters
    /// outside the BMP are written as a pair of three-byte surrogate encodings rather than
    /// as one four-byte sequence. Feeding such bytes to a standard UTF-8 decoder corrupts
    /// exactly the strings that game resources tend to rely on, so we decode by hand.
    /// </summary>
    public string ReadModifiedUtf8(int byteCount)
    {
        EnsureAvailable(byteCount);
        var bytes = _data.Slice(_position, byteCount);
        _position += byteCount;

        // Fast path: pure ASCII, which covers nearly every class, method and field name.
        bool ascii = true;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b is 0 or >= 0x80)
            {
                ascii = false;
                break;
            }
        }

        if (ascii) return Encoding.ASCII.GetString(bytes);

        var builder = new StringBuilder(byteCount);
        int index = 0;
        while (index < bytes.Length)
        {
            byte b1 = bytes[index++];
            if ((b1 & 0x80) == 0)
            {
                builder.Append((char)b1);
            }
            else if ((b1 & 0xE0) == 0xC0)
            {
                if (index >= bytes.Length) throw new ClassFormatException("Truncated modified-UTF8 sequence.");
                byte b2 = bytes[index++];
                builder.Append((char)(((b1 & 0x1F) << 6) | (b2 & 0x3F)));
            }
            else if ((b1 & 0xF0) == 0xE0)
            {
                if (index + 1 >= bytes.Length) throw new ClassFormatException("Truncated modified-UTF8 sequence.");
                byte b2 = bytes[index++];
                byte b3 = bytes[index++];
                builder.Append((char)(((b1 & 0x0F) << 12) | ((b2 & 0x3F) << 6) | (b3 & 0x3F)));
            }
            else
            {
                throw new ClassFormatException($"Invalid modified-UTF8 lead byte 0x{b1:X2}.");
            }
        }

        return builder.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void EnsureAvailable(int count)
    {
        if (count < 0 || _position + count > _data.Length)
            throw new ClassFormatException(
                $"Class file truncated: needed {count} bytes at offset {_position} of {_data.Length}.");
    }
}
