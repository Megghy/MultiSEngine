using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using MultiSEngine.Protocol.Framing;
using MultiSEngine.Protocol.Terraria;
using Terraria;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.IntegrationTests.Support;

internal sealed record FakeServerOptions
{
    public string WorldName { get; init; } = "FakeWorld";
    public byte AssignedPlayerSlot { get; init; } = 5;
    public bool IsServerSideCharacter { get; init; }
    public string? RequiredPassword { get; init; }
    public short SpawnX { get; init; } = 2100;
    public short SpawnY { get; init; } = 600;
    public INetPacket[] PostConnectPackets { get; init; } = [];
}

internal sealed class FakeTerrariaServer : IAsyncDisposable
{
    private readonly FakeServerOptions _options;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentBag<FramedConnection> _connections = [];
    private readonly Channel<(MessageID MessageId, byte[] RawFrame)> _receivedFrames = Channel.CreateUnbounded<(MessageID, byte[])>();
    private readonly Task _acceptTask;

    private FramedConnection? _latestConnection;

    public FakeTerrariaServer(FakeServerOptions? options = null)
    {
        _options = options ?? new FakeServerOptions();
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = AcceptLoopAsync();
    }

    public int Port { get; }
    public int ConnectionCount => _connections.Count;

    public async Task<byte[]> WaitForFrameAsync(MessageID expectedId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (await _receivedFrames.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
        {
            while (_receivedFrames.Reader.TryRead(out var item))
            {
                if (item.MessageId == expectedId)
                    return item.RawFrame;
            }
        }
        throw new TimeoutException($"Timed out waiting for {expectedId} on FakeTerrariaServer '{_options.WorldName}'.");
    }

    public bool TryHasReceived(MessageID id)
    {
        // Peek via snapshot is not needed when caller checks recorded list.
        return _recordedMessageIds.ContainsKey(id);
    }

    private readonly ConcurrentDictionary<MessageID, int> _recordedMessageIds = new();

    public async Task SendToLatestAsync(INetPacket packet, CancellationToken ct = default)
    {
        var conn = Volatile.Read(ref _latestConnection) ?? throw new InvalidOperationException("No connected client.");
        await conn.WritePacketAsync(packet, fromServer: true, ct).ConfigureAwait(false);
    }

    public async Task SendRawToLatestAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        var conn = Volatile.Read(ref _latestConnection) ?? throw new InvalidOperationException("No connected client.");
        await conn.WriteFrameAsync(frame, ct).ConfigureAwait(false);
    }

    public async Task CloseLatestConnectionAsync()
    {
        var conn = Interlocked.Exchange(ref _latestConnection, null);
        if (conn is not null)
            await conn.DisposeAsync().ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient? tcp = null;
            try
            {
                tcp = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                var conn = FramedConnection.FromTcpClient(tcp);
                tcp = null;
                _connections.Add(conn);
                Volatile.Write(ref _latestConnection, conn);

                var passwordAccepted = string.IsNullOrEmpty(_options.RequiredPassword);
                conn.SetInboundHandler((frame, ct) => HandleFrameAsync(conn, frame, () => passwordAccepted, v => passwordAccepted = v, ct));
                conn.Start();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                tcp?.Dispose();
            }
        }
    }

    private async ValueTask HandleFrameAsync(
        FramedConnection conn,
        ReadOnlySequence<byte> frame,
        Func<bool> isPasswordAccepted,
        Action<bool> setPasswordAccepted,
        CancellationToken ct)
    {
        var copy = PacketFrameCodec.CopyToArray(frame);
        var msgId = (MessageID)copy[2];
        _recordedMessageIds.AddOrUpdate(msgId, 1, (_, c) => c + 1);
        _receivedFrames.Writer.TryWrite((msgId, copy));

        switch (msgId)
        {
            case MessageID.ClientHello:
                if (!isPasswordAccepted())
                {
                    await conn.WritePacketAsync(new RequestPassword(), fromServer: true, ct).ConfigureAwait(false);
                    return;
                }
                await conn.WritePacketAsync(new LoadPlayer { PlayerSlot = _options.AssignedPlayerSlot }, fromServer: true, ct).ConfigureAwait(false);
                return;

            case MessageID.SendPassword:
                var pwd = (SendPassword)PacketFrameCodec.Deserialize(copy, fromServer: false);
                if (pwd.Password == _options.RequiredPassword)
                {
                    setPasswordAccepted(true);
                    await conn.WritePacketAsync(new LoadPlayer { PlayerSlot = _options.AssignedPlayerSlot }, fromServer: true, ct).ConfigureAwait(false);
                }
                else
                {
                    await conn.WritePacketAsync(new RequestPassword(), fromServer: true, ct).ConfigureAwait(false);
                }
                return;

            case MessageID.RequestWorldInfo:
                var bits = new BitsByte();
                bits[6] = _options.IsServerSideCharacter;
                await conn.WritePacketAsync(new WorldData
                {
                    MaxTileX = 4200,
                    MaxTileY = 1200,
                    SpawnX = _options.SpawnX,
                    SpawnY = _options.SpawnY,
                    WorldName = _options.WorldName,
                    WorldUniqueID = Guid.NewGuid(),
                    EventInfo1 = bits,
                    ExtraSpawnPoints = [],
                }, fromServer: true, ct).ConfigureAwait(false);
                return;

            case MessageID.RequestTileData:
                await conn.WritePacketAsync(new StatusText
                {
                    Max = 1,
                    Text = TerrariaPacketFactory.LiteralText("Tile data"),
                    Flag = 0,
                }, fromServer: true, ct).ConfigureAwait(false);
                await conn.WritePacketAsync(new StartPlaying(), fromServer: true, ct).ConfigureAwait(false);
                return;

            case MessageID.SpawnPlayer:
                await conn.WritePacketAsync(new FinishedConnectingToServer(), fromServer: true, ct).ConfigureAwait(false);
                // Immediately send post-connect packets back-to-back to verify zero packet loss across transfer gate!
                foreach (var pkt in _options.PostConnectPackets)
                    await conn.WritePacketAsync(pkt, fromServer: true, ct).ConfigureAwait(false);
                return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try { await _acceptTask.ConfigureAwait(false); } catch { }
        foreach (var conn in _connections)
            await conn.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
