using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Microsoft.Xna.Framework;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Extensions;
using MultiSEngine.Core.Logging;
using MultiSEngine.Core.Security;
using MultiSEngine.Core.Sessions;
using MultiSEngine.Core.Transfers;
using MultiSEngine.Protocol.CustomPackets;
using MultiSEngine.Protocol.Framing;
using MultiSEngine.Protocol.Terraria;
using MultiSEngine.Runtime.Commands;
using MultiSEngine.Runtime.Extensions;
using TrProtocol;
using TrProtocol.NetPackets;
using TrProtocol.NetPackets.Modules;

namespace MultiSEngine.Runtime.Sessions;

internal interface ISessionHostServices
{
    IConfigStore ConfigStore { get; }
    ILocalizationStore Localization { get; }
    IMotdStore Motd { get; }
    ISessionRegistry Registry { get; }
    HookBus Hooks { get; }
    MiddlewarePipeline Middlewares { get; }
    CustomPacketRegistry CustomPackets { get; }
    CustomPacketCodec CustomPacketCodec { get; }
    CommandRouter Commands { get; }
    IProxyLogger Logger { get; }
    ValueTask BroadcastAsync(string text, Color color, string? serverFilter = null, bool withPrefix = true, CancellationToken cancellationToken = default);
}

/// <summary>
/// Single-writer session actor owning the lifetime of one client connection and at most one active upstream connection.
/// Eliminates dual state machines, concurrent transfer races, re-entrant disposal deadlocks, and cross-player data corruption.
/// </summary>
public sealed class PlayerSessionActor : ISessionHandle, IAsyncDisposable
{
    private static readonly AsyncLocal<PlayerSessionActor?> CurrentActorContext = new();

    private abstract record ControlMessage;
    private sealed record ClientFrameMessage(byte[] Frame) : ControlMessage;
    private sealed record ServerFrameMessage(long LinkId, byte[] Frame) : ControlMessage;
    private sealed record TransferRequestMessage(ServerDescriptor Target, TaskCompletionSource<TransferResult> Completion, bool IsFallback = false) : ControlMessage;
    private sealed record TransferHandshakeCompletedMessage(long TransferId, ServerDescriptor Target, UpstreamHandshakeOutcome Outcome, bool IsFallback) : ControlMessage;
    private sealed record TransferPasswordRequestedMessage(long TransferId, bool IsRetry) : ControlMessage;
    private sealed record SubmitPasswordMessage(string Password, TaskCompletionSource<bool> Completion) : ControlMessage;
    private sealed record ReturnBackMessage(string? Reason, TaskCompletionSource<TransferResult> Completion) : ControlMessage;
    private sealed record UpstreamClosedMessage(long LinkId, Exception? Exception) : ControlMessage;
    private sealed record ClientClosedMessage(Exception? Exception) : ControlMessage;
    private sealed record DisconnectRequestMessage(string? Reason, TaskCompletionSource Completion) : ControlMessage;

    private readonly FramedConnection _clientConnection;
    private readonly ISessionHostServices _services;
    private readonly Channel<ControlMessage> _mailbox;
    private readonly PlayerStateTracker _playerState = new();
    private readonly CancellationTokenSource _sessionCts = new();
    private readonly DateTimeOffset _connectedAtUtc = DateTimeOffset.UtcNow;

    private SessionState _state = new SessionState.Handshaking(HandshakeStage.AwaitingHello);
    private FramedConnection? _activeUpstream;
    private long _activeLinkId;
    private long _nextLinkId;
    private long _activeTransferId;
    private TransferHandshakeRunner? _activeTransferRunner;
    private TaskCompletionSource<TransferResult>? _activeTransferCompletion;
    private Task? _actorLoopTask;
    private Task? _handshakeTimeoutTask;
    private int _handshakePacketCounter;
    private long _handshakeWindowStartTick = Environment.TickCount64;
    private int _disposed;

    internal PlayerSessionActor(FramedConnection clientConnection, ISessionHostServices services)
    {
        _clientConnection = clientConnection;
        _services = services;
        SessionId = Guid.NewGuid();
        RemoteEndPoint = clientConnection.RemoteEndPoint;

        var capacity = Math.Max(16, services.ConfigStore.Current.Limits.SessionMailboxCapacity);
        _mailbox = Channel.CreateBounded<ControlMessage>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    public Guid SessionId { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public SessionState CurrentState => Volatile.Read(ref _state);
    public string PlayerName => _playerState.Name;
    public string PlayerUuid => _playerState.Uuid;
    public int ProtocolVersion => _playerState.ProtocolVersion;
    public ServerDescriptor? CurrentServer => CurrentState.CurrentServer;
    public Task Completion => _actorLoopTask ?? Task.CompletedTask;

    public SessionSnapshot GetSnapshot()
    {
        var state = Volatile.Read(ref _state);
        return new SessionSnapshot(
            SessionId,
            _playerState.Name,
            _playerState.Uuid,
            RemoteEndPoint.Address.ToString(),
            RemoteEndPoint.Port,
            _playerState.ProtocolVersion,
            state.Phase,
            state.CurrentServer?.Name,
            state.LastServer?.Name,
            state.ActivePlayerSlot,
            _connectedAtUtc);
    }

    public void Start()
    {
        _services.Registry.TryRegister(this);
        _actorLoopTask = RunActorLoopAsync();
        _handshakeTimeoutTask = MonitorHandshakeTimeoutAsync();

        _clientConnection.SetInboundHandler(OnClientInboundFrameAsync);
        _clientConnection.SetClosedHandler(ex =>
        {
            _mailbox.Writer.TryWrite(new ClientClosedMessage(ex));
            return ValueTask.CompletedTask;
        });
        _clientConnection.Start();
    }

    public async ValueTask<TransferResult> TransferToAsync(ServerDescriptor target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var tcs = new TaskCompletionSource<TransferResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ReferenceEquals(CurrentActorContext.Value, this))
        {
            await HandleTransferRequestAsync(target, tcs).ConfigureAwait(false);
            return tcs.Task.IsCompleted ? await tcs.Task.ConfigureAwait(false) : new TransferResult(TransferStatus.Started, target);
        }

        if (!await TryEnqueueAsync(new TransferRequestMessage(target, tcs), cancellationToken).ConfigureAwait(false))
            return new TransferResult(TransferStatus.SessionClosed, target, "Session is closed.");

        return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryRequestTransfer(ServerDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Volatile.Read(ref _disposed) != 0)
            return false;
        return _mailbox.Writer.TryWrite(new TransferRequestMessage(target, new TaskCompletionSource<TransferResult>(TaskCreationOptions.RunContinuationsAsynchronously)));
    }

    public async ValueTask<TransferResult> ReturnBackAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<TransferResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ReferenceEquals(CurrentActorContext.Value, this))
        {
            await HandleReturnBackAsync(reason, tcs).ConfigureAwait(false);
            return tcs.Task.IsCompleted ? await tcs.Task.ConfigureAwait(false) : new TransferResult(TransferStatus.Started, null, reason);
        }

        if (!await TryEnqueueAsync(new ReturnBackMessage(reason, tcs), cancellationToken).ConfigureAwait(false))
            return new TransferResult(TransferStatus.SessionClosed, null, "Session is closed.");

        return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryRequestReturnBack(string? reason = null)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;
        return _mailbox.Writer.TryWrite(new ReturnBackMessage(reason, new TaskCompletionSource<TransferResult>(TaskCreationOptions.RunContinuationsAsynchronously)));
    }

    public async ValueTask<bool> SubmitPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (ReferenceEquals(CurrentActorContext.Value, this))
        {
            await HandleSubmitPasswordAsync(password, tcs).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }

        if (!await TryEnqueueAsync(new SubmitPasswordMessage(password, tcs), cancellationToken).ConfigureAwait(false))
            return false;

        return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (ReferenceEquals(CurrentActorContext.Value, this))
        {
            await ShutdownSessionAsync(reason, sendKick: true).ConfigureAwait(false);
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!await TryEnqueueAsync(new DisconnectRequestMessage(reason, tcs), cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    public ValueTask SendChatAsync(string text, Color color, bool withPrefix = true, CancellationToken cancellationToken = default)
    {
        var fullText = withPrefix ? $"{_services.Localization.Get("Prefix")}{text}" : text;
        var packet = TerrariaPacketFactory.CreateServerChat(fullText, color);
        return SendPacketToClientAsync(packet, fromServer: true, cancellationToken);
    }

    public async ValueTask SendPacketToClientAsync(INetPacket packet, bool fromServer = true, CancellationToken cancellationToken = default)
        => await _clientConnection.WritePacketAsync(packet, fromServer, cancellationToken).ConfigureAwait(false);

    public async ValueTask SendPacketToServerAsync(INetPacket packet, CancellationToken cancellationToken = default)
    {
        var upstream = Volatile.Read(ref _activeUpstream);
        if (upstream is not null)
            await upstream.WritePacketAsync(packet, fromServer: false, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendRawToClientAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
        => await _clientConnection.WriteFrameAsync(frame, cancellationToken).ConfigureAwait(false);

    public async ValueTask SendRawToServerAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        var upstream = Volatile.Read(ref _activeUpstream);
        if (upstream is not null)
            await upstream.WriteFrameAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fast-path ingress from ClientConnection read loop. Never blocks on transfers or disposes self.
    /// </summary>
    private async ValueTask OnClientInboundFrameAsync(ReadOnlySequence<byte> frame, CancellationToken cancellationToken)
    {
        var state = Volatile.Read(ref _state);
        if (state.Phase is SessionPhase.Disconnecting or SessionPhase.Disconnected)
            return;

        // Handshake rate limiting against flood / TileData amplification attacks.
        if (state.Phase == SessionPhase.Handshaking)
        {
            var now = Environment.TickCount64;
            if (now - _handshakeWindowStartTick >= 1000)
            {
                _handshakeWindowStartTick = now;
                _handshakePacketCounter = 0;
            }

            if (++_handshakePacketCounter > _services.ConfigStore.Current.Limits.MaxHandshakePacketsPerSecond)
            {
                await _mailbox.Writer.WriteAsync(new DisconnectRequestMessage("Handshake packet rate limit exceeded.", new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)), cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var msgId = FastPathClassifier.PeekMessageId(frame);

        // During transfers, drop ordinary gameplay packets while keeping NetModules (chat/password commands) responsive!
        if (state.Phase is SessionPhase.TransferConnecting or SessionPhase.TransferAwaitingPassword or SessionPhase.TransferSyncing or SessionPhase.ReturningToLobby)
        {
            if (msgId != MessageID.NetModules && msgId != MessageID.SendPassword)
                return;
        }

        // Zero-copy fast pass-through for unsubscribed gameplay frames in InServer state.
        if (state.Phase == SessionPhase.InServer)
        {
            var hasMw = _services.Middlewares.HasSubscribers(PacketDirection.ClientToServer, msgId);
            if (FastPathClassifier.ClassifyClientFrame(frame, hasMw) == InboundRouteDecision.FastPassThrough)
            {
                var upstream = Volatile.Read(ref _activeUpstream);
                if (upstream is not null)
                {
                    var writeTask = upstream.WriteFrameAsync(frame, cancellationToken);
                    if (!writeTask.IsCompletedSuccessfully)
                        await writeTask.ConfigureAwait(false);
                }
                return;
            }
        }

        var copy = PacketFrameCodec.CopyToArray(frame);
        await _mailbox.Writer.WriteAsync(new ClientFrameMessage(copy), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fast-path ingress from active Upstream FramedConnection.
    /// Filters other players' broadcast packets in O(1) using Span[3] without deserialization.
    /// </summary>
    private async ValueTask OnServerInboundFrameAsync(long linkId, ReadOnlySequence<byte> frame, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _activeLinkId) != linkId)
            return;

        var state = Volatile.Read(ref _state);
        if (state.Phase != SessionPhase.InServer)
            return;

        var msgId = FastPathClassifier.PeekMessageId(frame);
        var hasMw = _services.Middlewares.HasSubscribers(PacketDirection.ServerToClient, msgId);

        if (FastPathClassifier.ClassifyServerFrame(frame, state.ActivePlayerSlot, hasMw) == InboundRouteDecision.FastPassThrough)
        {
            var writeTask = _clientConnection.WriteFrameAsync(frame, cancellationToken);
            if (!writeTask.IsCompletedSuccessfully)
                await writeTask.ConfigureAwait(false);
            return;
        }

        var copy = PacketFrameCodec.CopyToArray(frame);
        await _mailbox.Writer.WriteAsync(new ServerFrameMessage(linkId, copy), cancellationToken).ConfigureAwait(false);
    }

    private async Task RunActorLoopAsync()
    {
        CurrentActorContext.Value = this;
        try
        {
            await foreach (var message in _mailbox.Reader.ReadAllAsync(_sessionCts.Token).ConfigureAwait(false))
            {
                switch (message)
                {
                    case ClientFrameMessage m:
                        await HandleClientFrameAsync(m.Frame).ConfigureAwait(false);
                        break;
                    case ServerFrameMessage m:
                        await HandleServerFrameAsync(m.LinkId, m.Frame).ConfigureAwait(false);
                        break;
                    case TransferRequestMessage m:
                        await HandleTransferRequestAsync(m.Target, m.Completion, m.IsFallback).ConfigureAwait(false);
                        break;
                    case TransferPasswordRequestedMessage m:
                        await HandlePasswordRequestedAsync(m.TransferId, m.IsRetry).ConfigureAwait(false);
                        break;
                    case SubmitPasswordMessage m:
                        await HandleSubmitPasswordAsync(m.Password, m.Completion).ConfigureAwait(false);
                        break;
                    case TransferHandshakeCompletedMessage m:
                        await HandleTransferCompletedAsync(m.TransferId, m.Target, m.Outcome, m.IsFallback).ConfigureAwait(false);
                        break;
                    case ReturnBackMessage m:
                        await HandleReturnBackAsync(m.Reason, m.Completion).ConfigureAwait(false);
                        break;
                    case UpstreamClosedMessage m:
                        await HandleUpstreamClosedAsync(m.LinkId, m.Exception).ConfigureAwait(false);
                        break;
                    case ClientClosedMessage m:
                        await ShutdownSessionAsync(m.Exception?.Message ?? "Client closed connection.", sendKick: false).ConfigureAwait(false);
                        return;
                    case DisconnectRequestMessage m:
                        await ShutdownSessionAsync(m.Reason, sendKick: true).ConfigureAwait(false);
                        m.Completion.TrySetResult();
                        return;
                }

                if (_state.Phase == SessionPhase.Disconnected)
                    return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _services.Logger.Error($"Unhandled exception in session actor [{PlayerName} / {SessionId}].", ex, "Session");
            await ShutdownSessionAsync("Internal proxy error.", sendKick: true).ConfigureAwait(false);
        }
        finally
        {
            CurrentActorContext.Value = null;
            await FinalizeResourcesAsync().ConfigureAwait(false);
        }
    }

    private async Task MonitorHandshakeTimeoutAsync()
    {
        try
        {
            var timeoutMs = _services.ConfigStore.Current.HandshakeTimeoutMs;
            await Task.Delay(timeoutMs, _sessionCts.Token).ConfigureAwait(false);
            if (Volatile.Read(ref _state).Phase == SessionPhase.Handshaking)
            {
                _services.Logger.Warn($"Handshake timed out for {RemoteEndPoint}.", "Session");
                await DisconnectAsync("Handshake timed out.").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async ValueTask HandleClientFrameAsync(byte[] rawFrame)
    {
        var msgId = (MessageID)rawFrame[2];
        var state = _state;

        // 1. Handshaking phase: strictly enforce Terraria handshake order.
        if (state is SessionState.Handshaking handshaking)
        {
            await HandleHandshakeClientFrameAsync(handshaking.Stage, msgId, rawFrame).ConfigureAwait(false);
            return;
        }

        // 2. Security boundary: never allow arbitrary client-forged Unused15 custom packets to reach backend servers.
        if (msgId == MessageID.Unused15)
        {
            await HandleCustomPacketFrameAsync(PacketDirection.ClientToServer, rawFrame, sourceServer: null).ConfigureAwait(false);
            return;
        }

        // 3. Direct password packet while awaiting password.
        if (msgId == MessageID.SendPassword && state.Phase == SessionPhase.TransferAwaitingPassword && _activeTransferRunner is not null)
        {
            var pwd = (SendPassword)PacketFrameCodec.Deserialize(rawFrame, fromServer: false);
            await _activeTransferRunner.SubmitPasswordAsync(pwd.Password ?? string.Empty, _sessionCts.Token).ConfigureAwait(false);
            return;
        }

        // 4. Run plugin middleware pipeline.
        var mwContext = new PacketMiddlewareContext(
            this,
            PacketDirection.ClientToServer,
            msgId,
            rawFrame,
            () => PacketFrameCodec.Deserialize(rawFrame, fromServer: false));

        await _services.Middlewares.ExecuteAsync(mwContext, _sessionCts.Token).ConfigureAwait(false);
        if (mwContext.Action == PacketAction.Drop)
            return;

        ReadOnlyMemory<byte> effectiveFrame = rawFrame;
        if (mwContext.Action == PacketAction.Replace)
        {
            effectiveFrame = mwContext.ReplacementPacket is not null
                ? PacketFrameCodec.SerializeToArray(mwContext.ReplacementPacket, fromServer: false)
                : mwContext.ReplacementFrame;
            msgId = (MessageID)effectiveFrame.Span[2];
        }

        // 5. Chat & command handling (NetTextModule).
        if (msgId == MessageID.NetModules)
        {
            var packet = PacketFrameCodec.Deserialize(effectiveFrame.Span, fromServer: false);
            if (packet is NetTextModule { TextC2S: { } textC2S })
            {
                var intercepted = await HandleClientChatAsync(textC2S, state).ConfigureAwait(false);
                if (intercepted)
                    return;

                // Re-serialize if a hook modified TextC2S.Text.
                effectiveFrame = PacketFrameCodec.SerializeToArray(packet, fromServer: false);
            }
        }
        // 6. Player identity & character tracking.
        else if (IsTrackedClientPacket(msgId))
        {
            var packet = PacketFrameCodec.Deserialize(effectiveFrame.Span, fromServer: false);
            var validation = _playerState.ApplyClientPacket(packet, inTargetServer: state.Phase == SessionPhase.InServer);
            if (validation == IdentityValidationResult.NameMismatch)
            {
                await ShutdownSessionAsync("You cannot change your name.", sendKick: true).ConfigureAwait(false);
                return;
            }
            if (validation == IdentityValidationResult.UuidMismatch)
            {
                await ShutdownSessionAsync("You cannot change your UUID.", sendKick: true).ConfigureAwait(false);
                return;
            }
        }

        // 7. Forward to active upstream only when actually InServer.
        if (state.Phase == SessionPhase.InServer && _activeUpstream is { } upstream)
        {
            await upstream.WriteFrameAsync(effectiveFrame, _sessionCts.Token).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleHandshakeClientFrameAsync(HandshakeStage stage, MessageID msgId, byte[] rawFrame)
    {
        var config = _services.ConfigStore.Current;

        switch (msgId)
        {
            case MessageID.ClientHello:
            {
                if (stage != HandshakeStage.AwaitingHello)
                {
                    await ShutdownSessionAsync("Duplicate ClientHello during handshake.", sendKick: true).ConfigureAwait(false);
                    return;
                }

                var hello = (ClientHello)PacketFrameCodec.Deserialize(rawFrame, fromServer: false);
                var joiningEvent = new PlayerJoiningEvent(this, RemoteEndPoint.Address.ToString(), RemoteEndPoint.Port, hello.Version ?? string.Empty);
                await _services.Hooks.DispatchJoiningAsync(joiningEvent, _sessionCts.Token).ConfigureAwait(false);
                if (joiningEvent.Cancel)
                {
                    await ShutdownSessionAsync(joiningEvent.CancelReason ?? "Connection rejected.", sendKick: true).ConfigureAwait(false);
                    return;
                }

                if (!TerrariaVersions.TryParseClientHelloVersion(joiningEvent.RawVersion, out var versionNum))
                    versionNum = config.ServerVersion;

                _playerState.ProtocolVersion = versionNum;
                if (versionNum < TerrariaVersions.MinimumSupportedVersion ||
                    (versionNum != config.ServerVersion && !config.EnableCrossplay))
                {
                    var label = $"{TerrariaVersions.ToVersionString(versionNum)} ({versionNum})";
                    await ShutdownSessionAsync(_services.Localization.Format("Prompt_VersionNotAllowed", label), sendKick: true).ConfigureAwait(false);
                    return;
                }

                _services.Logger.Info($"Version of {RemoteEndPoint} is {TerrariaVersions.ToVersionString(versionNum)}<{versionNum}>.", "Handshake");
                TransitionTo(new SessionState.Handshaking(HandshakeStage.AwaitingWorldInfo));
                await _clientConnection.WritePacketAsync(new LoadPlayer
                {
                    PlayerSlot = 0,
                    ServerWantsToRunCheckBytesInClientLoopThread = false,
                }, fromServer: true, _sessionCts.Token).ConfigureAwait(false);
                return;
            }

            case MessageID.SyncPlayer:
            case MessageID.SyncEquipment:
            case MessageID.SyncLoadout:
            case MessageID.PlayerHealth:
            case MessageID.PlayerMana:
            case MessageID.PlayerBuffs:
            case MessageID.PlayerTeam:
            case MessageID.PlayerPvP:
            case MessageID.PlayerZone:
            case MessageID.PlayerStealth:
            case MessageID.UpdatePlayerLuckFactors:
            case MessageID.ClientUUID:
            {
                if (stage == HandshakeStage.AwaitingHello)
                    return;

                var packet = PacketFrameCodec.Deserialize(rawFrame, fromServer: false);
                _playerState.ApplyClientPacket(packet, inTargetServer: false);
                return;
            }

            case MessageID.RequestWorldInfo:
            {
                if (stage != HandshakeStage.AwaitingWorldInfo)
                    return;

                var lobbyWorld = LobbyProtocol.CreateLobbyWorldData(config.ServerName);
                _playerState.OriginCharacter.WorldData = lobbyWorld;
                TransitionTo(new SessionState.Handshaking(HandshakeStage.AwaitingTileData));
                await _clientConnection.WritePacketAsync(lobbyWorld, fromServer: true, _sessionCts.Token).ConfigureAwait(false);
                return;
            }

            case MessageID.RequestTileData:
            {
                if (stage != HandshakeStage.AwaitingTileData)
                    return;

                TransitionTo(new SessionState.Handshaking(HandshakeStage.AwaitingSpawn));
                await _clientConnection.WritePacketAsync(new StatusText
                {
                    Max = 1,
                    Text = TerrariaPacketFactory.LiteralText("Receiving tile data"),
                    Flag = 0,
                }, fromServer: true, _sessionCts.Token).ConfigureAwait(false);
                await _clientConnection.WriteFrameAsync(LobbyProtocol.SpawnSquarePacket, _sessionCts.Token).ConfigureAwait(false);
                await _clientConnection.WritePacketAsync(new StartPlaying(), fromServer: true, _sessionCts.Token).ConfigureAwait(false);
                return;
            }

            case MessageID.SpawnPlayer:
            {
                if (stage != HandshakeStage.AwaitingSpawn)
                {
                    await ShutdownSessionAsync("Protocol violation: premature SpawnPlayer.", sendKick: true).ConfigureAwait(false);
                    return;
                }

                var spawn = (SpawnPlayer)PacketFrameCodec.Deserialize(rawFrame, fromServer: false);
                _playerState.ApplyClientPacket(spawn, inTargetServer: false);
                _playerState.LockIdentity();

                TransitionTo(new SessionState.Lobby(LastServerInfo: null));
                await _clientConnection.WritePacketAsync(spawn, fromServer: true, _sessionCts.Token).ConfigureAwait(false);
                await _clientConnection.WritePacketAsync(new FinishedConnectingToServer(), fromServer: true, _sessionCts.Token).ConfigureAwait(false);

                var snapshots = _services.Registry.GetSnapshots();
                var motd = _services.Motd.Render(
                    config.ServerName,
                    snapshots.Length,
                    snapshots.Where(s => !string.IsNullOrEmpty(s.PlayerName)).Select(s => s.PlayerName),
                    config.Servers.Where(s => s.Visible).Select(s => s.Name));

                await SendChatAsync(motd, TerrariaPacketFactory.Rgb(255, 255, 255), withPrefix: false, _sessionCts.Token).ConfigureAwait(false);
                await _services.Hooks.DispatchJoinedAsync(new PlayerJoinedEvent(this), _sessionCts.Token).ConfigureAwait(false);
                _services.Logger.Info($"[{PlayerName}] joined the proxy from {RemoteEndPoint}.", "Session");

                // Notify other players in the lobby.
                foreach (var other in _services.Registry.GetSessions())
                {
                    if (other.SessionId != SessionId && other.CurrentState.Phase == SessionPhase.Lobby)
                        await other.SendChatAsync($"{PlayerName} has joined.", TerrariaPacketFactory.Rgb(255, 255, 255), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
                }

                if (config.SwitchToDefaultServerOnJoin)
                {
                    if (config.DefaultServerDescriptor is { } defaultServer)
                    {
                        await SendChatAsync(_services.Localization.Format("Command_Switch", defaultServer.Name), TerrariaPacketFactory.Rgb(220, 220, 130), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
                        await HandleTransferRequestAsync(defaultServer, new TaskCompletionSource<TransferResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendChatAsync(_services.Localization.Format("Prompt_DefaultServerNotFound", config.DefaultServer), TerrariaPacketFactory.Rgb(220, 220, 130), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    _services.Logger.Text($"[{PlayerName}] is waiting in Lobby.", "Session");
                }
                return;
            }
        }
    }

    private async ValueTask<bool> HandleClientChatAsync(TextC2S textC2S, SessionState state)
    {
        var command = textC2S.Command ?? string.Empty;
        var text = textC2S.Text ?? string.Empty;

        // 1. Proxy slash commands are always evaluated first (even while awaiting password!).
        if (string.Equals(command, "Say", StringComparison.OrdinalIgnoreCase))
        {
            var cmdResult = await _services.Commands.DispatchAsync(
                CommandSourceKind.Player,
                text,
                playerSession: this,
                cancellationToken: _sessionCts.Token).ConfigureAwait(false);

            if (cmdResult.Handled && !cmdResult.ForwardToServer)
                return true;
        }

        // 2. Dispatch plugin chat hook (mutated Message IS written back to TextC2S.Text!).
        var chatEvent = new PlayerChatEvent(this, command, text);
        await _services.Hooks.DispatchChatAsync(chatEvent, _sessionCts.Token).ConfigureAwait(false);
        if (chatEvent.Handled)
            return true;

        textC2S.Text = chatEvent.Message;

        // 3. Cross-server chat broadcast and Lobby local chat.
        var config = _services.ConfigStore.Current;
        if (string.Equals(command, "Say", StringComparison.OrdinalIgnoreCase) && !chatEvent.Message.StartsWith('/'))
        {
            var originServerName = state.CurrentServer?.Name ?? config.ServerName;
            var formatted = FormatChatTemplate(config.ChatFormat, originServerName, PlayerName, chatEvent.Message);

            if (state.Phase == SessionPhase.Lobby)
            {
                await _services.BroadcastAsync(formatted, TerrariaPacketFactory.Rgb(255, 255, 255), serverFilter: null, withPrefix: false, _sessionCts.Token).ConfigureAwait(false);
                return true;
            }

            if (config.EnableChatForward && state.Phase == SessionPhase.InServer)
            {
                foreach (var other in _services.Registry.GetSessions())
                {
                    if (other.SessionId == SessionId)
                        continue;
                    if (other.CurrentServer is { } otherSrv &&
                        state.CurrentServer is { } mySrv &&
                        string.Equals(otherSrv.Name, mySrv.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    await other.SendChatAsync(formatted, TerrariaPacketFactory.Rgb(255, 255, 255), withPrefix: false, _sessionCts.Token).ConfigureAwait(false);
                }
            }
        }

        return state.Phase != SessionPhase.InServer;
    }

    private async ValueTask HandleServerFrameAsync(long linkId, byte[] rawFrame)
    {
        if (linkId != _activeLinkId || _state is not SessionState.InServer inServer)
            return;

        var msgId = (MessageID)rawFrame[2];

        // 1. Kick from backend server triggers graceful fallback instead of raw disconnect.
        if (msgId == MessageID.Kick)
        {
            var kick = (Kick)PacketFrameCodec.Deserialize(rawFrame, fromServer: true);
            var reason = kick.Reason.ToString();
            _services.Logger.Info($"[{PlayerName}] kicked by [{inServer.Server.Name}]: {reason}", "Session");
            await SendChatAsync(_services.Localization.Format("Prompt_Disconnect", inServer.Server.Name, reason), TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
            await HandleReturnBackAsync(reason, new TaskCompletionSource<TransferResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ConfigureAwait(false);
            return;
        }

        // 2. Authenticated v2 Custom Packet from backend server.
        if (msgId == MessageID.Unused15)
        {
            await HandleCustomPacketFrameAsync(PacketDirection.ServerToClient, rawFrame, inServer.Server).ConfigureAwait(false);
            return;
        }

        // 3. Plugin middleware pipeline.
        var mwContext = new PacketMiddlewareContext(
            this,
            PacketDirection.ServerToClient,
            msgId,
            rawFrame,
            () => PacketFrameCodec.Deserialize(rawFrame, fromServer: true));

        await _services.Middlewares.ExecuteAsync(mwContext, _sessionCts.Token).ConfigureAwait(false);
        if (mwContext.Action == PacketAction.Drop)
            return;

        ReadOnlyMemory<byte> effectiveFrame = rawFrame;
        if (mwContext.Action == PacketAction.Replace)
        {
            effectiveFrame = mwContext.ReplacementPacket is not null
                ? PacketFrameCodec.SerializeToArray(mwContext.ReplacementPacket, fromServer: true)
                : mwContext.ReplacementFrame;
            msgId = (MessageID)effectiveFrame.Span[2];
        }

        // 4. Track only this session's own player slot state (never other players!).
        if (IsTrackedServerPacket(msgId))
        {
            var packet = PacketFrameCodec.Deserialize(effectiveFrame.Span, fromServer: true);
            if (_playerState.ApplyServerPacket(packet, inServer.RemotePlayerSlot) && packet is LoadPlayer load)
            {
                if (load.PlayerSlot != inServer.RemotePlayerSlot)
                    TransitionTo(inServer with { RemotePlayerSlot = load.PlayerSlot });
            }
        }

        await _clientConnection.WriteFrameAsync(effectiveFrame, _sessionCts.Token).ConfigureAwait(false);
    }

    private async ValueTask HandleCustomPacketFrameAsync(PacketDirection direction, byte[] rawFrame, ServerDescriptor? sourceServer)
    {
        var config = _services.ConfigStore.Current;
        var secret = direction == PacketDirection.ServerToClient
            ? (sourceServer?.SharedSecret ?? config.CustomPackets.GlobalSharedSecret)
            : config.CustomPackets.GlobalSharedSecret;

        var decoded = _services.CustomPacketCodec.Decode(
            rawFrame,
            expectedSecret: secret,
            maxClockSkew: TimeSpan.FromSeconds(config.CustomPackets.MaxClockSkewSeconds),
            allowUnsigned: config.CustomPackets.AllowUnsignedInDevelopment);

        if (!decoded.IsSuccess || decoded.Envelope is null)
        {
            _services.Logger.Warn($"Rejected {direction} custom packet on [{PlayerName}]: {decoded.Status} ({decoded.Error}).", "Security");
            return;
        }

        var env = decoded.Envelope;
        if (!_services.CustomPackets.TryGet(env.PacketType, out var definition) || definition is null)
        {
            // Still dispatch hook only if authenticated from server.
            if (direction == PacketDirection.ServerToClient)
            {
                var ctx = new CustomPacketContext(this, direction, env.PacketType, env.Payload, env.IsAuthenticated, sourceServer);
                await _services.Hooks.DispatchCustomPacketAsync(new CustomPacketReceivedEvent(ctx), _sessionCts.Token).ConfigureAwait(false);
            }
            return;
        }

        if (direction == PacketDirection.ClientToServer && !definition.AllowFromClient)
        {
            _services.Logger.Warn($"Blocked client-forged custom packet '{env.PacketType}' from [{PlayerName}].", "Security");
            return;
        }

        if (direction == PacketDirection.ServerToClient && !definition.AllowFromServer)
            return;

        var customCtx = new CustomPacketContext(this, direction, env.PacketType, env.Payload, env.IsAuthenticated, sourceServer);
        var hookEvent = new CustomPacketReceivedEvent(customCtx);
        await _services.Hooks.DispatchCustomPacketAsync(hookEvent, _sessionCts.Token).ConfigureAwait(false);
        if (hookEvent.Handled)
            return;

        try
        {
            await definition.HandleAsync(customCtx, _sessionCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _services.Logger.Error($"Custom packet handler '{env.PacketType}' failed.", ex, "CustomPacket");
        }
    }

    private async ValueTask HandleTransferRequestAsync(ServerDescriptor target, TaskCompletionSource<TransferResult> completion, bool isFallback = false)
    {
        var state = _state;
        if (state.Phase is not (SessionPhase.Lobby or SessionPhase.InServer))
        {
            completion.TrySetResult(new TransferResult(TransferStatus.InvalidState, target, "Cannot start transfer in current session state."));
            return;
        }

        if (state.CurrentServer is { } current &&
            string.Equals(current.Name, target.Name, StringComparison.OrdinalIgnoreCase))
        {
            await SendChatAsync(_services.Localization.Format("Command_AlreadyIn", target.Name), TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
            completion.TrySetResult(new TransferResult(TransferStatus.AlreadyInTarget, target, "Already in target server."));
            return;
        }

        var preSwitch = new PreSwitchEvent(this, target);
        await _services.Hooks.DispatchPreSwitchAsync(preSwitch, _sessionCts.Token).ConfigureAwait(false);
        if (preSwitch.Cancel)
        {
            completion.TrySetResult(new TransferResult(TransferStatus.CancelledByHook, preSwitch.TargetServer, preSwitch.CancelReason));
            return;
        }

        var effectiveTarget = preSwitch.TargetServer;
        var transferId = Interlocked.Increment(ref _activeTransferId);
        var previousServer = state.CurrentServer;
        var lastServer = state.LastServer;
        var currentSlot = state.ActivePlayerSlot;

        TransitionTo(new SessionState.TransferConnecting(effectiveTarget, previousServer, lastServer, currentSlot, transferId));
        _activeTransferCompletion = completion;

        var runner = new TransferHandshakeRunner(
            effectiveTarget,
            _services.ConfigStore.Current,
            _playerState,
            RemoteEndPoint.Address.ToString(),
            _services.CustomPacketCodec,
            _services.Logger,
            onPasswordRequested: isRetry =>
            {
                _mailbox.Writer.TryWrite(new TransferPasswordRequestedMessage(transferId, isRetry));
                return ValueTask.CompletedTask;
            },
            onCrowdControlBuff: () => SendPacketToClientAsync(TerrariaPacketFactory.CreateBuff(currentSlot, 149, 60), fromServer: true, _sessionCts.Token));

        _activeTransferRunner = runner;
        _services.Logger.Info($"Switching [{PlayerName}] -> [{effectiveTarget.Name}] (transfer #{transferId}).", "Transfer");

        // Run handshake asynchronously off the actor mailbox so password prompts & disconnects remain responsive!
        _ = Task.Run(async () =>
        {
            UpstreamHandshakeOutcome outcome;
            try
            {
                outcome = await runner.RunAsync(_sessionCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outcome = UpstreamHandshakeOutcome.Failed(TransferStatus.ConnectionFailed, ex.Message);
            }

            if (!_mailbox.Writer.TryWrite(new TransferHandshakeCompletedMessage(transferId, effectiveTarget, outcome, isFallback)))
                await outcome.DisposeUnusedAsync().ConfigureAwait(false);
        });
    }

    private async ValueTask HandlePasswordRequestedAsync(long transferId, bool isRetry)
    {
        if (transferId != _activeTransferId)
            return;

        if (_state is SessionState.TransferConnecting connecting)
        {
            TransitionTo(new SessionState.TransferAwaitingPassword(
                connecting.TargetServer,
                connecting.PreviousServer,
                connecting.LastServerInfo,
                connecting.CurrentSlot,
                transferId,
                RetryPrompted: isRetry));
        }
        else if (_state is SessionState.TransferAwaitingPassword awaiting)
        {
            TransitionTo(awaiting with { RetryPrompted = true });
        }
        else
        {
            return;
        }

        var targetName = _state switch
        {
            SessionState.TransferAwaitingPassword p => p.TargetServer.Name,
            _ => string.Empty,
        };

        var msg = isRetry
            ? _services.Localization.Format("Prompt_WrongPassword", targetName)
            : _services.Localization.Format("Prompt_NeedPassword", targetName, _services.Localization.Get("Help_Password"));
        await SendChatAsync(msg, TerrariaPacketFactory.Rgb(220, 220, 130), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
    }

    private async ValueTask HandleSubmitPasswordAsync(string password, TaskCompletionSource<bool> completion)
    {
        if (_state.Phase != SessionPhase.TransferAwaitingPassword || _activeTransferRunner is null)
        {
            completion.TrySetResult(false);
            return;
        }

        var ok = await _activeTransferRunner.SubmitPasswordAsync(password, _sessionCts.Token).ConfigureAwait(false);
        completion.TrySetResult(ok);
    }

    private async ValueTask HandleTransferCompletedAsync(long transferId, ServerDescriptor target, UpstreamHandshakeOutcome outcome, bool isFallback)
    {
        if (transferId != _activeTransferId || _state.Phase is not (SessionPhase.TransferConnecting or SessionPhase.TransferAwaitingPassword))
        {
            await outcome.DisposeUnusedAsync().ConfigureAwait(false);
            return;
        }

        var runner = _activeTransferRunner;
        var completion = _activeTransferCompletion;
        _activeTransferRunner = null;
        _activeTransferCompletion = null;

        if (runner is not null)
            await runner.DisposeAsync().ConfigureAwait(false);

        var previousServer = _state.CurrentServer;
        var lastServer = _state.LastServer;

        if (!outcome.IsSuccess || outcome.WorldData is not { } worldData)
        {
            await outcome.DisposeUnusedAsync().ConfigureAwait(false);
            RestoreStableState(previousServer, lastServer);

            var errorPrompt = outcome.Status switch
            {
                TransferStatus.InvalidAddress => _services.Localization.Get("Prompt_UnknownAddress"),
                TransferStatus.PasswordTimedOut => _services.Localization.Get("Prompt_PasswordTimeout"),
                TransferStatus.KickedByTarget => _services.Localization.Format("Prompt_Disconnect", target.Name, outcome.FailureMessage ?? string.Empty),
                _ => _services.Localization.Format("Prompt_CannotConnect", target.Name),
            };

            await SendChatAsync(errorPrompt, TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
            completion?.TrySetResult(new TransferResult(outcome.Status, target, outcome.FailureMessage));
            return;
        }

        // Atomic sync & swap phase.
        TransitionTo(new SessionState.TransferSyncing(target, previousServer, outcome.RemotePlayerSlot, transferId));

        FramedConnection? newUpstream = null;
        try
        {
            _playerState.ApplyTargetHandshake(worldData, outcome.RemotePlayerSlot, outcome.SpawnX, outcome.SpawnY);

            using var syncBatch = new PooledBufferWriter(16 * 1024);
            TerrariaSyncEngine.BuildTargetSwitchBatch(
                syncBatch,
                _playerState,
                worldData,
                outcome.RemotePlayerSlot,
                outcome.SpawnX,
                outcome.SpawnY,
                outcome.BufferedPackets,
                _services.ConfigStore.Current.RestoreDataWhenJoinNonSsc);

            var sent = await _clientConnection.WriteFrameAsync(syncBatch.WrittenMemory, _sessionCts.Token).ConfigureAwait(false);
            if (!sent)
                throw new IOException("Failed to deliver synchronization batch to client.");

            // Dispose previous upstream before activating the new one.
            var oldUpstream = Interlocked.Exchange(ref _activeUpstream, null);
            if (oldUpstream is not null)
                await oldUpstream.DisposeAsync().ConfigureAwait(false);

            newUpstream = outcome.TakeUpstreamOwnership();
            var newLinkId = Interlocked.Increment(ref _nextLinkId);
            Volatile.Write(ref _activeLinkId, newLinkId);
            Volatile.Write(ref _activeUpstream, newUpstream);

            newUpstream.SetInboundHandler((frame, ct) => OnServerInboundFrameAsync(newLinkId, frame, ct));
            newUpstream.SetClosedHandler(ex =>
            {
                _mailbox.Writer.TryWrite(new UpstreamClosedMessage(newLinkId, ex));
                return ValueTask.CompletedTask;
            });

            // On fallback return (e.g. kicked from B back to A), do NOT record B as the next fallback target to prevent A<->B ping-pong loops.
            var nextPreviousServer = isFallback ? null : previousServer;
            TransitionTo(new SessionState.InServer(target, nextPreviousServer, outcome.RemotePlayerSlot, newLinkId));

            // Open the read gate so all subsequent target server frames flow immediately with zero packet loss.
            newUpstream.ResumeReading();

            // PostSwitch runs ONLY after state and connection ownership are completely switched!
            await _services.Hooks.DispatchPostSwitchAsync(new PostSwitchEvent(this, target, previousServer), _sessionCts.Token).ConfigureAwait(false);
            await SendChatAsync(_services.Localization.Format("Prompt_ConnectSuccess", target.Name), TerrariaPacketFactory.Rgb(120, 230, 140), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
            _services.Logger.Success($"[{PlayerName}] switched to [{target.Name}] (slot={outcome.RemotePlayerSlot}).", "Transfer");
            completion?.TrySetResult(new TransferResult(TransferStatus.Succeeded, target));
        }
        catch (Exception ex)
        {
            _services.Logger.Error($"Fatal sync error switching [{PlayerName}] to [{target.Name}].", ex, "Transfer");
            if (newUpstream is not null && !ReferenceEquals(newUpstream, _activeUpstream))
                await newUpstream.DisposeAsync().ConfigureAwait(false);
            await outcome.DisposeUnusedAsync().ConfigureAwait(false);
            completion?.TrySetResult(new TransferResult(TransferStatus.SyncFailed, target, ex.Message));
            await ShutdownSessionAsync(_services.Localization.Format("Prompt_CannotConnect", target.Name), sendKick: true).ConfigureAwait(false);
        }
        finally
        {
            outcome.Dispose();
        }
    }

    private async ValueTask HandleReturnBackAsync(string? reason, TaskCompletionSource<TransferResult> completion)
    {
        var state = _state;
        if (state is not SessionState.InServer inServer)
        {
            completion.TrySetResult(new TransferResult(TransferStatus.InvalidState, null, "Not currently inside a backend server."));
            return;
        }

        // Priority: 1. LastServer (if different from current) -> 2. DefaultServer (if different from current) -> 3. Lobby.
        var config = _services.ConfigStore.Current;
        ServerDescriptor? fallback = null;
        if (inServer.PreviousServer is { } prev &&
            !string.Equals(prev.Name, inServer.Server.Name, StringComparison.OrdinalIgnoreCase))
        {
            fallback = prev;
        }
        else if (config.DefaultServerDescriptor is { } def &&
            !string.Equals(def.Name, inServer.Server.Name, StringComparison.OrdinalIgnoreCase))
        {
            fallback = def;
        }

        if (fallback is not null)
        {
            await HandleTransferRequestAsync(fallback, completion, isFallback: true).ConfigureAwait(false);
            return;
        }

        // Return cleanly to Lobby world.
        TransitionTo(new SessionState.ReturningToLobby(inServer.Server, inServer.PreviousServer));
        try
        {
            var oldUpstream = Interlocked.Exchange(ref _activeUpstream, null);
            Volatile.Write(ref _activeLinkId, 0);
            if (oldUpstream is not null)
                await oldUpstream.DisposeAsync().ConfigureAwait(false);

            _playerState.ResetToLobby();
            using var batch = new PooledBufferWriter(16 * 1024);
            TerrariaSyncEngine.BuildReturnToLobbyBatch(
                batch,
                _playerState,
                config.ServerName,
                config.RestoreDataWhenJoinNonSsc);

            var sent = await _clientConnection.WriteFrameAsync(batch.WrittenMemory, _sessionCts.Token).ConfigureAwait(false);
            if (!sent)
                throw new IOException("Failed to send lobby return batch to client.");

            TransitionTo(new SessionState.Lobby(LastServerInfo: null));
            await SendChatAsync(_services.Localization.Get("Prompt_NoAvailableServer"), TerrariaPacketFactory.Rgb(220, 220, 130), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
            _services.Logger.Info($"[{PlayerName}] returned to Lobby ({reason ?? "manual/fallback"}).", "Session");
            completion.TrySetResult(new TransferResult(TransferStatus.Succeeded, null, "Returned to lobby."));
        }
        catch (Exception ex)
        {
            _services.Logger.Error($"Failed returning [{PlayerName}] to Lobby.", ex, "Session");
            completion.TrySetResult(new TransferResult(TransferStatus.SyncFailed, null, ex.Message));
            await ShutdownSessionAsync(_services.Localization.Get("Prompt_UnknownError"), sendKick: true).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleUpstreamClosedAsync(long linkId, Exception? exception)
    {
        if (linkId != _activeLinkId)
            return;

        if (_state is SessionState.InServer inServer)
        {
            var reason = exception?.Message ?? "Backend server closed connection.";
            _services.Logger.Warn($"Upstream [{inServer.Server.Name}] disconnected for [{PlayerName}]: {reason}", "Session");
            await SendChatAsync(_services.Localization.Format("Prompt_Disconnect", inServer.Server.Name, reason), TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, _sessionCts.Token).ConfigureAwait(false);
            await HandleReturnBackAsync(reason, new TaskCompletionSource<TransferResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ConfigureAwait(false);
        }
    }

    private void RestoreStableState(ServerDescriptor? previousServer, ServerDescriptor? lastServer)
    {
        if (previousServer is not null && _activeUpstream is not null && _activeLinkId > 0)
        {
            TransitionTo(new SessionState.InServer(previousServer, lastServer, _playerState.ActiveSlot, _activeLinkId));
        }
        else
        {
            TransitionTo(new SessionState.Lobby(lastServer));
        }
    }

    private async ValueTask ShutdownSessionAsync(string? reason, bool sendKick)
    {
        if (_state.Phase is SessionPhase.Disconnecting or SessionPhase.Disconnected)
            return;

        var wasJoined = _state.Phase != SessionPhase.Handshaking;
        TransitionTo(new SessionState.Disconnecting(reason));

        if (_activeTransferRunner is { } runner)
        {
            _activeTransferRunner = null;
            await runner.DisposeAsync().ConfigureAwait(false);
        }

        _activeTransferCompletion?.TrySetResult(new TransferResult(TransferStatus.SessionClosed, null, reason));
        _activeTransferCompletion = null;

        if (wasJoined)
        {
            _services.Logger.Text($"[{PlayerName}] disconnected. {reason}", "Session");
            await _services.Hooks.DispatchLeftAsync(new PlayerLeftEvent(this, reason), CancellationToken.None).ConfigureAwait(false);
            foreach (var other in _services.Registry.GetSessions())
            {
                if (other.SessionId != SessionId && other.CurrentState.Phase == SessionPhase.Lobby)
                    await other.SendChatAsync($"{PlayerName} has left.", TerrariaPacketFactory.Rgb(255, 255, 255), withPrefix: true, CancellationToken.None).ConfigureAwait(false);
            }
        }

        if (sendKick && !_clientConnection.IsDisposed)
        {
            await _clientConnection.WritePacketAsync(
                TerrariaPacketFactory.CreateKick(reason ?? "Disconnected."),
                fromServer: true,
                CancellationToken.None).ConfigureAwait(false);
        }

        TransitionTo(new SessionState.Disconnected(reason));
        await FinalizeResourcesAsync().ConfigureAwait(false);
    }

    private async ValueTask FinalizeResourcesAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _services.Registry.TryRemove(SessionId, out _);
        _mailbox.Writer.TryComplete();

        try { await _sessionCts.CancelAsync().ConfigureAwait(false); } catch (ObjectDisposedException) { }

        if (_activeTransferRunner is { } runner)
        {
            _activeTransferRunner = null;
            await runner.DisposeAsync().ConfigureAwait(false);
        }

        var upstream = Interlocked.Exchange(ref _activeUpstream, null);
        if (upstream is not null)
            await upstream.DisposeAsync().ConfigureAwait(false);

        await _clientConnection.DisposeAsync().ConfigureAwait(false);

        if (_handshakeTimeoutTask is { } timeoutTask)
        {
            try { await timeoutTask.ConfigureAwait(false); } catch { }
        }

        TransitionTo(new SessionState.Disconnected(_state is SessionState.Disconnecting d ? d.Reason : null));
        _sessionCts.Dispose();
    }

    private void TransitionTo(SessionState next) => Volatile.Write(ref _state, next);

    private async ValueTask<bool> TryEnqueueAsync(ControlMessage message, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        try
        {
            await _mailbox.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    private static string FormatChatTemplate(string template, string serverName, string playerName, string message)
    {
        // Single-pass token replacement prevents replacement injection when playerName contains "{message}".
        var result = template;
        result = result.Replace("{servername}", "\x01S\x01", StringComparison.OrdinalIgnoreCase)
                       .Replace("{username}", "\x01U\x01", StringComparison.OrdinalIgnoreCase)
                       .Replace("{message}", "\x01M\x01", StringComparison.OrdinalIgnoreCase);
        return result.Replace("\x01S\x01", serverName, StringComparison.Ordinal)
                     .Replace("\x01U\x01", playerName, StringComparison.Ordinal)
                     .Replace("\x01M\x01", message, StringComparison.Ordinal);
    }

    private static bool IsTrackedClientPacket(MessageID msgId) => msgId is
        MessageID.SyncPlayer or MessageID.SyncEquipment or MessageID.SyncLoadout or
        MessageID.PlayerHealth or MessageID.PlayerMana or MessageID.PlayerBuffs or
        MessageID.PlayerControls or MessageID.PlayerTeam or MessageID.PlayerPvP or
        MessageID.PlayerZone or MessageID.PlayerStealth or MessageID.UpdatePlayerLuckFactors or
        MessageID.ClientUUID or MessageID.SpawnPlayer;

    private static bool IsTrackedServerPacket(MessageID msgId) => msgId is
        MessageID.LoadPlayer or MessageID.WorldData or
        MessageID.SyncPlayer or MessageID.SyncEquipment or MessageID.SyncLoadout or
        MessageID.PlayerHealth or MessageID.PlayerMana or MessageID.PlayerBuffs or
        MessageID.PlayerControls or MessageID.PlayerTeam or MessageID.PlayerPvP or
        MessageID.PlayerZone or MessageID.PlayerStealth or MessageID.UpdatePlayerLuckFactors or
        MessageID.SpawnPlayer;

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync("Server closed.").ConfigureAwait(false);
        if (_actorLoopTask is { } loop)
        {
            try { await loop.ConfigureAwait(false); } catch { }
        }
    }
}
