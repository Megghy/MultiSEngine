using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Protocol.CustomPackets;
using MultiSEngine.Protocol.Framing;
using MultiSEngine.Protocol.Terraria;
using MultiSEngine.Runtime.Hosting;
using Terraria;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.Benchmarks;

[MemoryDiagnoser]
public class ProductionPathBenchmarks
{
    private byte[] _otherPlayerControls = [];
    private byte[] _ownPlayerControls = [];
    private PlayerStateTracker _tracker = null!;
    private WorldData _nonSscWorld;
    private CustomPacketCodec _customCodec = null!;
    private byte[] _syncIpPayload = [];

    [GlobalSetup]
    public void Setup()
    {
        _otherPlayerControls = PacketFrameCodec.SerializeToArray(new PlayerControls { PlayerSlot = 9 }, fromServer: true);
        _ownPlayerControls = PacketFrameCodec.SerializeToArray(new PlayerControls { PlayerSlot = 3 }, fromServer: true);

        _tracker = new PlayerStateTracker { ActiveSlot = 3 };
        _tracker.ApplyClientPacket(new SyncPlayer { PlayerSlot = 0, Name = "BenchPlayer" }, inTargetServer: false);
        _tracker.ApplyClientPacket(new PlayerHealth { PlayerSlot = 0, StatLife = 500, StatLifeMax = 500 }, inTargetServer: false);
        _tracker.ApplyClientPacket(new PlayerMana { PlayerSlot = 0, StatMana = 200, StatManaMax = 200 }, inTargetServer: false);
        for (short i = 0; i < 59; i++)
            _tracker.ApplyClientPacket(new SyncEquipment { PlayerSlot = 0, ItemSlot = i, ItemType = (short)(i + 1), Stack = 1 }, inTargetServer: false);

        _nonSscWorld = new WorldData
        {
            WorldName = "BenchWorld",
            MaxTileX = 4200,
            MaxTileY = 1200,
            SpawnX = 2100,
            SpawnY = 600,
            WorldUniqueID = Guid.NewGuid(),
            EventInfo1 = new BitsByte(),
            ExtraSpawnPoints = [],
        };

        _customCodec = new CustomPacketCodec();
        _syncIpPayload = BuiltInCustomPacketTypes.EncodeSyncIpPayload("BenchPlayer", "198.51.100.7");
    }

    [Benchmark]
    public InboundRouteDecision ClassifyOtherPlayerBroadcast()
        => FastPathClassifier.ClassifyServerFrame(new ReadOnlySequence<byte>(_otherPlayerControls), activePlayerSlot: 3, hasMiddlewareForMessage: false);

    [Benchmark]
    public InboundRouteDecision ClassifyOwnPlayerPacket()
        => FastPathClassifier.ClassifyServerFrame(new ReadOnlySequence<byte>(_ownPlayerControls), activePlayerSlot: 3, hasMiddlewareForMessage: false);

    [Benchmark]
    public int BuildRealTargetSwitchSyncBatch()
    {
        using var writer = new PooledBufferWriter(16 * 1024);
        TerrariaSyncEngine.BuildTargetSwitchBatch(writer, _tracker, _nonSscWorld, targetSlot: 3, spawnX: 2100, spawnY: 600, ReadOnlyMemory<byte>.Empty, restoreDataWhenJoinNonSsc: true);
        return writer.WrittenCount;
    }

    [Benchmark]
    public bool EncodeAndVerifyCustomPacket()
    {
        var frame = _customCodec.Encode(BuiltInCustomPacketTypes.SyncIp, _syncIpPayload, "benchmark-hmac-secret");
        return _customCodec.Decode(frame, "benchmark-hmac-secret", TimeSpan.FromSeconds(30), allowUnsigned: false).IsSuccess;
    }
}

/// <summary>
/// Apples-to-apples loopback TCP 512-packet batch forwarding benchmark matching v1's ForwardingScenarioBenchmarks.
/// </summary>
[MemoryDiagnoser]
public class TcpForwardingBenchmarks
{
    private const int PacketsPerBatch = 512;

    private TcpClient _clientPeer = null!;
    private TcpClient _serverPeer = null!;
    private FramedConnection _clientConn = null!;
    private FramedConnection _serverConn = null!;
    private CountingDrain _clientDrain = null!;
    private CountingDrain _serverDrain = null!;
    private byte[] _s2cBatch = [];
    private byte[] _c2sBatch = [];

    [GlobalSetup]
    public void Setup()
    {
        SetupAsync().GetAwaiter().GetResult();
    }

    private async Task SetupAsync()
    {
        var (clientPeer, proxyClient) = await CreateLoopbackPairAsync().ConfigureAwait(false);
        var (serverPeer, proxyServer) = await CreateLoopbackPairAsync().ConfigureAwait(false);
        _clientPeer = clientPeer;
        _serverPeer = serverPeer;

        _clientConn = FramedConnection.FromTcpClient(proxyClient);
        _serverConn = FramedConnection.FromTcpClient(proxyServer);

        _serverConn.SetInboundHandler((frame, ct) =>
        {
            if (FastPathClassifier.ClassifyServerFrame(frame, activePlayerSlot: 1, hasMiddlewareForMessage: false) == InboundRouteDecision.FastPassThrough)
                return ForwardAsync(_clientConn, frame, ct);
            return ForwardAsync(_clientConn, frame, ct);
        });
        _clientConn.SetInboundHandler((frame, ct) =>
        {
            if (FastPathClassifier.ClassifyClientFrame(frame, hasMiddlewareForMessage: false) == InboundRouteDecision.FastPassThrough)
                return ForwardAsync(_serverConn, frame, ct);
            return ForwardAsync(_serverConn, frame, ct);
        });

        _clientConn.Start();
        _serverConn.Start();

        _clientDrain = CountingDrain.Start(_clientPeer.GetStream());
        _serverDrain = CountingDrain.Start(_serverPeer.GetStream());

        var s2cFrame = PacketFrameCodec.SerializeToArray(new PlayerControls { PlayerSlot = 9 }, fromServer: true);
        var c2sFrame = PacketFrameCodec.SerializeToArray(new TileChange { ChangeType = 0, Position = new Terraria.DataStructures.Point16(10, 10) }, fromServer: false);
        _s2cBatch = Repeat(s2cFrame, PacketsPerBatch);
        _c2sBatch = Repeat(c2sFrame, PacketsPerBatch);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupAsync().GetAwaiter().GetResult();
    }

    private async Task CleanupAsync()
    {
        await _clientConn.DisposeAsync().ConfigureAwait(false);
        await _serverConn.DisposeAsync().ConfigureAwait(false);
        await _clientDrain.DisposeAsync().ConfigureAwait(false);
        await _serverDrain.DisposeAsync().ConfigureAwait(false);
        _clientPeer.Dispose();
        _serverPeer.Dispose();
    }

    [Benchmark]
    public async Task ForwardServerToClient512Tcp()
    {
        var target = _clientDrain.BytesRead + _s2cBatch.Length;
        await _serverPeer.GetStream().WriteAsync(_s2cBatch).ConfigureAwait(false);
        await _clientDrain.WaitForBytesAsync(target, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task ForwardClientToServer512Tcp()
    {
        var target = _serverDrain.BytesRead + _c2sBatch.Length;
        await _clientPeer.GetStream().WriteAsync(_c2sBatch).ConfigureAwait(false);
        await _serverDrain.WaitForBytesAsync(target, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    private static ValueTask ForwardAsync(FramedConnection target, ReadOnlySequence<byte> frame, CancellationToken ct)
    {
        var task = target.WriteFrameAsync(frame, ct);
        return task.IsCompletedSuccessfully ? ValueTask.CompletedTask : AwaitSlowAsync(task);

        static async ValueTask AwaitSlowAsync(ValueTask<bool> pending) => await pending.ConfigureAwait(false);
    }

    private static byte[] Repeat(byte[] frame, int count)
    {
        var result = new byte[frame.Length * count];
        for (var i = 0; i < count; i++)
            Buffer.BlockCopy(frame, 0, result, i * frame.Length, frame.Length);
        return result;
    }

    private static async Task<(TcpClient PeerA, TcpClient PeerB)> CreateLoopbackPairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync();
            var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
            var server = await acceptTask.ConfigureAwait(false);
            server.NoDelay = true;
            return (client, server);
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class CountingDrain : IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loopTask;
        private long _bytesRead;
        private long _targetBytes;
        private TaskCompletionSource? _waiter;
        private readonly Lock _lock = new();

        private CountingDrain(Stream stream)
        {
            _stream = stream;
            _loopTask = ReadLoopAsync();
        }

        public long BytesRead => Volatile.Read(ref _bytesRead);

        public static CountingDrain Start(Stream stream) => new(stream);

        public Task WaitForBytesAsync(long targetBytes, TimeSpan timeout)
        {
            TaskCompletionSource tcs;
            lock (_lock)
            {
                if (Volatile.Read(ref _bytesRead) >= targetBytes)
                    return Task.CompletedTask;

                _targetBytes = targetBytes;
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiter = tcs;
            }
            return tcs.Task.WaitAsync(timeout);
        }

        private async Task ReadLoopAsync()
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var n = await _stream.ReadAsync(buf, _cts.Token).ConfigureAwait(false);
                    if (n <= 0)
                        break;
                    var total = Interlocked.Add(ref _bytesRead, n);
                    TaskCompletionSource? toComplete = null;
                    lock (_lock)
                    {
                        if (_waiter is not null && total >= _targetBytes)
                        {
                            toComplete = _waiter;
                            _waiter = null;
                        }
                    }
                    toComplete?.TrySetResult();
                }
            }
            catch
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            try { _stream.Close(); } catch { }
            try { await _loopTask.ConfigureAwait(false); } catch { }
            _cts.Dispose();
        }
    }
}

/// <summary>
/// Measures real end-to-end forwarding throughput through FramedConnection + FastPathClassifier
/// with realistic 90% other-player broadcast / 10% own-slot traffic mix.
/// </summary>
internal static class ProxyLoadReport
{
    private sealed class Duplex(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;
        public PipeWriter Output { get; } = output;
    }

    public static async Task RunAsync()
    {
        const int batchFrames = 512;
        const int iterations = 200;

        var pipeIn = new Pipe(new PipeOptions(pauseWriterThreshold: 1024 * 1024, resumeWriterThreshold: 512 * 1024, useSynchronizationContext: false));
        var pipeOut = new Pipe(new PipeOptions(pauseWriterThreshold: 1024 * 1024, resumeWriterThreshold: 512 * 1024, useSynchronizationContext: false));

        await using var serverConn = FramedConnection.FromDuplexPipe(new Duplex(pipeIn.Reader, pipeOut.Writer), options: new FramingOptions
        {
            PauseWriterThreshold = 1024 * 1024,
            ResumeWriterThreshold = 512 * 1024,
        });

        using var batchWriter = new PooledBufferWriter(32 * 1024);
        var otherControls = PacketFrameCodec.SerializeToArray(new PlayerControls { PlayerSlot = 9 }, fromServer: true);
        var ownControls = PacketFrameCodec.SerializeToArray(new PlayerControls { PlayerSlot = 1 }, fromServer: true);

        for (var i = 0; i < batchFrames; i++)
            batchWriter.Write(i % 10 == 0 ? ownControls : otherControls);

        var batchPayload = batchWriter.WrittenMemory.ToArray();
        var tracker = new PlayerStateTracker { ActiveSlot = 1 };
        long processed = 0;
        var expectedTotal = (long)batchFrames * iterations;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        serverConn.SetInboundHandler((frame, ct) =>
        {
            if (FastPathClassifier.ClassifyServerFrame(frame, activePlayerSlot: 1, hasMiddlewareForMessage: false) == InboundRouteDecision.Inspect)
            {
                var pkt = PacketFrameCodec.Deserialize(frame, fromServer: true);
                tracker.ApplyServerPacket(pkt, expectedPlayerSlot: 1);
            }

            if (Interlocked.Increment(ref processed) == expectedTotal)
                done.TrySetResult();

            return ValueTask.CompletedTask;
        });
        serverConn.Start();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            await pipeIn.Writer.WriteAsync(batchPayload).ConfigureAwait(false);

        await done.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        sw.Stop();

        var pps = expectedTotal / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"[MultiSEngine v2 Load Report] Realistic 90/10 broadcast mix: {expectedTotal:N0} frames in {sw.Elapsed.TotalMilliseconds:F1} ms => {pps:N0} packets/sec | Managed Heap: {GC.GetTotalMemory(false) / 1024.0 / 1024.0:F2} MiB");
    }
}
