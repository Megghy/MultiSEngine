using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using MultiSEngine.Protocol.Framing;
using MultiSEngine.Protocol.Terraria;
using Terraria;
using Terraria.DataStructures;
using TrProtocol;
using TrProtocol.NetPackets;
using TrProtocol.NetPackets.Modules;

namespace MultiSEngine.IntegrationTests.Support;

internal sealed class FakeTerrariaClient : IAsyncDisposable
{
    private readonly FramedConnection _connection;
    private readonly Channel<(MessageID MessageId, byte[] RawFrame)> _inbound = Channel.CreateUnbounded<(MessageID, byte[])>();
    private readonly TaskCompletionSource _closedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private FakeTerrariaClient(FramedConnection connection)
    {
        _connection = connection;
        _connection.SetInboundHandler((frame, _) =>
        {
            var copy = PacketFrameCodec.CopyToArray(frame);
            _inbound.Writer.TryWrite(((MessageID)copy[2], copy));
            return ValueTask.CompletedTask;
        });
        _connection.SetClosedHandler(_ =>
        {
            _closedTcs.TrySetResult();
            _inbound.Writer.TryComplete();
            return ValueTask.CompletedTask;
        });
        _connection.Start();
    }

    public Task Closed => _closedTcs.Task;

    public static async Task<FakeTerrariaClient> ConnectAsync(int port, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, ct).ConfigureAwait(false);
        return new FakeTerrariaClient(FramedConnection.FromTcpClient(tcp));
    }

    public ValueTask<bool> SendPacketAsync(INetPacket packet, CancellationToken ct = default)
        => _connection.WritePacketAsync(packet, fromServer: false, ct);

    public ValueTask<bool> SendRawAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        => _connection.WriteFrameAsync(frame, ct);

    public ValueTask<bool> SendChatAsync(string text, CancellationToken ct = default)
        => SendPacketAsync(TerrariaPacketFactory.CreateClientChat(text), ct);

    public async Task CompleteLobbyHandshakeAsync(string playerName, string uuid = "uuid-001", int version = 329)
    {
        await SendPacketAsync(new ClientHello { Version = TerrariaVersions.ToClientHelloString(version) });
        await WaitForPacketAsync<LoadPlayer>(TimeSpan.FromSeconds(5));

        await SendPacketAsync(new SyncPlayer { PlayerSlot = 0, Name = playerName });
        await SendPacketAsync(new ClientUUID { UUID = uuid });
        await SendPacketAsync(new PlayerHealth { PlayerSlot = 0, StatLife = 400, StatLifeMax = 400 });
        await SendPacketAsync(new PlayerMana { PlayerSlot = 0, StatMana = 200, StatManaMax = 200 });
        await SendPacketAsync(new RequestWorldInfo());
        await WaitForPacketAsync<WorldData>(TimeSpan.FromSeconds(5));

        await SendPacketAsync(new RequestTileData { Position = new Microsoft.Xna.Framework.Point(LobbyProtocol.SpawnX, LobbyProtocol.SpawnY) });
        await WaitForPacketAsync<StartPlaying>(TimeSpan.FromSeconds(5));

        await SendPacketAsync(new SpawnPlayer
        {
            PlayerSlot = 0,
            Position = new Point16(LobbyProtocol.SpawnX, LobbyProtocol.SpawnY),
            Context = PlayerSpawnContext.SpawningIntoWorld,
        });
        await WaitForPacketAsync<FinishedConnectingToServer>(TimeSpan.FromSeconds(5));
    }

    public async Task<TPacket> WaitForPacketAsync<TPacket>(TimeSpan timeout, Func<TPacket, bool>? predicate = null)
        where TPacket : struct, INetPacket
    {
        using var cts = new CancellationTokenSource(timeout);
        while (await _inbound.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
        {
            while (_inbound.Reader.TryRead(out var item))
            {
                var packet = PacketFrameCodec.Deserialize(item.RawFrame, fromServer: true);
                if (packet is TPacket typed && (predicate is null || predicate(typed)))
                    return typed;
            }
        }
        throw new TimeoutException($"Timed out waiting for packet {typeof(TPacket).Name}.");
    }

    public async Task<string> WaitForChatContainingAsync(string substring, TimeSpan timeout)
    {
        var msg = await WaitForPacketAsync<NetTextModule>(
            timeout,
            m => m.TextS2C?.Text?.ToString()?.Contains(substring, StringComparison.OrdinalIgnoreCase) == true);
        return msg.TextS2C!.Text!.ToString();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync().ConfigureAwait(false);
}
