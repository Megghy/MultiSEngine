using System.Buffers;
using System.Net.Sockets;
using Microsoft.Xna.Framework;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Logging;
using MultiSEngine.Core.Transfers;
using MultiSEngine.Protocol.CustomPackets;
using MultiSEngine.Protocol.Framing;
using MultiSEngine.Protocol.Terraria;
using MultiSEngine.Runtime.Infrastructure;
using Terraria;
using Terraria.DataStructures;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.Runtime.Sessions;

internal sealed class UpstreamHandshakeOutcome : IDisposable
{
    private PooledBufferWriter? _bufferedPackets;

    public TransferStatus Status { get; init; }
    public bool IsSuccess => Status == TransferStatus.Succeeded;
    public string? FailureMessage { get; init; }
    public FramedConnection? UpstreamConnection { get; private set; }
    public byte RemotePlayerSlot { get; init; }
    public WorldData? WorldData { get; init; }
    public short SpawnX { get; init; }
    public short SpawnY { get; init; }

    public ReadOnlyMemory<byte> BufferedPackets => _bufferedPackets?.WrittenMemory ?? ReadOnlyMemory<byte>.Empty;

    public static UpstreamHandshakeOutcome Success(
        FramedConnection upstream,
        byte remoteSlot,
        WorldData worldData,
        short spawnX,
        short spawnY,
        PooledBufferWriter bufferedPackets) => new()
    {
        Status = TransferStatus.Succeeded,
        UpstreamConnection = upstream,
        RemotePlayerSlot = remoteSlot,
        WorldData = worldData,
        SpawnX = spawnX,
        SpawnY = spawnY,
        _bufferedPackets = bufferedPackets,
    };

    public static UpstreamHandshakeOutcome Failed(TransferStatus status, string? message = null) => new()
    {
        Status = status,
        FailureMessage = message,
    };

    public FramedConnection TakeUpstreamOwnership()
    {
        var conn = UpstreamConnection ?? throw new InvalidOperationException("No upstream connection to transfer ownership.");
        UpstreamConnection = null;
        return conn;
    }

    public async ValueTask DisposeUnusedAsync()
    {
        if (UpstreamConnection is { } unused)
        {
            UpstreamConnection = null;
            await unused.DisposeAsync().ConfigureAwait(false);
        }

        Dispose();
    }

    public void Dispose()
    {
        _bufferedPackets?.Dispose();
        _bufferedPackets = null;
    }
}

/// <summary>
/// Isolated target-server pre-connection runner.
/// Never subscribes to ClientConnection, buffers only post-handshake world initialization frames into a single
/// contiguous pooled buffer, supports password prompts without blocking the client read loop, and pauses the
/// upstream reader at FinishedConnectingToServer for zero-loss ownership handoff.
/// </summary>
internal sealed class TransferHandshakeRunner : IAsyncDisposable
{
    private readonly ServerDescriptor _targetServer;
    private readonly ProxyConfig _config;
    private readonly PlayerStateTracker _playerState;
    private readonly string _clientIp;
    private readonly CustomPacketCodec _customPacketCodec;
    private readonly IProxyLogger _logger;
    private readonly Func<bool, ValueTask> _onPasswordRequested;
    private readonly Func<ValueTask>? _onCrowdControlBuff;

    private readonly CancellationTokenSource _cancelCts = new();
    private readonly TaskCompletionSource<UpstreamHandshakeOutcome> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _bufferLock = new();

    private FramedConnection? _connection;
    private PooledBufferWriter? _buffer;
    private int _targetProtocolVersion;
    private byte _remoteSlot;
    private WorldData? _worldData;
    private short _spawnX;
    private short _spawnY;
    private bool _passwordPrompted;
    private int _completed;

    public TransferHandshakeRunner(
        ServerDescriptor targetServer,
        ProxyConfig config,
        PlayerStateTracker playerState,
        string clientIp,
        CustomPacketCodec customPacketCodec,
        IProxyLogger logger,
        Func<bool, ValueTask> onPasswordRequested,
        Func<ValueTask>? onCrowdControlBuff = null)
    {
        _targetServer = targetServer;
        _config = config;
        _playerState = playerState;
        _clientIp = clientIp;
        _customPacketCodec = customPacketCodec;
        _logger = logger;
        _onPasswordRequested = onPasswordRequested;
        _onCrowdControlBuff = onCrowdControlBuff;
    }

    public async Task<UpstreamHandshakeOutcome> RunAsync(CancellationToken externalToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cancelCts.Token, externalToken);
        var ct = linked.Token;

        var address = await NetworkAddressResolver.ResolveHostAsync(_targetServer.Host, ct).ConfigureAwait(false);
        if (address is null)
            return UpstreamHandshakeOutcome.Failed(TransferStatus.InvalidAddress, $"Unable to resolve host '{_targetServer.Host}'.");

        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient();
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectTimeout.CancelAfter(_config.SwitchTimeoutMs);

            await tcp.ConnectAsync(address, _targetServer.Port, connectTimeout.Token).ConfigureAwait(false);

            var connection = FramedConnection.FromTcpClient(tcp, new FramingOptions
            {
                PauseWriterThreshold = _config.Limits.SendPipePauseThresholdBytes,
                ResumeWriterThreshold = _config.Limits.SendPipeResumeThresholdBytes,
                SendFlushTimeout = TimeSpan.FromMilliseconds(_config.Limits.SendFlushTimeoutMs),
                DisableTcpDelay = _config.DisableTcpDelay,
            });
            tcp = null; // Ownership transferred to FramedConnection.
            _connection = connection;
            _buffer = new PooledBufferWriter(8192);

            connection.SetInboundHandler(OnUpstreamFrameAsync);
            connection.SetClosedHandler(ex =>
            {
                TryFail(TransferStatus.ConnectionFailed, ex?.Message ?? "Target server closed the connection during handshake.");
                return ValueTask.CompletedTask;
            });
            connection.Start();

            var targetVersion = _targetServer.VersionNum > 0
                ? _targetServer.VersionNum
                : (_playerState.ProtocolVersion > 0 ? _playerState.ProtocolVersion : _config.ServerVersion);
            _targetProtocolVersion = targetVersion;

            var sent = await connection.WritePacketAsync(new ClientHello
            {
                Version = TerrariaVersions.ToClientHelloString(targetVersion),
            }, fromServer: false, ct).ConfigureAwait(false);

            if (!sent)
                return await FailAndCleanupAsync(TransferStatus.ConnectionFailed, "Failed to send ClientHello to target server.").ConfigureAwait(false);

            return await WaitForCompletionWithAdaptiveTimeoutAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (externalToken.IsCancellationRequested || _cancelCts.IsCancellationRequested)
        {
            return await FailAndCleanupAsync(TransferStatus.SessionClosed, "Transfer cancelled.").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var status = Volatile.Read(ref _passwordPrompted) ? TransferStatus.PasswordTimedOut : TransferStatus.TimedOut;
            return await FailAndCleanupAsync(status, "Timed out connecting to target server.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Pre-connect to [{_targetServer.Name}] failed: {ex.Message}", "Transfer");
            return await FailAndCleanupAsync(TransferStatus.ConnectionFailed, ex.Message).ConfigureAwait(false);
        }
        finally
        {
            tcp?.Dispose();
        }
    }

    public async ValueTask<bool> SubmitPasswordAsync(string password, CancellationToken cancellationToken)
    {
        var conn = Volatile.Read(ref _connection);
        if (conn is null || conn.IsDisposed || Volatile.Read(ref _completed) != 0)
            return false;

        return await conn.WritePacketAsync(new SendPassword { Password = password }, fromServer: false, cancellationToken).ConfigureAwait(false);
    }

    public void Cancel()
    {
        try { _cancelCts.Cancel(); } catch (ObjectDisposedException) { }
        TryFail(TransferStatus.SessionClosed, "Transfer cancelled.");
    }

    private async Task<UpstreamHandshakeOutcome> WaitForCompletionWithAdaptiveTimeoutAsync(CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + _config.SwitchTimeoutMs;
        var extendedForPassword = false;

        while (true)
        {
            if (Volatile.Read(ref _passwordPrompted) && !extendedForPassword)
            {
                extendedForPassword = true;
                deadline = Environment.TickCount64 + _config.PasswordTimeoutMs;
            }

            var remainingMs = deadline - Environment.TickCount64;
            if (remainingMs <= 0)
            {
                var status = extendedForPassword ? TransferStatus.PasswordTimedOut : TransferStatus.TimedOut;
                return await FailAndCleanupAsync(status, "Handshake timed out.").ConfigureAwait(false);
            }

            var slice = (int)Math.Min(remainingMs, 250);
            try
            {
                var outcome = await _tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(slice), ct).ConfigureAwait(false);
                if (!outcome.IsSuccess)
                    await CleanupConnectionsAsync().ConfigureAwait(false);
                return outcome;
            }
            catch (TimeoutException)
            {
                // Re-evaluate whether password prompt extended the deadline.
            }
        }
    }

    private async ValueTask OnUpstreamFrameAsync(ReadOnlySequence<byte> frame, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _completed) != 0)
            return;

        var msgId = FastPathClassifier.PeekMessageId(frame);

        switch (msgId)
        {
            case MessageID.Kick:
                var kick = (Kick)PacketFrameCodec.Deserialize(frame, fromServer: true);
                TryFail(TransferStatus.KickedByTarget, kick.Reason.ToString());
                return;

            case MessageID.RequestPassword:
                var isRetry = Volatile.Read(ref _passwordPrompted);
                Volatile.Write(ref _passwordPrompted, true);
                await _onPasswordRequested(isRetry).ConfigureAwait(false);
                return;

            case MessageID.LoadPlayer:
                var load = (LoadPlayer)PacketFrameCodec.Deserialize(frame, fromServer: true);
                _remoteSlot = load.PlayerSlot;

                if (_config.UseCrowdControlled && _onCrowdControlBuff is not null)
                    await _onCrowdControlBuff().ConfigureAwait(false);

                var conn = _connection!;
                await conn.WritePacketAsync(_playerState.CreateRemoteSyncPlayer(_remoteSlot), fromServer: false, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(_playerState.Uuid))
                    await conn.WritePacketAsync(new ClientUUID { UUID = _playerState.Uuid }, fromServer: false, cancellationToken).ConfigureAwait(false);

                if (_targetProtocolVersion >= TerrariaVersions.LatestProtocolVersion && _playerState.PlatformInfo is { } platformInfo)
                {
                    platformInfo.PlayerSlot = _remoteSlot;
                    await conn.WritePacketAsync(platformInfo, fromServer: false, cancellationToken).ConfigureAwait(false);
                }

                // Send signed v2 SyncIP custom packet if a secret is configured or dev unsigned mode is enabled.
                var secret = _targetServer.SharedSecret ?? _config.CustomPackets.GlobalSharedSecret;
                if (!string.IsNullOrEmpty(secret) || _config.CustomPackets.AllowUnsignedInDevelopment)
                {
                    var payload = BuiltInCustomPacketTypes.EncodeSyncIpPayload(_playerState.Name, _clientIp);
                    var customFrame = _customPacketCodec.Encode(BuiltInCustomPacketTypes.SyncIp, payload, secret);
                    await conn.WriteFrameAsync(customFrame, cancellationToken).ConfigureAwait(false);
                }

                await conn.WritePacketAsync(new RequestWorldInfo(), fromServer: false, cancellationToken).ConfigureAwait(false);
                return;

            case MessageID.WorldData:
                var worldData = (WorldData)PacketFrameCodec.Deserialize(frame, fromServer: true);
                _worldData = worldData;

                var spawnX = _targetServer.SpawnX >= 0 && _targetServer.SpawnX <= worldData.MaxTileX
                    ? _targetServer.SpawnX
                    : worldData.SpawnX;
                var spawnY = _targetServer.SpawnY >= 0 && _targetServer.SpawnY <= worldData.MaxTileY
                    ? _targetServer.SpawnY
                    : worldData.SpawnY;

                _spawnX = spawnX;
                _spawnY = spawnY;

                var targetConn = _connection!;
                await targetConn.WritePacketAsync(new RequestTileData
                {
                    Position = new Point(spawnX, spawnY),
                }, fromServer: false, cancellationToken).ConfigureAwait(false);

                await targetConn.WritePacketAsync(new SpawnPlayer
                {
                    PlayerSlot = _remoteSlot,
                    Position = new Point16(spawnX, spawnY),
                    Timer = _playerState.Timer,
                    DeathsPVE = _playerState.DeathsPve,
                    DeathsPVP = _playerState.DeathsPvp,
                    Team = _playerState.Team,
                    Context = PlayerSpawnContext.SpawningIntoWorld,
                }, fromServer: false, cancellationToken).ConfigureAwait(false);
                return;

            case MessageID.StatusText:
            case MessageID.StartPlaying:
                // Handshake-only control packets consumed by proxy; do not replay to an already-in-game client.
                return;

            case MessageID.FinishedConnectingToServer:
                CompleteHandshakeSuccess();
                return;

            default:
                // Buffer world/chunk/entity initialization packets tightly in one contiguous pooled buffer.
                BufferFrame(frame);
                return;
        }
    }

    private void BufferFrame(in ReadOnlySequence<byte> frame)
    {
        lock (_bufferLock)
        {
            if (_buffer is null || Volatile.Read(ref _completed) != 0)
                return;

            if (_buffer.WrittenCount + frame.Length > _config.Limits.MaxPreConnectBufferedBytes)
            {
                TryFail(TransferStatus.ConnectionFailed, "Target server exceeded maximum pre-connect buffer limit.");
                return;
            }

            _buffer.Write(frame);
        }
    }

    private void CompleteHandshakeSuccess()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        if (_connection is null || _worldData is null)
        {
            _tcs.TrySetResult(UpstreamHandshakeOutcome.Failed(TransferStatus.ConnectionFailed, "Target server finished handshake without sending WorldData."));
            return;
        }

        // Pause reading BEFORE handing off so zero subsequent packets from the target server are lost or out-of-order.
        _connection.PauseReading();

        PooledBufferWriter buffered;
        lock (_bufferLock)
        {
            buffered = _buffer ?? new PooledBufferWriter(64);
            _buffer = null;
        }

        var conn = _connection;
        _connection = null;

        _tcs.TrySetResult(UpstreamHandshakeOutcome.Success(conn, _remoteSlot, _worldData.Value, _spawnX, _spawnY, buffered));
    }

    private void TryFail(TransferStatus status, string? message)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        _tcs.TrySetResult(UpstreamHandshakeOutcome.Failed(status, message));
    }

    private async Task<UpstreamHandshakeOutcome> FailAndCleanupAsync(TransferStatus status, string? message)
    {
        TryFail(status, message);
        await CleanupConnectionsAsync().ConfigureAwait(false);
        return await _tcs.Task.ConfigureAwait(false);
    }

    private async ValueTask CleanupConnectionsAsync()
    {
        var conn = Interlocked.Exchange(ref _connection, null);
        if (conn is not null)
            await conn.DisposeAsync().ConfigureAwait(false);

        lock (_bufferLock)
        {
            _buffer?.Dispose();
            _buffer = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Cancel();
        await CleanupConnectionsAsync().ConfigureAwait(false);
        _cancelCts.Dispose();
    }
}
