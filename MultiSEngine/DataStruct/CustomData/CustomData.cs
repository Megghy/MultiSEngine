using System.Buffers;
using System.Buffers.Binary;

namespace MultiSEngine.DataStruct.CustomData
{
    public abstract class BaseCustomData
    {
        public abstract string Name { get; }
        public abstract void InternalWrite(BinaryWriter writer);
        public abstract unsafe void InternalRead(BinaryReader reader);
        public virtual ValueTask OnRecievedData(ClientData client)
        {
            return ValueTask.CompletedTask;
        }

        public static Utils.PacketMemoryRental Serialize(BaseCustomData data)
        {
            ArgumentNullException.ThrowIfNull(data);
            using var stream = new PooledBufferStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((ushort)0);
                writer.Write((byte)MessageID.Unused15);
                writer.Write(data.Name);
                writer.Write(string.Empty);
                data.InternalWrite(writer);
                writer.Flush();
            }

            var owner = stream.Detach();
            if (owner.Length > ushort.MaxValue)
                throw new InvalidOperationException($"Custom packet too large: {owner.Length}");
            BinaryPrimitives.WriteUInt16LittleEndian(owner.Memory.Span[..2], (ushort)owner.Length);
            return new Utils.PacketMemoryRental(owner, owner.Length);
        }

        /// <summary>
        /// 允许 BinaryWriter 写入池化缓冲区，避免额外内存复制。
        /// </summary>
        private sealed class PooledBufferStream : Stream
        {
            private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
            private byte[] _buffer;
            private int _position;
            private bool _detached;

            public PooledBufferStream(int initialCapacity = 256)
            {
                _buffer = _pool.Rent(initialCapacity);
            }

            public ArrayPoolMemoryOwner Detach()
            {
                if (_detached || _buffer is null)
                    throw new ObjectDisposedException(nameof(PooledBufferStream));

                _detached = true;
                var owner = new ArrayPoolMemoryOwner(_pool, _buffer, _position);
                _buffer = null!;
                _position = 0;
                return owner;
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _position;
            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
                // no-op
            }

            public override int Read(byte[] buffer, int offset, int count)
                => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin)
                => throw new NotSupportedException();

            public override void SetLength(long value)
                => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
                => Write(buffer.AsSpan(offset, count));

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                EnsureCapacity(buffer.Length);
                buffer.CopyTo(_buffer.AsSpan(_position));
                _position += buffer.Length;
            }

            private void EnsureCapacity(int additional)
            {
                if (_buffer is null)
                    throw new ObjectDisposedException(nameof(PooledBufferStream));

                var required = _position + additional;
                if (required <= _buffer.Length)
                    return;

                var newBuffer = _pool.Rent(Math.Max(required, _buffer.Length * 2));
                _buffer.AsSpan(0, _position).CopyTo(newBuffer);
                _pool.Return(_buffer, clearArray: true);
                _buffer = newBuffer;
            }

            protected override void Dispose(bool disposing)
            {
                if (!_detached && _buffer is not null)
                {
                    _pool.Return(_buffer, clearArray: true);
                }
                _buffer = null!;
                _position = 0;
                base.Dispose(disposing);
            }
        }

        private sealed class ArrayPoolMemoryOwner : IMemoryOwner<byte>
        {
            private readonly ArrayPool<byte> _pool;
            private byte[]? _buffer;

            public ArrayPoolMemoryOwner(ArrayPool<byte> pool, byte[] buffer, int length)
            {
                _pool = pool;
                _buffer = buffer;
                Length = length;
            }

            public int Length { get; }

            public Memory<byte> Memory
                => _buffer is null ? Memory<byte>.Empty : _buffer.AsMemory(0, Length);

            public void Dispose()
            {
                var buffer = Interlocked.Exchange(ref _buffer, null);
                if (buffer is not null)
                {
                    _pool.Return(buffer, clearArray: true);
                }
            }
        }
    }
}
