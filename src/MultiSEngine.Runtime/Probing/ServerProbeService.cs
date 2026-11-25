using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Logging;
using MultiSEngine.Core.Transfers;
using MultiSEngine.Protocol.Framing;
using MultiSEngine.Protocol.Terraria;
using MultiSEngine.Runtime.Infrastructure;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.Runtime.Probing;

/// <summary>
/// Standalone, concurrency-safe Terraria backend connectivity prober.
/// Replaces the v1 TestAdapter/TestHandler inheritance hack and broken static lock.
/// </summary>
public sealed class ServerProbeService(IConfigStore configStore, IProxyLogger logger) : IServerProbeService
{
    public async Task<IReadOnlyList<ProbeResult>> ProbeAllAsync(
        IEnumerable<ServerDescriptor> servers,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var list = servers.ToArray();
        var tasks = list.Select(s => ProbeAsync(s, timeout, cancellationToken)).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async Task<ProbeResult> ProbeAsync(
        ServerDescriptor server,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        var sw = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var ct = timeoutCts.Token;

        var address = await NetworkAddressResolver.ResolveHostAsync(server.Host, ct).ConfigureAwait(false);
        if (address is null)
            return new ProbeResult(server, false, sw.Elapsed, $"Unable to resolve host '{server.Host}'.");

        using var tcp = new TcpClient();
        FramedConnection? connection = null;
        var completion = new TaskCompletionSource<ProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await tcp.ConnectAsync(address, server.Port, ct).ConfigureAwait(false);
            connection = FramedConnection.FromTcpClient(tcp, new FramingOptions
            {
                DisableTcpDelay = configStore.Current.DisableTcpDelay,
                SendFlushTimeout = timeout,
            });

            connection.SetInboundHandler((frame, token) => HandleProbeFrameAsync(connection, server, sw, completion, frame, token));
            connection.SetClosedHandler(ex =>
            {
                completion.TrySetResult(new ProbeResult(server, false, sw.Elapsed, ex?.Message ?? "Connection closed by remote server."));
                return ValueTask.CompletedTask;
            });
            connection.Start();

            var protocolVersion = server.VersionNum > 0 ? server.VersionNum : configStore.Current.ServerVersion;
            var sent = await connection.WritePacketAsync(new ClientHello
            {
                Version = TerrariaVersions.ToClientHelloString(protocolVersion),
            }, fromServer: false, ct).ConfigureAwait(false);

            if (!sent)
                return new ProbeResult(server, false, sw.Elapsed, "Failed to send ClientHello.");

            return await completion.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(server, false, sw.Elapsed, $"Probe timed out after {timeout.TotalMilliseconds:F0}ms.");
        }
        catch (Exception ex)
        {
            logger.Debug($"Probe to [{server.Name}] failed: {ex.Message}", "Probe");
            return new ProbeResult(server, false, sw.Elapsed, ex.Message);
        }
        finally
        {
            if (connection is not null)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async ValueTask HandleProbeFrameAsync(
        FramedConnection connection,
        ServerDescriptor server,
        Stopwatch sw,
        TaskCompletionSource<ProbeResult> completion,
        ReadOnlySequence<byte> frame,
        CancellationToken cancellationToken)
    {
        var msgId = FastPathClassifier.PeekMessageId(frame);
        switch (msgId)
        {
            case MessageID.Kick:
                var kick = (Kick)PacketFrameCodec.Deserialize(frame, fromServer: true);
                completion.TrySetResult(new ProbeResult(server, false, sw.Elapsed, $"Kicked: {kick.Reason}"));
                break;

            case MessageID.RequestPassword:
                completion.TrySetResult(new ProbeResult(server, false, sw.Elapsed, "Target server requires a password."));
                break;

            case MessageID.LoadPlayer:
                var load = (LoadPlayer)PacketFrameCodec.Deserialize(frame, fromServer: true);
                await connection.WritePacketAsync(new SyncPlayer
                {
                    PlayerSlot = load.PlayerSlot,
                    Name = "MultiSEngineProbe",
                }, fromServer: false, cancellationToken).ConfigureAwait(false);
                await connection.WritePacketAsync(new RequestWorldInfo(), fromServer: false, cancellationToken).ConfigureAwait(false);
                break;

            case MessageID.WorldData:
                var world = (WorldData)PacketFrameCodec.Deserialize(frame, fromServer: true);
                await connection.WritePacketAsync(new RequestTileData
                {
                    Position = new Microsoft.Xna.Framework.Point(world.SpawnX, world.SpawnY),
                }, fromServer: false, cancellationToken).ConfigureAwait(false);
                await connection.WritePacketAsync(new SpawnPlayer
                {
                    PlayerSlot = 0,
                    Position = new Terraria.DataStructures.Point16(world.SpawnX, world.SpawnY),
                    Context = Terraria.PlayerSpawnContext.SpawningIntoWorld,
                }, fromServer: false, cancellationToken).ConfigureAwait(false);
                break;

            case MessageID.StartPlaying:
            case MessageID.FinishedConnectingToServer:
                completion.TrySetResult(new ProbeResult(server, true, sw.Elapsed));
                break;
        }
    }
}
