using System.Buffers;
using System.IO.Pipelines;
using MultiSEngine.Protocol.Framing;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.Tests;

internal sealed class InMemoryDuplexPair
{
    private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;
        public PipeWriter Output { get; } = output;
    }

    public Pipe ClientToServer { get; }
    public Pipe ServerToClient { get; }
    public IDuplexPipe ClientEnd { get; }
    public IDuplexPipe ServerEnd { get; }

    public InMemoryDuplexPair(PipeOptions? options = null)
    {
        options ??= new PipeOptions(useSynchronizationContext: false);
        ClientToServer = new Pipe(options);
        ServerToClient = new Pipe(options);
        ClientEnd = new DuplexPipe(ServerToClient.Reader, ClientToServer.Writer);
        ServerEnd = new DuplexPipe(ClientToServer.Reader, ServerToClient.Writer);
    }
}

public sealed class FramingAndBackpressureTests
{
    [Fact]
    public async Task FragmentedByteByByteStream_ReassemblesAllTerrariaFramesCorrectly()
    {
        var pair = new InMemoryDuplexPair();
        await using var conn = FramedConnection.FromDuplexPipe(pair.ServerEnd);

        var received = new List<byte[]>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        conn.SetInboundHandler((frame, _) =>
        {
            received.Add(PacketFrameCodec.CopyToArray(frame));
            if (received.Count == 2)
                done.TrySetResult();
            return ValueTask.CompletedTask;
        });
        conn.Start();

        var p1 = PacketFrameCodec.SerializeToArray(new ClientHello { Version = "Terraria319" }, fromServer: false);
        var p2 = PacketFrameCodec.SerializeToArray(new RequestWorldInfo(), fromServer: false);
        var combined = new byte[p1.Length + p2.Length];
        p1.CopyTo(combined, 0);
        p2.CopyTo(combined, p1.Length);

        // Feed 1 byte at a time to exercise PipeReader multi-segment reassembly.
        for (var i = 0; i < combined.Length; i++)
        {
            var span = pair.ClientEnd.Output.GetSpan(1);
            span[0] = combined[i];
            pair.ClientEnd.Output.Advance(1);
            await pair.ClientEnd.Output.FlushAsync();
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, received.Count);
        Assert.Equal(p1, received[0]);
        Assert.Equal(p2, received[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MalformedFrameLengthBelowThree_FaultsConnectionWithoutIndexOutOfRange(ushort badLength)
    {
        var pair = new InMemoryDuplexPair();
        await using var conn = FramedConnection.FromDuplexPipe(pair.ServerEnd);

        var closedTcs = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.SetInboundHandler((_, _) => ValueTask.CompletedTask);
        conn.SetClosedHandler(ex =>
        {
            closedTcs.TrySetResult(ex);
            return ValueTask.CompletedTask;
        });
        conn.Start();

        var span = pair.ClientEnd.Output.GetSpan(2);
        BitConverter.TryWriteBytes(span, badLength);
        pair.ClientEnd.Output.Advance(2);
        await pair.ClientEnd.Output.FlushAsync();

        var fault = await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<InvalidDataException>(fault);
    }

    [Fact]
    public async Task SelfDisposeInsideInboundHandler_NeverDeadlocks()
    {
        var pair = new InMemoryDuplexPair();
        var conn = FramedConnection.FromDuplexPipe(pair.ServerEnd);
        var handlerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        conn.SetInboundHandler(async (_, _) =>
        {
            // In v1 this caused a 100% deterministic self-await deadlock on _receiverTask.
            await conn.DisposeAsync();
            handlerCompleted.TrySetResult();
        });
        conn.Start();

        var frame = PacketFrameCodec.SerializeToArray(new RequestWorldInfo(), fromServer: false);
        await pair.ClientEnd.Output.WriteAsync(frame);

        await handlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(conn.IsDisposed);
    }

    [Fact]
    public async Task SlowPeerBackpressure_TimesOutAndClosesInsteadOfUnboundedOom()
    {
        // Tiny transport pipe (4 KB) with nobody reading from the client side.
        var pair = new InMemoryDuplexPair(new PipeOptions(
            pauseWriterThreshold: 4096,
            resumeWriterThreshold: 2048,
            useSynchronizationContext: false));

        await using var conn = FramedConnection.FromDuplexPipe(pair.ServerEnd, options: new FramingOptions
        {
            PauseWriterThreshold = 4096,
            ResumeWriterThreshold = 2048,
            SendFlushTimeout = TimeSpan.FromMilliseconds(250),
        });

        var closedTcs = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.SetClosedHandler(ex =>
        {
            closedTcs.TrySetResult(ex);
            return ValueTask.CompletedTask;
        });
        conn.Start();

        var bigPayload = new byte[2048];
        BitConverter.TryWriteBytes(bigPayload.AsSpan(0, 2), (ushort)bigPayload.Length);
        bigPayload[2] = (byte)MessageID.WorldData;

        var hitBackpressureFailure = false;
        for (var i = 0; i < 64; i++)
        {
            if (!await conn.WriteFrameAsync(bigPayload))
            {
                hitBackpressureFailure = true;
                break;
            }
        }

        Assert.True(hitBackpressureFailure);
        var fault = await closedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(fault);
    }

    [Fact]
    public async Task PauseAndResumeReading_PreservesAllSubsequentFramesWithZeroLoss()
    {
        var pair = new InMemoryDuplexPair();
        await using var conn = FramedConnection.FromDuplexPipe(pair.ServerEnd);

        var phase1Count = 0;
        var phase2Messages = new List<MessageID>();
        var phase1Paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var phase2Done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        conn.SetInboundHandler((frame, _) =>
        {
            phase1Count++;
            conn.PauseReading();
            phase1Paused.TrySetResult();
            return ValueTask.CompletedTask;
        });
        conn.Start();

        // Send 3 frames back-to-back in one write.
        using var writer = new PooledBufferWriter();
        writer.WritePacket(new FinishedConnectingToServer(), fromServer: true);
        writer.WritePacket(new StartPlaying(), fromServer: true);
        writer.WritePacket(new RequestWorldInfo(), fromServer: true);
        await pair.ClientEnd.Output.WriteAsync(writer.WrittenMemory);

        await phase1Paused.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, phase1Count);

        // Swap handler while paused and resume — frames 2 and 3 must arrive intact in order.
        conn.SetInboundHandler((frame, _) =>
        {
            phase2Messages.Add((MessageID)PacketFrameCodec.CopyToArray(frame)[2]);
            if (phase2Messages.Count == 2)
                phase2Done.TrySetResult();
            return ValueTask.CompletedTask;
        });
        conn.ResumeReading();

        await phase2Done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal([MessageID.StartPlaying, MessageID.RequestWorldInfo], phase2Messages);
    }
}
