using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;

namespace MultiSEngine.Core.Handler
{
    public class CustomPacketHandler(BaseAdapter parent) : BaseHandler(parent)
    {
        public override async ValueTask<bool> RecieveServerDataAsync(HandlerPacketContext context)
        {
            if (context.MessageId is MessageID.Unused15)
            {
                var data = context.Data;
                using var ms = new ReadOnlyMemoryStream(data);
                using var br = new BinaryReader(ms);
                br.BaseStream.Position = 3;
                var name = br.ReadString();
                if (DataBridge.CustomPackets.TryGetValue(name, out var type))
                {
                    var token = br.ReadString();
                    var packet = Activator.CreateInstance(type) as DataStruct.CustomData.BaseCustomData;
                    packet.InternalRead(br);
                    await packet.OnRecievedData(Client).ConfigureAwait(false);
                }
                else
                {
                    Logs.Error($"Packet [{name}] not defined, ignore.");
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 纯托管的只读内存流，用于避免为 BinaryReader 复制缓冲区。
        /// </summary>
        private sealed class ReadOnlyMemoryStream : Stream
        {
            private readonly ReadOnlyMemory<byte> _buffer;
            private int _position;

            public ReadOnlyMemoryStream(ReadOnlyMemory<byte> buffer)
            {
                _buffer = buffer;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _buffer.Length;

            public override long Position
            {
                get => _position;
                set
                {
                    if (value < 0 || value > Length)
                        throw new ArgumentOutOfRangeException(nameof(value));
                    _position = (int)value;
                }
            }

            public override void Flush() { }

            public override int Read(Span<byte> buffer)
            {
                var remaining = _buffer.Length - _position;
                if (remaining <= 0)
                    return 0;
                var count = Math.Min(remaining, buffer.Length);
                _buffer.Span.Slice(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                return Read(buffer.AsSpan(offset, count));
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long target = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End => Length + offset,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };

                if (target < 0 || target > Length)
                    throw new IOException("Attempted to seek outside the buffer");

                _position = (int)target;
                return _position;
            }

            public override void SetLength(long value)
                => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
                => throw new NotSupportedException();

            public override void Write(ReadOnlySpan<byte> buffer)
                => throw new NotSupportedException();
        }
    }
}
