using System.Buffers;
using System.Buffers.Binary;
using TrProtocol;
using TrProtocol.Interfaces;

namespace MultiSEngine.Protocol.Framing;

public sealed record FramingOptions
{
    public int PauseWriterThreshold { get; init; } = 512 * 1024;
    public int ResumeWriterThreshold { get; init; } = 256 * 1024;
    public TimeSpan SendFlushTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public bool DisableTcpDelay { get; init; } = true;
}

public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private bool _disposed;

    public PooledBufferWriter(int initialCapacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    public int WrittenCount { get; private set; }
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, WrittenCount);
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    public void Reset() => WrittenCount = 0;

    public void Advance(int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var next = WrittenCount + count;
        if (next > _buffer.Length)
            throw new InvalidOperationException("Advanced beyond rented buffer length.");
        WrittenCount = next;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(WrittenCount);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(WrittenCount);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;
        data.CopyTo(GetSpan(data.Length));
        Advance(data.Length);
    }

    public void Write(in ReadOnlySequence<byte> sequence)
    {
        var length = checked((int)sequence.Length);
        if (length == 0)
            return;
        sequence.CopyTo(GetSpan(length));
        Advance(length);
    }

    public int WritePacket(INetPacket packet, bool fromServer)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet is ISideSpecific sideSpecific)
            sideSpecific.IsServerSide = fromServer;
        return PacketCodec.SerializeDirect(packet, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ArrayPool<byte>.Shared.Return(_buffer, clearArray: false);
        _buffer = [];
        WrittenCount = 0;
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 1)
            sizeHint = 1;
        var required = WrittenCount + sizeHint;
        if (required <= _buffer.Length)
            return;

        var newSize = Math.Max(required, _buffer.Length * 2);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        _buffer.AsSpan(0, WrittenCount).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer, clearArray: false);
        _buffer = newBuffer;
    }
}

public static class PacketFrameCodec
{
    public const int MinFrameLength = 3;
    public const int MaxFrameLength = ushort.MaxValue;

    public static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> frame)
    {
        frame = default;
        if (buffer.Length < 2)
            return false;

        Span<byte> header = stackalloc byte[2];
        buffer.Slice(0, 2).CopyTo(header);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(header);
        if (length < MinFrameLength)
            throw new InvalidDataException($"Invalid Terraria packet length: {length} (minimum is {MinFrameLength}).");

        if (buffer.Length < length)
            return false;

        frame = buffer.Slice(0, length);
        buffer = buffer.Slice(length);
        return true;
    }

    public static byte[] CopyToArray(in ReadOnlySequence<byte> frame)
    {
        var array = GC.AllocateUninitializedArray<byte>(checked((int)frame.Length));
        frame.CopyTo(array);
        return array;
    }

    public static PacketCodecRental SerializeRented(INetPacket packet, bool fromServer)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet is ISideSpecific sideSpecific)
            sideSpecific.IsServerSide = fromServer;
        return PacketCodec.SerializeRented(packet);
    }

    public static byte[] SerializeToArray(INetPacket packet, bool fromServer)
    {
        using var rental = SerializeRented(packet, fromServer);
        return rental.Memory.ToArray();
    }

    public static INetPacket Deserialize(ReadOnlySpan<byte> frame, bool fromServer)
    {
        if (frame.Length < MinFrameLength)
            throw new InvalidDataException($"Invalid Terraria frame length: {frame.Length}.");
        return PacketCodec.Deserialize(frame, client: fromServer);
    }

    public static INetPacket Deserialize(in ReadOnlySequence<byte> frame, bool fromServer)
    {
        if (frame.IsSingleSegment)
            return Deserialize(frame.FirstSpan, fromServer);

        var length = checked((int)frame.Length);
        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            frame.CopyTo(rented);
            return Deserialize(rented.AsSpan(0, length), fromServer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }
}
