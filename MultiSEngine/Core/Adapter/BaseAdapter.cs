using MultiSEngine.Core.Handler;
using MultiSEngine.DataStruct;

namespace MultiSEngine.Core.Adapter
{
    public class BaseAdapter : IAsyncDisposable, IDisposable
    {
        public BaseAdapter(ClientData client, TcpContainer clientConnection, TcpContainer serverConnection = null)
        {
            Client = client;
            if (clientConnection is not null)
            {
                ClientConnection = clientConnection;
                ClientConnection.OnException += OnConnectionException;
            }
            if (serverConnection is not null)
            {
                SetServerConnection(serverConnection);
            }
            RegisterHandlers();
        }
        #region 变量
        public int ErrorCount { get; protected set; } = 0;
        public bool IsDisposed { get; private set; } = false;
        public ClientData Client { get; private set; }
        protected List<BaseHandler> _handlers { get; set; } = [];

        internal TcpContainer ClientConnection { get; private set; }
        internal TcpContainer ServerConnection { get; private set; }

        private readonly CancellationTokenSource _cts = new();
        private int _started = 0;
        private volatile bool _attached = false;
        private volatile bool _pauseClientToServer = false;
        private volatile bool _pauseServerToClient = false;
        private Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? _clientPacketHandler;
        private Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? _serverPacketHandler;

        #endregion
        protected virtual void RegisterHandlers()
        {
            if (Client is null)
                return;
            RegisterHandler<CommonHandler>();
            RegisterHandler<CustomPacketHandler>();
            RegisterHandler<PlayerInfoHandler>();
            RegisterHandler<ChatHandler>();
        }
        public async ValueTask SetServerConnectionAsync(TcpContainer serverConnection, bool disposeOld = false)
        {
            // 先取消订阅旧连接的接收回调，避免双读
            try
            {
                if (_attached && ServerConnection is not null && _serverPacketHandler is not null)
                    ServerConnection.UnsubscribePacket(_serverPacketHandler);
            }
            catch (Exception ex)
            {
                Logs.Warn($"[{GetType().Name}] Unsubscribe server packet handler failed: {ex}");
                try { ExceptionRaised?.Invoke(ex); } catch { }
            }
            if (disposeOld && ServerConnection is { })
            {
                try
                {
                    await ServerConnection.DisposeAsync(true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logs.Warn($"[{GetType().Name}] Dispose old server connection failed: {ex}");
                }
            }
            ServerConnection = serverConnection;
            ServerConnection.OnException += OnConnectionException;
            // 若已启动，立即订阅新连接的数据回调
            if (_attached)
            {
                EnsureServerSubscribed();
                ServerConnection.Start(); // 如果 Adapter 已启动，立即启动新连接的接收
            }
        }

        public void SetServerConnection(TcpContainer serverConnection, bool disposeOld = false)
            => _ = SetServerConnectionAsync(serverConnection, disposeOld);
        public virtual async ValueTask DisposeAsync(bool disposeConnection = true)
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            _cts.Cancel();
            // 取消订阅，防止回调泄漏
            try
            {
                if (ClientConnection is not null && _clientPacketHandler is not null)
                    ClientConnection.UnsubscribePacket(_clientPacketHandler);
            }
            catch (Exception ex)
            {
                Logs.Warn($"[{GetType().Name}] Unsubscribe client packet handler failed: {ex}");
                try { ExceptionRaised?.Invoke(ex); } catch { }
            }
            try
            {
                if (ServerConnection is not null && _serverPacketHandler is not null)
                    ServerConnection.UnsubscribePacket(_serverPacketHandler);
            }
            catch (Exception ex)
            {
                Logs.Warn($"[{GetType().Name}] Unsubscribe server packet handler failed during dispose: {ex}");
                try { ExceptionRaised?.Invoke(ex); } catch { }
            }
            foreach (var handler in _handlers)
            {
                handler.Dispose();
            }
            _handlers.Clear();
            if (ClientConnection is not null)
                ClientConnection.OnException -= OnConnectionException;
            if (ServerConnection is not null)
                ServerConnection.OnException -= OnConnectionException;
            if (disposeConnection)
            {
                if (ClientConnection is not null)
                    await ClientConnection.DisposeAsync(true).ConfigureAwait(false);
                if (ServerConnection is not null)
                    await ServerConnection.DisposeAsync(true).ConfigureAwait(false);
            }
#if DEBUG
            Logs.Warn($"[{GetType()}] Stopped");
#endif
        }

        public void Dispose()
            => Dispose(true);

        public void Dispose(bool disposeConnection)
            // 改为异步 fire-and-forget，避免阻塞线程
            => _ = DisposeAsync(disposeConnection);

        ValueTask IAsyncDisposable.DisposeAsync()
            => DisposeAsync(true);
        public void Start()
        {
            if (IsDisposed || _cts.IsCancellationRequested)
                return;
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                return;
            // 1. 订阅回调
            EnsureClientSubscribed();
            EnsureServerSubscribed();

            // 2. 启动 TcpContainer 的接收循环
            ClientConnection?.Start();
            ServerConnection?.Start();

            _attached = true;
        }
        public void PauseRouting(bool pauseClientToServer, bool pauseServerToClient)
        {
            _pauseClientToServer = pauseClientToServer;
            _pauseServerToClient = pauseServerToClient;
        }
        private async ValueTask HandlePacketAsync(bool fromServer, ReadOnlyMemory<byte> memory, CancellationToken cancellationToken)
        {
            try
            {
                if (fromServer)
                {
                    if (_pauseServerToClient)
                        return;
                }
                else
                {
                    if (_pauseClientToServer)
                        return;
                }

                var messageId = (MessageID)memory.Span[2];
#if DEBUG
                Console.WriteLine(fromServer
                    ? $"[Recieve SERVER] {messageId}"
                    : $"[Recieve CLIENT] {messageId}");
#endif
                var context = new HandlerPacketContext(messageId, memory, fromServer);
                if (fromServer)
                {
                    for (int i = _handlers.Count - 1; i >= 0; i--)
                    {
                        if (await _handlers[i].RecieveServerDataAsync(context).ConfigureAwait(false))
                            return;
                    }
                    await SendToClientDirectAsync(memory, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    for (int i = _handlers.Count - 1; i >= 0; i--)
                    {
                        if (await _handlers[i].RecieveClientDataAsync(context).ConfigureAwait(false))
                            return;
                    }
                    await SendToServerDirectAsync(memory, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ErrorCount++;
                Logs.Error($"An error occurred while processing {(fromServer ? "SERVER" : "CLIENT")} packets.{Environment.NewLine}{ex}");
                try { ExceptionRaised?.Invoke(ex); } catch { }
            }
        }
        private void EnsureClientSubscribed()
        {
            if (ClientConnection is null || ClientConnection.IsDisposed)
                return;
            _clientPacketHandler ??= async (mem, token) => await HandlePacketAsync(fromServer: false, mem, token);
            ClientConnection.SubscribePacket(_clientPacketHandler);
        }
        private void EnsureServerSubscribed()
        {
            if (ServerConnection is null || ServerConnection.IsDisposed)
                return;
            _serverPacketHandler ??= async (mem, token) => await HandlePacketAsync(fromServer: true, mem, token);
            ServerConnection.SubscribePacket(_serverPacketHandler);
        }

        public event Action<Exception> ExceptionRaised;

        public virtual void OnConnectionException(Exception ex)
        {
            ErrorCount++;
            Logs.Error($"Connection error: {ex}");
            try { ExceptionRaised?.Invoke(ex); } catch { }
        }


        public void RegisterHandler(BaseHandler handler)
        {
            handler.Initialize();
            _handlers.Add(handler);
        }
        public void RegisterHandler<T>() where T : BaseHandler
        {
            var handler = Activator.CreateInstance(typeof(T), new object[] { this }) as BaseHandler;
            RegisterHandler(handler);
        }
        public bool DeregisterHandler<T>(T handler) where T : BaseHandler
        {
            handler?.Dispose();
            return _handlers.Remove(handler);
        }

        #region Packet Send

        public async ValueTask<bool> SendToClientDirectAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (ClientConnection is null)
            {
#if DEBUG
                Console.WriteLine($"[Internal Send TO CLIENT] Connection is null");
#endif
                return false;
            }
#if DEBUG
            if ((MessageID)buffer.Span[2] is MessageID.NetModules)
            {
                Console.WriteLine($"[Internal Send TO CLIENT] {MessageID.NetModules} - SubID: {(NetModuleType)buffer.Span[3]}");
            }
            else
            {
                Console.WriteLine($"[Internal Send TO CLIENT] {(MessageID)buffer.Span[2]}");
            }
#endif

            var sendContext = ClientConnection.GetSendPipeline();
            return await sendContext.SendViaPipelineAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        public async ValueTask<bool> SendToClientBatchAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken = default)
        {
            if (ClientConnection is null)
            {
#if DEBUG
                Console.WriteLine($"[Internal Batch TO CLIENT] Connection is null");
#endif
                return false;
            }
#if DEBUG
            if (buffers.Count > 0)
            {
                var first = buffers[0];
                if (first.Length >= 3)
                    Console.WriteLine($"[Internal Batch TO CLIENT] <{buffers.Count}> {string.Join(", ", buffers.Select(b => (MessageID)b.Span[2]))}");
                else
                    Console.WriteLine($"[Internal Batch TO CLIENT] <header-less> x{buffers.Count}");
            }
#endif

            var sendContext = ClientConnection.GetSendPipeline();
            return await sendContext.SendViaPipelineBatchAsync(buffers, cancellationToken).ConfigureAwait(false);
        }
        public async ValueTask<bool> SendToServerDirectAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (ServerConnection is null)
            {
#if DEBUG
                if (Client.CurrentServer is not null)
                    Console.WriteLine($"[Internal Send TO SERVER] Connection is null");
#endif
                return false;
            }
#if DEBUG
            Console.WriteLine($"[Internal Send TO SERVER] {(MessageID)buffer.Span[2]}");
#endif

            var sendContext = ServerConnection.GetSendPipeline();
            return await sendContext.SendViaPipelineAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        public async ValueTask<bool> SendToServerBatchAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken = default)
        {
            if (ServerConnection is null)
            {
#if DEBUG
                Console.WriteLine($"[Internal Batch TO SERVER] Connection is null");
#endif
                return false;
            }
#if DEBUG
            if (buffers.Count > 0)
            {
                var first = buffers[0];
                if (first.Length >= 3)
                    Console.WriteLine($"[Internal Batch TO SERVER] {(MessageID)first.Span[2]} x{buffers.Count}");
                else
                    Console.WriteLine($"[Internal Batch TO SERVER] <header-less> x{buffers.Count}");
            }
#endif

            var sendContext = ServerConnection.GetSendPipeline();
            return await sendContext.SendViaPipelineBatchAsync(buffers, cancellationToken).ConfigureAwait(false);
        }
        public async ValueTask<bool> SendToClientDirectAsync(Packet packet, CancellationToken cancellationToken = default)
        {
            using var rental = packet.AsPacketRental();
            return await SendToClientDirectAsync(rental.Memory, cancellationToken).ConfigureAwait(false);
        }
        public async ValueTask<bool> SendToClientBatchAsync(IReadOnlyList<Packet> packets, CancellationToken cancellationToken = default)
        {
            using var rentals = new PacketBatchRental(packets);
            return await SendToClientBatchAsync(rentals.Buffers, cancellationToken).ConfigureAwait(false);
        }
        public async ValueTask<bool> SendToServerDirectAsync(Packet packet, CancellationToken cancellationToken = default)
        {
            using var rental = packet.AsPacketRental();
            return await SendToServerDirectAsync(rental.Memory, cancellationToken).ConfigureAwait(false);
        }
        public async ValueTask<bool> SendToServerBatchAsync(IReadOnlyList<Packet> packets, CancellationToken cancellationToken = default)
        {
            using var rentals = new PacketBatchRental(packets);
            return await SendToServerBatchAsync(rentals.Buffers, cancellationToken).ConfigureAwait(false);
        }

        protected virtual void ConfigureSendPipeline(TcpContainer.SendPipelineContext context)
        {
            // 保留虚方法以便子类扩展，例如追加额外的发送参数
        }

        private readonly struct PacketBatchRental : IDisposable
        {
            private readonly Utils.PacketMemoryRental[] _rentals;
            public PacketBatchRental(IReadOnlyList<Packet> packets)
            {
                var count = packets.Count;
                _rentals = new Utils.PacketMemoryRental[count];
                Buffers = new ReadOnlyMemory<byte>[count];
                for (int i = 0; i < count; i++)
                {
                    _rentals[i] = packets[i].AsPacketRental();
                    Buffers[i] = _rentals[i].Memory;
                }
            }

            public ReadOnlyMemory<byte>[] Buffers { get; }

            public void Dispose()
            {
                for (int i = 0; i < _rentals.Length; i++)
                {
                    _rentals[i].Dispose();
                }
            }
        }
        #endregion
    }
}
