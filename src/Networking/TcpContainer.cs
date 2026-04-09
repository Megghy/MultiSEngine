using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace MultiSEngine.Networking
{
    public sealed partial class TcpContainer : IAsyncDisposable
    {
        /// <summary>
        /// 封装租借到的缓冲区, 确保处理完毕后正确归还内存池。
        /// </summary>
        public sealed class PacketRental : IDisposable
        {
            private IMemoryOwner<byte>? _owner;
            public PacketRental(IMemoryOwner<byte> owner, int length)
            {
                _owner = owner;
                Memory = owner.Memory[..length];
            }

            public Memory<byte> Memory { get; }

            public Span<byte> Span => Memory.Span;

            public ReadOnlyMemory<byte> ReadOnlyMemory => Memory;

            public void Dispose()
            {
                _owner?.Dispose();
                _owner = null;
            }
        }

        public TcpContainer(TcpClient connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            Connection = connection;
            _stream = connection.GetStream();
            SendPipeline = new SendPipelineContext(this);
            _reader = PipeReader.Create(_stream, new StreamPipeReaderOptions(leaveOpen: true));
        }

        public event Action<Exception>? OnException;
        public TcpClient Connection { get; }
        public EndPoint? RemoteEndPoint => Connection.Client?.RemoteEndPoint;
        public bool IsDisposed { get; private set; }
        private SendPipelineContext SendPipeline { get; }
        public SendPipelineContext GetSendPipeline() => SendPipeline;

        private readonly NetworkStream _stream;
        private const int PacketChannelCapacity = 512;
        private readonly Channel<PacketRental> _packets = Channel.CreateBounded<PacketRental>(new BoundedChannelOptions(PacketChannelCapacity)
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        private readonly CancellationTokenSource _cts = new();
        private Task? _receiverTask;
        private int _started = 0;

        private readonly PipeReader _reader;
        private const int MaxPacketSize = 65535;

        // 多订阅者批量回调：订阅/退订在锁内复制替换；读路径复用同一批次容器
        private readonly Lock _packetSubLock = new();
        private Func<IReadOnlyList<PacketRental>, CancellationToken, ValueTask>[] _packetSubscribers
            = [];
        public void SubscribePacket(Func<IReadOnlyList<PacketRental>, CancellationToken, ValueTask> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_packetSubLock)
            {
                var old = _packetSubscribers;
                var n = new Func<IReadOnlyList<PacketRental>, CancellationToken, ValueTask>[old.Length + 1];
                Array.Copy(old, n, old.Length);
                n[^1] = handler;
                Volatile.Write(ref _packetSubscribers, n);
            }
        }
        public void Start()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            {
                _receiverTask = ReceiveLoopAsync();
            }
        }

        public void UnsubscribePacket(Func<IReadOnlyList<PacketRental>, CancellationToken, ValueTask> handler)
        {
            if (handler is null)
                return;
            lock (_packetSubLock)
            {
                var old = _packetSubscribers;
                var idx = Array.IndexOf(old, handler);
                if (idx < 0)
                    return;
                if (old.Length == 1)
                {
                    Volatile.Write(ref _packetSubscribers, []);
                    return;
                }
                var n = new Func<IReadOnlyList<PacketRental>, CancellationToken, ValueTask>[old.Length - 1];
                if (idx > 0)
                    Array.Copy(old, 0, n, 0, idx);
                if (idx < old.Length - 1)
                    Array.Copy(old, idx + 1, n, idx, old.Length - idx - 1);
                Volatile.Write(ref _packetSubscribers, n);
            }
        }

        public bool IsConnected => Connection.Client is { Connected: true } && _stream.CanRead;
        public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => SendPipeline.SendAsync(buffer, cancellationToken);
        public ValueTask<bool> SendViaPipelineAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => SendPipeline.SendViaPipelineAsync(buffer, cancellationToken);
        public ValueTask<bool> SendBatchAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken = default)
            => SendPipeline.SendBatchAsync(buffers, cancellationToken);

        public async ValueTask<PacketRental?> GetPacketAsync(CancellationToken cancellationToken = default)
        {
            if (IsDisposed)
                return null;

            try
            {
                while (await _packets.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_packets.Reader.TryRead(out var rental))
                    {
                        return rental;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// 后台异步读取网络流, 将完整包按批次交给订阅者或写入 Channel。
        /// </summary>
        private async Task ReceiveLoopAsync()
        {
            List<PacketRental>? subscriberPackets = null;
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var result = await _reader.ReadAsync(_cts.Token).ConfigureAwait(false);
                    var buffer = result.Buffer;
                    var subs = Volatile.Read(ref _packetSubscribers);

                    if (subs.Length > 0)
                    {
                        subscriberPackets ??= [];
                        subscriberPackets.Clear();

                        while (TryReadPacket(ref buffer, out var owner, out var length))
                        {
                            subscriberPackets.Add(new PacketRental(owner!, length));
                        }

                        try
                        {
                            if (subscriberPackets.Count > 0)
                            {
                                foreach (var handler in subs)
                                {
                                    try
                                    {
                                        await handler(subscriberPackets, _cts.Token).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    {
                                        OnException?.Invoke(ex);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            for (var i = 0; i < subscriberPackets.Count; i++)
                            {
                                subscriberPackets[i].Dispose();
                            }
                            subscriberPackets.Clear();
                        }
                    }
                    else
                    {
                        while (TryReadPacket(ref buffer, out var owner, out var length))
                        {
                            var rental = new PacketRental(owner!, length);
                            try
                            {
                                await _packets.Writer.WriteAsync(rental, _cts.Token).ConfigureAwait(false);
                                rental = null!; // 已交由通道管理
                            }
                            finally
                            {
                                rental?.Dispose();
                            }
                        }
                    }

                    _reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted)
                    {
                        if (!IsDisposed)
                        {
                            OnException?.Invoke(new IOException("Remote closed the connection."));
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    OnException?.Invoke(ex);
                }
            }
            finally
            {
                _packets.Writer.TryComplete();
            }
        }

        private static bool TryReadPacket(ref ReadOnlySequence<byte> buffer, out IMemoryOwner<byte>? owner, out int length)
        {
            owner = null;
            length = 0;
            if (buffer.Length < 2)
                return false;

            Span<byte> header = stackalloc byte[2];
            buffer.Slice(0, 2).CopyTo(header);
            var len = BinaryPrimitives.ReadUInt16LittleEndian(header);
            if (len < 2 || len > MaxPacketSize)
                throw new IOException($"Invalid packet length: {len}");
            if (buffer.Length < len)
                return false;

            var packetOwner = MemoryPool<byte>.Shared.Rent(len);
            buffer.Slice(0, len).CopyTo(packetOwner.Memory.Span);
            buffer = buffer.Slice(len);
            owner = packetOwner;
            length = len;
            return true;
        }

        private async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var remaining = buffer.Length;
            var offset = 0;
            while (remaining > 0)
            {
                var read = await _stream.ReadAsync(buffer.Slice(offset, remaining), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("Remote closed the connection while reading.");
                }
                offset += read;
                remaining -= read;
            }
        }

        public ValueTask DisposeAsync()
            => DisposeCoreAsync(closeConnection: true);

        public ValueTask DisposeAsync(bool closeConnection)
            => DisposeCoreAsync(closeConnection);

        private async ValueTask DisposeCoreAsync(bool closeConnection)
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            _cts.Cancel();

            try
            {
                if (_receiverTask is not null)
                    await _receiverTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore receiver errors during disposal
            }

            _packets.Writer.TryComplete();

            if (closeConnection)
            {
                Connection.Close();
            }

            while (_packets.Reader.TryRead(out var rental))
            {
                rental.Dispose();
            }

            await SendPipeline.DisposeAsync().ConfigureAwait(false);
            _cts.Dispose();
#if DEBUG
            Logs.Warn($"{RemoteEndPoint} TcpContainer disposed.");
#endif
        }
    }
}


