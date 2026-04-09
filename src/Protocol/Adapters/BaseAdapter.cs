
namespace MultiSEngine.Protocol.Adapters
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
        private Func<IReadOnlyList<TcpContainer.PacketRental>, CancellationToken, ValueTask>? _clientPacketHandler;
        private Func<IReadOnlyList<TcpContainer.PacketRental>, CancellationToken, ValueTask>? _serverPacketHandler;
        private Dictionary<MessageID, BaseHandler[]> _clientHandlerIndex = new();
        private Dictionary<MessageID, BaseHandler[]> _serverHandlerIndex = new();
        private bool _clientHasWildcardHandlers;
        private bool _serverHasWildcardHandlers;

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
                await RaiseExceptionAsync(ex).ConfigureAwait(false);
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
                await RaiseExceptionAsync(ex).ConfigureAwait(false);
            }
            try
            {
                if (ServerConnection is not null && _serverPacketHandler is not null)
                    ServerConnection.UnsubscribePacket(_serverPacketHandler);
            }
            catch (Exception ex)
            {
                Logs.Warn($"[{GetType().Name}] Unsubscribe server packet handler failed during dispose: {ex}");
                await RaiseExceptionAsync(ex).ConfigureAwait(false);
            }
            foreach (var handler in _handlers)
            {
                handler.Dispose();
            }
            _handlers.Clear();
            _clientHandlerIndex.Clear();
            _serverHandlerIndex.Clear();
            _clientHasWildcardHandlers = false;
            _serverHasWildcardHandlers = false;
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
                if (IsRoutingPaused(fromServer))
                    return;

                var messageId = (MessageID)memory.Span[2];
#if DEBUG
                Console.WriteLine(fromServer
                    ? $"[Recieve SERVER] {messageId}"
                    : $"[Recieve CLIENT] {messageId}");
#endif
                if (fromServer)
                {
                    BaseHandler[] handlers = [];
                    if (!_serverHasWildcardHandlers && !_serverHandlerIndex.TryGetValue(messageId, out handlers))
                    {
                        await SendToClientDirectAsync(memory, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var context = new HandlerPacketContext(messageId, memory, fromServer);
                    if (_serverHasWildcardHandlers)
                    {
                        for (int i = _handlers.Count - 1; i >= 0; i--)
                        {
                            if (await _handlers[i].RecieveServerDataAsync(context).ConfigureAwait(false))
                                return;
                        }
                    }
                    else
                    {
                        for (int i = handlers.Length - 1; i >= 0; i--)
                        {
                            if (await handlers[i].RecieveServerDataAsync(context).ConfigureAwait(false))
                                return;
                        }
                    }
                    await SendToClientDirectAsync(memory, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    BaseHandler[] handlers = [];
                    if (!_clientHasWildcardHandlers && !_clientHandlerIndex.TryGetValue(messageId, out handlers))
                    {
                        await SendToServerDirectAsync(memory, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var context = new HandlerPacketContext(messageId, memory, fromServer);
                    if (_clientHasWildcardHandlers)
                    {
                        for (int i = _handlers.Count - 1; i >= 0; i--)
                        {
                            if (await _handlers[i].RecieveClientDataAsync(context).ConfigureAwait(false))
                                return;
                        }
                    }
                    else
                    {
                        for (int i = handlers.Length - 1; i >= 0; i--)
                        {
                            if (await handlers[i].RecieveClientDataAsync(context).ConfigureAwait(false))
                                return;
                        }
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
                await RaiseExceptionAsync(ex).ConfigureAwait(false);
            }
        }
        private bool IsRoutingPaused(bool fromServer)
            => fromServer ? _pauseServerToClient : _pauseClientToServer;

        private bool CanDirectForward(bool fromServer, MessageID messageId)
        {
            if (fromServer)
                return !_serverHasWildcardHandlers && !_serverHandlerIndex.ContainsKey(messageId);

            return !_clientHasWildcardHandlers && !_clientHandlerIndex.ContainsKey(messageId);
        }

        private async ValueTask FlushForwardBatchAsync(bool fromServer, List<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken)
        {
            if (buffers.Count == 0)
                return;

            if (fromServer)
                await SendToClientBatchAsync(buffers, cancellationToken).ConfigureAwait(false);
            else
                await SendToServerBatchAsync(buffers, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask HandlePacketBatchAsync(bool fromServer, IReadOnlyList<TcpContainer.PacketRental> packets, CancellationToken cancellationToken)
        {
            if (packets.Count == 0 || IsRoutingPaused(fromServer))
                return;

            List<ReadOnlyMemory<byte>>? pendingForwardBatch = null;
            for (var i = 0; i < packets.Count; i++)
            {
                var memory = packets[i].ReadOnlyMemory;
                var messageId = (MessageID)memory.Span[2];
                if (CanDirectForward(fromServer, messageId))
                {
                    pendingForwardBatch ??= [];
                    pendingForwardBatch.Add(memory);
                    continue;
                }

                if (pendingForwardBatch is { Count: > 0 })
                {
                    await FlushForwardBatchAsync(fromServer, pendingForwardBatch, cancellationToken).ConfigureAwait(false);
                    pendingForwardBatch.Clear();
                }

                await HandlePacketAsync(fromServer, memory, cancellationToken).ConfigureAwait(false);
            }

            if (pendingForwardBatch is { Count: > 0 })
            {
                await FlushForwardBatchAsync(fromServer, pendingForwardBatch, cancellationToken).ConfigureAwait(false);
            }
        }
        private void EnsureClientSubscribed()
        {
            if (ClientConnection is null || ClientConnection.IsDisposed)
                return;
            _clientPacketHandler ??= async (packets, token) => await HandlePacketBatchAsync(fromServer: false, packets, token);
            ClientConnection.SubscribePacket(_clientPacketHandler);
        }
        private void EnsureServerSubscribed()
        {
            if (ServerConnection is null || ServerConnection.IsDisposed)
                return;
            _serverPacketHandler ??= async (packets, token) => await HandlePacketBatchAsync(fromServer: true, packets, token);
            ServerConnection.SubscribePacket(_serverPacketHandler);
        }

        public event Func<Exception, Task>? ExceptionRaised;

        private async Task RaiseExceptionAsync(Exception ex)
        {
            var handlers = ExceptionRaised;
            if (handlers is null)
                return;

            foreach (var handler in handlers.GetInvocationList().Cast<Func<Exception, Task>>())
            {
                try
                {
                    await handler(ex).ConfigureAwait(false);
                }
                catch (Exception handlerEx)
                {
                    Logs.Warn($"[{GetType().Name}] Exception handler failed: {handlerEx}");
                }
            }
        }

        public virtual void OnConnectionException(Exception ex)
        {
            ErrorCount++;
            Logs.Error($"Connection error: {ex}");
            _ = RaiseExceptionAsync(ex);
        }


        public void RegisterHandler(BaseHandler handler)
        {
            handler.Initialize();
            _handlers.Add(handler);
            RebuildHandlerIndex();
        }
        public void RegisterHandler<T>() where T : BaseHandler
        {
            var handler = Activator.CreateInstance(typeof(T), [this]) as BaseHandler;
            RegisterHandler(handler);
        }
        public bool DeregisterHandler<T>(T handler) where T : BaseHandler
        {
            handler?.Dispose();
            var removed = _handlers.Remove(handler);
            if (removed)
                RebuildHandlerIndex();
            return removed;
        }

        private void RebuildHandlerIndex()
        {
            var clientIndex = new Dictionary<MessageID, List<BaseHandler>>();
            var serverIndex = new Dictionary<MessageID, List<BaseHandler>>();
            var clientHasWildcard = false;
            var serverHasWildcard = false;

            foreach (var handler in _handlers)
            {
                clientHasWildcard |= AddSubscriptions(handler, handler.ClientMessageSubscriptions, clientIndex);
                serverHasWildcard |= AddSubscriptions(handler, handler.ServerMessageSubscriptions, serverIndex);
            }

            _clientHandlerIndex = FreezeSubscriptions(clientIndex);
            _serverHandlerIndex = FreezeSubscriptions(serverIndex);
            _clientHasWildcardHandlers = clientHasWildcard;
            _serverHasWildcardHandlers = serverHasWildcard;
        }

        private static bool AddSubscriptions(
            BaseHandler handler,
            IReadOnlyList<MessageID>? subscriptions,
            Dictionary<MessageID, List<BaseHandler>> index)
        {
            if (subscriptions is null)
                return true;

            for (var i = 0; i < subscriptions.Count; i++)
            {
                var messageId = subscriptions[i];
                if (!index.TryGetValue(messageId, out var handlers))
                {
                    handlers = [];
                    index[messageId] = handlers;
                }

                handlers.Add(handler);
            }

            return false;
        }

        private static Dictionary<MessageID, BaseHandler[]> FreezeSubscriptions(Dictionary<MessageID, List<BaseHandler>> source)
        {
            var frozen = new Dictionary<MessageID, BaseHandler[]>(source.Count);
            foreach (var (messageId, handlers) in source)
            {
                frozen[messageId] = [.. handlers];
            }

            return frozen;
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
        public async ValueTask<bool> SendToClientDirectAsync(INetPacket packet, CancellationToken cancellationToken = default)
        {
            using var rental = packet.AsPacketRental(true);
            return await SendToClientDirectAsync(rental.Memory, cancellationToken).ConfigureAwait(false);
        }
        public async ValueTask<bool> SendToServerDirectAsync(INetPacket packet, CancellationToken cancellationToken = default)
        {
            using var rental = packet.AsPacketRental(false);
            return await SendToServerDirectAsync(rental.Memory, cancellationToken).ConfigureAwait(false);
        }

        protected virtual void ConfigureSendPipeline(TcpContainer.SendPipelineContext context)
        {
            // 保留虚方法以便子类扩展，例如追加额外的发送参数
        }
        #endregion
    }
}


