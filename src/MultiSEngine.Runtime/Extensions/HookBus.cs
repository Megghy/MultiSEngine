using System.Collections.Immutable;
using MultiSEngine.Core.Extensions;
using MultiSEngine.Core.Logging;
using TrProtocol;

namespace MultiSEngine.Runtime.Extensions;

public sealed class HookBus(IProxyLogger logger)
{
    private readonly Lock _lock = new();
    private ImmutableArray<Func<PlayerJoiningEvent, CancellationToken, ValueTask>> _joining = [];
    private ImmutableArray<Func<PlayerJoinedEvent, CancellationToken, ValueTask>> _joined = [];
    private ImmutableArray<Func<PlayerLeftEvent, CancellationToken, ValueTask>> _left = [];
    private ImmutableArray<Func<PreSwitchEvent, CancellationToken, ValueTask>> _preSwitch = [];
    private ImmutableArray<Func<PostSwitchEvent, CancellationToken, ValueTask>> _postSwitch = [];
    private ImmutableArray<Func<PlayerChatEvent, CancellationToken, ValueTask>> _chat = [];
    private ImmutableArray<Func<CustomPacketReceivedEvent, CancellationToken, ValueTask>> _customPacket = [];

    public IDisposable SubscribeJoining(Func<PlayerJoiningEvent, CancellationToken, ValueTask> h) => Add(ref _joining, h);
    public IDisposable SubscribeJoined(Func<PlayerJoinedEvent, CancellationToken, ValueTask> h) => Add(ref _joined, h);
    public IDisposable SubscribeLeft(Func<PlayerLeftEvent, CancellationToken, ValueTask> h) => Add(ref _left, h);
    public IDisposable SubscribePreSwitch(Func<PreSwitchEvent, CancellationToken, ValueTask> h) => Add(ref _preSwitch, h);
    public IDisposable SubscribePostSwitch(Func<PostSwitchEvent, CancellationToken, ValueTask> h) => Add(ref _postSwitch, h);
    public IDisposable SubscribeChat(Func<PlayerChatEvent, CancellationToken, ValueTask> h) => Add(ref _chat, h);
    public IDisposable SubscribeCustomPacket(Func<CustomPacketReceivedEvent, CancellationToken, ValueTask> h) => Add(ref _customPacket, h);

    public ValueTask DispatchJoiningAsync(PlayerJoiningEvent e, CancellationToken ct) => DispatchAsync(_joining, e, nameof(PlayerJoiningEvent), ct);
    public ValueTask DispatchJoinedAsync(PlayerJoinedEvent e, CancellationToken ct) => DispatchAsync(_joined, e, nameof(PlayerJoinedEvent), ct);
    public ValueTask DispatchLeftAsync(PlayerLeftEvent e, CancellationToken ct) => DispatchAsync(_left, e, nameof(PlayerLeftEvent), ct);
    public ValueTask DispatchPreSwitchAsync(PreSwitchEvent e, CancellationToken ct) => DispatchAsync(_preSwitch, e, nameof(PreSwitchEvent), ct);
    public ValueTask DispatchPostSwitchAsync(PostSwitchEvent e, CancellationToken ct) => DispatchAsync(_postSwitch, e, nameof(PostSwitchEvent), ct);
    public ValueTask DispatchChatAsync(PlayerChatEvent e, CancellationToken ct) => DispatchAsync(_chat, e, nameof(PlayerChatEvent), ct);
    public ValueTask DispatchCustomPacketAsync(CustomPacketReceivedEvent e, CancellationToken ct) => DispatchAsync(_customPacket, e, nameof(CustomPacketReceivedEvent), ct);

    private UnsubscribeAction Add<TEvent>(ref ImmutableArray<Func<TEvent, CancellationToken, ValueTask>> target, Func<TEvent, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            target = target.Add(handler);
        }

        // Capture removal via field selector delegate to keep unsubscribe allocation-light and safe.
        return new UnsubscribeAction(() => Remove(handler));
    }

    private void Remove<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler)
    {
        lock (_lock)
        {
            switch (handler)
            {
                case Func<PlayerJoiningEvent, CancellationToken, ValueTask> h: _joining = _joining.Remove(h); break;
                case Func<PlayerJoinedEvent, CancellationToken, ValueTask> h: _joined = _joined.Remove(h); break;
                case Func<PlayerLeftEvent, CancellationToken, ValueTask> h: _left = _left.Remove(h); break;
                case Func<PreSwitchEvent, CancellationToken, ValueTask> h: _preSwitch = _preSwitch.Remove(h); break;
                case Func<PostSwitchEvent, CancellationToken, ValueTask> h: _postSwitch = _postSwitch.Remove(h); break;
                case Func<PlayerChatEvent, CancellationToken, ValueTask> h: _chat = _chat.Remove(h); break;
                case Func<CustomPacketReceivedEvent, CancellationToken, ValueTask> h: _customPacket = _customPacket.Remove(h); break;
            }
        }
    }

    private async ValueTask DispatchAsync<TEvent>(
        ImmutableArray<Func<TEvent, CancellationToken, ValueTask>> handlers,
        TEvent eventArgs,
        string eventName,
        CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
        {
            try
            {
                await handler(eventArgs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Error($"Hook handler threw an unhandled exception during {eventName}.", ex, "Hooks");
            }
        }
    }

    internal sealed class UnsubscribeAction(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;

        public void Dispose() => Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }
}

public sealed class MiddlewarePipeline(IProxyLogger logger)
{
    private sealed record CompiledPipeline(
        ImmutableArray<IPacketMiddleware> All,
        bool ClientHasWildcard,
        bool ServerHasWildcard,
        ImmutableDictionary<MessageID, ImmutableArray<IPacketMiddleware>> ClientByMessage,
        ImmutableDictionary<MessageID, ImmutableArray<IPacketMiddleware>> ServerByMessage);

    private readonly Lock _lock = new();
    private readonly List<IPacketMiddleware> _middlewares = [];
    private CompiledPipeline _compiled = new([], false, false, ImmutableDictionary<MessageID, ImmutableArray<IPacketMiddleware>>.Empty, ImmutableDictionary<MessageID, ImmutableArray<IPacketMiddleware>>.Empty);

    public IDisposable Register(IPacketMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        lock (_lock)
        {
            _middlewares.Add(middleware);
            RebuildLocked();
        }

        return new HookBus.UnsubscribeAction(() =>
        {
            lock (_lock)
            {
                if (_middlewares.Remove(middleware))
                    RebuildLocked();
            }
        });
    }

    public bool HasSubscribers(PacketDirection direction, MessageID messageId)
    {
        var snapshot = Volatile.Read(ref _compiled);
        if (snapshot.All.IsEmpty)
            return false;

        return direction == PacketDirection.ClientToServer
            ? snapshot.ClientHasWildcard || snapshot.ClientByMessage.ContainsKey(messageId)
            : snapshot.ServerHasWildcard || snapshot.ServerByMessage.ContainsKey(messageId);
    }

    public async ValueTask ExecuteAsync(PacketMiddlewareContext context, CancellationToken cancellationToken)
    {
        var snapshot = Volatile.Read(ref _compiled);
        if (snapshot.All.IsEmpty)
            return;

        ImmutableArray<IPacketMiddleware> list;
        if (context.Direction == PacketDirection.ClientToServer)
        {
            if (snapshot.ClientHasWildcard)
                list = snapshot.All;
            else if (!snapshot.ClientByMessage.TryGetValue(context.MessageId, out list))
                return;
        }
        else
        {
            if (snapshot.ServerHasWildcard)
                list = snapshot.All;
            else if (!snapshot.ServerByMessage.TryGetValue(context.MessageId, out list))
                return;
        }

        foreach (var middleware in list)
        {
            if (context.Direction == PacketDirection.ClientToServer &&
                middleware.ClientSubscriptions is { } clientSubs &&
                !clientSubs.Contains(context.MessageId))
            {
                continue;
            }

            if (context.Direction == PacketDirection.ServerToClient &&
                middleware.ServerSubscriptions is { } serverSubs &&
                !serverSubs.Contains(context.MessageId))
            {
                continue;
            }

            try
            {
                await middleware.HandleAsync(context, cancellationToken).ConfigureAwait(false);
                if (context.Action == PacketAction.Drop)
                    return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Error($"Packet middleware '{middleware.GetType().Name}' failed on {context.Direction}/{context.MessageId}.", ex, "Middleware");
            }
        }
    }

    private void RebuildLocked()
    {
        var sorted = _middlewares.OrderByDescending(m => m.Priority).ToImmutableArray();
        var clientMap = new Dictionary<MessageID, ImmutableArray<IPacketMiddleware>.Builder>();
        var serverMap = new Dictionary<MessageID, ImmutableArray<IPacketMiddleware>.Builder>();
        var clientWildcard = false;
        var serverWildcard = false;

        foreach (var mw in sorted)
        {
            if (mw.ClientSubscriptions is null)
            {
                clientWildcard = true;
            }
            else
            {
                foreach (var id in mw.ClientSubscriptions)
                {
                    if (!clientMap.TryGetValue(id, out var b))
                        clientMap[id] = b = ImmutableArray.CreateBuilder<IPacketMiddleware>();
                    b.Add(mw);
                }
            }

            if (mw.ServerSubscriptions is null)
            {
                serverWildcard = true;
            }
            else
            {
                foreach (var id in mw.ServerSubscriptions)
                {
                    if (!serverMap.TryGetValue(id, out var b))
                        serverMap[id] = b = ImmutableArray.CreateBuilder<IPacketMiddleware>();
                    b.Add(mw);
                }
            }
        }

        Volatile.Write(
            ref _compiled,
            new CompiledPipeline(
                sorted,
                clientWildcard,
                serverWildcard,
                clientMap.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutable()),
                serverMap.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutable())));
    }
}

public sealed class CustomPacketRegistry
{
    private readonly Lock _lock = new();
    private ImmutableDictionary<string, ICustomPacketDefinition> _definitions =
        ImmutableDictionary<string, ICustomPacketDefinition>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public IDisposable Register(ICustomPacketDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.PacketType);

        lock (_lock)
        {
            _definitions = _definitions.SetItem(definition.PacketType, definition);
        }

        return new HookBus.UnsubscribeAction(() =>
        {
            lock (_lock)
            {
                if (_definitions.TryGetValue(definition.PacketType, out var existing) && ReferenceEquals(existing, definition))
                    _definitions = _definitions.Remove(definition.PacketType);
            }
        });
    }

    public bool TryGet(string packetType, out ICustomPacketDefinition? definition)
        => Volatile.Read(ref _definitions).TryGetValue(packetType, out definition);

    public ImmutableDictionary<string, ICustomPacketDefinition> Snapshot() => Volatile.Read(ref _definitions);
}
