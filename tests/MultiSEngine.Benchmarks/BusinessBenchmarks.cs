using System.Buffers;
using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using MultiSEngine.Application.Transfers;
using MultiSEngine.Models;
using MultiSEngine.Networking;
using MultiSEngine.Protocol.Adapters;
using MultiSEngine.Runtime;
using Terraria;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class ForwardingScenarioBenchmarks
{
    private ForwardingHarness _harness = null!;

    [Params(512)]
    public int PacketsPerBatch { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _harness = ForwardingHarness.CreateAsync(PacketsPerBatch).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public Task ForwardServerToClientBatch()
        => _harness.ForwardServerToClientAsync();

    [Benchmark]
    public Task ForwardClientToServerBatch()
        => _harness.ForwardClientToServerAsync();
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class PlayerSyncScenarioBenchmarks
{
    private PlayerSyncClusterHarness _cluster = null!;

    [Params(16, 64)]
    public int Players { get; set; }

    [Params(1, 4)]
    public int Servers { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _cluster = PlayerSyncClusterHarness.CreateAsync(Players, Servers).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cluster.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public Task SyncAllPlayersBatch()
        => _cluster.SyncAllAsync();
}

internal sealed class ForwardingHarness : IAsyncDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpClient _clientPeer;
    private readonly TcpClient _serverPeer;
    private readonly PassthroughAdapter _adapter;
    private readonly CountingDrain _clientDrain;
    private readonly CountingDrain _serverDrain;
    private readonly byte[] _serverToClientBatch;
    private readonly byte[] _clientToServerBatch;

    private ForwardingHarness(
        TcpClient clientPeer,
        TcpClient serverPeer,
        PassthroughAdapter adapter,
        CountingDrain clientDrain,
        CountingDrain serverDrain,
        byte[] serverToClientBatch,
        byte[] clientToServerBatch)
    {
        _clientPeer = clientPeer;
        _serverPeer = serverPeer;
        _adapter = adapter;
        _clientDrain = clientDrain;
        _serverDrain = serverDrain;
        _serverToClientBatch = serverToClientBatch;
        _clientToServerBatch = clientToServerBatch;
    }

    public static async Task<ForwardingHarness> CreateAsync(int packetsPerBatch)
    {
        var (clientPeer, adapterClient) = await LoopbackTcpPair.CreateAsync().ConfigureAwait(false);
        var (serverPeer, adapterServer) = await LoopbackTcpPair.CreateAsync().ConfigureAwait(false);

        var client = new ClientData();
        var adapter = new PassthroughAdapter(client, new TcpContainer(adapterClient), new TcpContainer(adapterServer));
        client.Adapter = adapter;
        adapter.Start();

        using var serverToClientPacket = PacketCodec.SerializeRented(new LoadPlayer { PlayerSlot = 7 });
        using var clientToServerPacket = PacketCodec.SerializeRented(new RequestWorldInfo());

        var clientDrain = await CountingDrain.StartAsync(clientPeer.GetStream()).ConfigureAwait(false);
        var serverDrain = await CountingDrain.StartAsync(serverPeer.GetStream()).ConfigureAwait(false);

        return new ForwardingHarness(
            clientPeer,
            serverPeer,
            adapter,
            clientDrain,
            serverDrain,
            BuildBatch(serverToClientPacket.Memory.Span, packetsPerBatch),
            BuildBatch(clientToServerPacket.Memory.Span, packetsPerBatch));
    }

    public async Task ForwardServerToClientAsync()
    {
        var targetBytes = _clientDrain.BytesRead + _serverToClientBatch.Length;
        await _serverPeer.GetStream().WriteAsync(_serverToClientBatch).ConfigureAwait(false);
        await _clientDrain.WaitForBytesAsync(targetBytes, WaitTimeout).ConfigureAwait(false);
    }

    public async Task ForwardClientToServerAsync()
    {
        var targetBytes = _serverDrain.BytesRead + _clientToServerBatch.Length;
        await _clientPeer.GetStream().WriteAsync(_clientToServerBatch).ConfigureAwait(false);
        await _serverDrain.WaitForBytesAsync(targetBytes, WaitTimeout).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _adapter.DisposeAsync(true).ConfigureAwait(false);
        await _clientDrain.DisposeAsync().ConfigureAwait(false);
        await _serverDrain.DisposeAsync().ConfigureAwait(false);
        _clientPeer.Dispose();
        _serverPeer.Dispose();
    }

    private static byte[] BuildBatch(ReadOnlySpan<byte> packet, int count)
    {
        var batch = new byte[packet.Length * count];
        for (var i = 0; i < count; i++)
            packet.CopyTo(batch.AsSpan(i * packet.Length, packet.Length));
        return batch;
    }
}

internal sealed class PlayerSyncClusterHarness : IAsyncDisposable
{
    private readonly SyncClientHarness[] _clients;

    private PlayerSyncClusterHarness(SyncClientHarness[] clients)
    {
        _clients = clients;
        ExpectedPacketsPerRound = clients.Sum(static client => client.ExpectedPacketsPerSync);
    }

    public long ExpectedPacketsPerRound { get; }

    public static async Task<PlayerSyncClusterHarness> CreateAsync(int players, int servers)
    {
        Config.Instance.RestoreDataWhenJoinNonSSC = true;
        Config.Instance.DisableTcpDelayWhenPipeline = true;

        RuntimeState.ClientRegistry.SnapshotClients().ToList().ForEach(static client => client.Dispose());

        var serverPool = Enumerable.Range(0, servers)
            .Select(index => new ServerInfo
            {
                Name = $"server-{index + 1}",
                ShortName = $"s{index + 1}",
                IP = IPAddress.Loopback.ToString(),
                Port = 7000 + index,
                SpawnX = (short)(200 + index * 10),
                SpawnY = (short)(100 + index * 10),
                VersionNum = Config.Instance.ServerVersion,
            })
            .ToArray();

        Config.Instance.Servers = [.. serverPool];
        Config.Instance.DefaultServer = serverPool[0].Name;

        var clients = new SyncClientHarness[players];
        for (var i = 0; i < players; i++)
        {
            clients[i] = await SyncClientHarness.CreateAsync(i, serverPool[i % serverPool.Length]).ConfigureAwait(false);
        }

        return new PlayerSyncClusterHarness(clients);
    }

    public async Task SyncAllAsync()
    {
        var targets = new long[_clients.Length];
        for (var i = 0; i < _clients.Length; i++)
            targets[i] = _clients[i].Drain.BytesRead + _clients[i].ExpectedBytesPerSync;

        await Task.WhenAll(_clients.Select(static client => BenchmarkPlayerSyncService.SyncClientAsync(client.Client))).ConfigureAwait(false);
        await Task.WhenAll(_clients.Select((client, index) => client.Drain.WaitForBytesAsync(targets[index], TimeSpan.FromSeconds(5)))).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class SyncClientHarness : IAsyncDisposable
{
    private SyncClientHarness(ClientData client, BenchmarkClientAdapter adapter, TcpClient peer, CountingDrain drain, long expectedBytesPerSync, int expectedPacketsPerSync)
    {
        Client = client;
        Adapter = adapter;
        Peer = peer;
        Drain = drain;
        ExpectedBytesPerSync = expectedBytesPerSync;
        ExpectedPacketsPerSync = expectedPacketsPerSync;
    }

    public ClientData Client { get; }

    public BenchmarkClientAdapter Adapter { get; }

    public TcpClient Peer { get; }

    public CountingDrain Drain { get; }

    public long ExpectedBytesPerSync { get; }

    public int ExpectedPacketsPerSync { get; }

    public static async Task<SyncClientHarness> CreateAsync(int index, ServerInfo server)
    {
        var (peer, adapterSide) = await LoopbackTcpPair.CreateAsync().ConfigureAwait(false);
        var drain = await CountingDrain.StartAsync(peer.GetStream()).ConfigureAwait(false);

        var client = new ClientData();
        client.Player = new PlayerInfo();
        client.CurrentServer = server;
        client.SetIndex((byte)((index % 200) + 1));
        PopulateCharacterData(client, server, index);

        var adapter = new BenchmarkClientAdapter(client, new TcpContainer(adapterSide));
        client.Adapter = adapter;

        // 一个 player-sync 会拼出多条 Terraria 包，压测时同时记录字节数和底层发包数。
        var metrics = BenchmarkPlayerSyncService.MeasureSyncMetrics(client);
        return new SyncClientHarness(client, adapter, peer, drain, metrics.Bytes, metrics.Packets);
    }

    public async ValueTask DisposeAsync()
    {
        await Adapter.DisposeAsync(true).ConfigureAwait(false);
        await Drain.DisposeAsync().ConfigureAwait(false);
        Peer.Dispose();
    }

    private static void PopulateCharacterData(ClientData client, ServerInfo server, int seed)
    {
        var origin = client.Player.OriginCharacter;
        var serverCharacter = client.Player.ServerCharacter;
        var playerIndex = client.Index;

        origin.WorldData = CreateWorldData(server, seed);
        serverCharacter.WorldData = CreateWorldData(server, seed);
        origin.Info = CreateSyncPlayer(playerIndex, seed);
        serverCharacter.Info = CreateSyncPlayer(playerIndex, seed);
        origin.Health = 400;
        origin.HealthMax = 500;
        origin.Mana = 180;
        origin.ManaMax = 200;
        origin.Loadout = new SyncLoadout
        {
            PlayerSlot = playerIndex,
            LoadOutSlot = (byte)(seed % 3),
            AccessoryVisibility = (ushort)(100 + seed),
        };

        for (var i = 0; i < CharacterData.InventorySlotCount; i++)
        {
            origin.Inventory[i] = CreateEquipment(playerIndex, CharacterData.InventorySlotStart + i, (short)((seed + i) % 5000 + 1));
        }
    }

    private static WorldData CreateWorldData(ServerInfo server, int seed)
    {
        return new WorldData
        {
            MaxTileX = 8400,
            MaxTileY = 2400,
            SpawnX = server.SpawnX > 0 ? server.SpawnX : (short)(200 + seed),
            SpawnY = server.SpawnY > 0 ? server.SpawnY : (short)(100 + seed),
            WorldName = server.Name,
            WorldUniqueID = new Guid(seed + 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11),
            EventInfo1 = 0,
            ExtraSpawnPointCount = 0,
            ExtraSpawnPoints = [],
        };
    }

    private static SyncPlayer CreateSyncPlayer(byte playerIndex, int seed)
    {
        return new SyncPlayer
        {
            PlayerSlot = playerIndex,
            SkinVariant = 1,
            Hair = (byte)(seed % 50),
            Name = $"bench-{seed}",
            HairDye = 2,
            HairColor = Utils.Rgb(10, 20, 30),
            SkinColor = Utils.Rgb(40, 50, 60),
            EyeColor = Utils.Rgb(70, 80, 90),
            ShirtColor = Utils.Rgb(100, 110, 120),
            UnderShirtColor = Utils.Rgb(130, 140, 150),
            PantsColor = Utils.Rgb(160, 170, 180),
            ShoeColor = Utils.Rgb(190, 200, 210),
        };
    }

    private static SyncEquipment CreateEquipment(byte playerIndex, int slot, short itemType)
    {
        return new SyncEquipment
        {
            PlayerSlot = playerIndex,
            ItemSlot = (short)slot,
            Stack = 1,
            Prefix = 0,
            ItemType = itemType,
        };
    }
}

internal static class BenchmarkPlayerSyncService
{
    public static async Task SyncClientAsync(ClientData client, CancellationToken cancellationToken = default)
    {
        using var batchWriter = new Utils.PooledBufferWriter();

        void EnqueuePacket(INetPacket packet)
            => batchWriter.WritePacket(packet, fromServer: true);

        var data = client.Player.ServerCharacter?.WorldData ?? client.Player.OriginCharacter.WorldData
            ?? throw new Exception("[BenchmarkPlayerSyncService] World data not available for sync");

        if (!client.Player.SSC && Config.Instance.RestoreDataWhenJoinNonSSC)
        {
            var bb = data.EventInfo1;
            bb[6] = true;
            data.EventInfo1 = bb;
            EnqueuePacket(data);
            EnqueuePacket(client.Player.OriginCharacter.Info ?? throw new Exception("[BenchmarkPlayerSyncService] Origin player info not available"));
            EnqueuePacket(new PlayerHealth
            {
                PlayerSlot = client.Player.Index,
                StatLife = client.Player.OriginCharacter.Health,
                StatLifeMax = client.Player.OriginCharacter.HealthMax,
            });
            EnqueuePacket(new PlayerMana
            {
                PlayerSlot = client.Player.Index,
                StatMana = client.Player.OriginCharacter.Mana,
                StatManaMax = client.Player.OriginCharacter.ManaMax,
            });
            EnqueuePacket(client.Player.OriginCharacter.CreateSyncLoadoutPacket(client.Player.Index));
            foreach (var packet in client.Player.OriginCharacter.EnumerateSyncEquipment(client.Player.Index))
                EnqueuePacket(packet);
            bb[6] = false;
            data.EventInfo1 = bb;
            EnqueuePacket(data);
        }
        else
        {
            EnqueuePacket(data);
        }

        await DispatchBatchToClientAsync(client, batchWriter.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    public static (int Bytes, int Packets) MeasureSyncMetrics(ClientData client)
    {
        using var batchWriter = new Utils.PooledBufferWriter();
        var packets = 0;

        void AddPacket(INetPacket packet)
        {
            batchWriter.WritePacket(packet, fromServer: true);
            packets++;
        }

        var data = client.Player.ServerCharacter?.WorldData ?? client.Player.OriginCharacter.WorldData
            ?? throw new Exception("[BenchmarkPlayerSyncService] World data not available for measurement");

        if (!client.Player.SSC && Config.Instance.RestoreDataWhenJoinNonSSC)
        {
            var bb = data.EventInfo1;
            bb[6] = true;
            data.EventInfo1 = bb;
            AddPacket(data);
            AddPacket(client.Player.OriginCharacter.Info ?? throw new Exception("[BenchmarkPlayerSyncService] Origin player info not available"));
            AddPacket(new PlayerHealth
            {
                PlayerSlot = client.Player.Index,
                StatLife = client.Player.OriginCharacter.Health,
                StatLifeMax = client.Player.OriginCharacter.HealthMax,
            });
            AddPacket(new PlayerMana
            {
                PlayerSlot = client.Player.Index,
                StatMana = client.Player.OriginCharacter.Mana,
                StatManaMax = client.Player.OriginCharacter.ManaMax,
            });
            AddPacket(client.Player.OriginCharacter.CreateSyncLoadoutPacket(client.Player.Index));
            foreach (var packet in client.Player.OriginCharacter.EnumerateSyncEquipment(client.Player.Index))
                AddPacket(packet);
            bb[6] = false;
            data.EventInfo1 = bb;
            AddPacket(data);
        }
        else
        {
            AddPacket(data);
        }

        return (batchWriter.WrittenCount, packets);
    }

    private static async Task DispatchBatchToClientAsync(ClientData client, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var adapter = client.Adapter ?? throw new InvalidOperationException("[BenchmarkPlayerSyncService] Adapter is required.");
        await adapter.SendToClientDirectAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class BenchmarkClientAdapter : BaseAdapter
{
    public BenchmarkClientAdapter(ClientData client, TcpContainer clientConnection)
        : base(client, clientConnection)
    {
    }

    protected override void RegisterHandlers()
    {
    }
}

internal sealed class PassthroughAdapter : BaseAdapter
{
    public PassthroughAdapter(ClientData client, TcpContainer clientConnection, TcpContainer serverConnection)
        : base(client, clientConnection, serverConnection)
    {
    }

    protected override void RegisterHandlers()
    {
    }
}

internal sealed class CountingDrain : IAsyncDisposable
{
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;
    private readonly object _advanceLock = new();
    private TaskCompletionSource _bytesAdvanced = CreateAdvanceSignal();

    private CountingDrain(NetworkStream stream)
    {
        _stream = stream;
        _pumpTask = PumpAsync();
    }

    public long BytesRead => Volatile.Read(ref _bytesRead);

    private long _bytesRead;

    public static Task<CountingDrain> StartAsync(NetworkStream stream)
        => Task.FromResult(new CountingDrain(stream));

    public async Task WaitForBytesAsync(long targetBytes, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (BytesRead < targetBytes)
        {
            Task waitTask;
            lock (_advanceLock)
            {
                if (BytesRead >= targetBytes)
                    return;

                waitTask = _bytesAdvanced.Task;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException($"Timed out waiting for {targetBytes} bytes, got {BytesRead}.");

            try
            {
                await waitTask.WaitAsync(remaining).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Timed out waiting for {targetBytes} bytes, got {BytesRead}.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _pumpTask.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task PumpAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(), _cts.Token).ConfigureAwait(false);
                if (read == 0)
                    break;

                Interlocked.Add(ref _bytesRead, read);
                SignalAdvance();
            }
        }
        catch
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private void SignalAdvance()
    {
        TaskCompletionSource previous;
        lock (_advanceLock)
        {
            previous = _bytesAdvanced;
            _bytesAdvanced = CreateAdvanceSignal();
        }

        previous.TrySetResult();
    }

    private static TaskCompletionSource CreateAdvanceSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal static class LoopbackTcpPair
{
    public static async Task<(TcpClient peer, TcpClient adapterSide)> CreateAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connectTask = ConnectAsync(port);
        var adapterSide = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
        var peer = await connectTask.ConfigureAwait(false);

        peer.NoDelay = true;
        adapterSide.NoDelay = true;
        return (peer, adapterSide);
    }

    private static async Task<TcpClient> ConnectAsync(int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
        return client;
    }
}
