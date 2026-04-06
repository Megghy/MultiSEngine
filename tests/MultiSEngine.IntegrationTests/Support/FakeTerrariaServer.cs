using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using TrProtocol;

namespace MultiSEngine.IntegrationTests.Support;

internal sealed class FakeTerrariaServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly PacketSerializer _serverDeserializer = new(false);
    private readonly PacketSerializer _serverSerializer = new(true);
    private readonly Channel<object> _receivedPackets = Channel.CreateUnbounded<object>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<TcpClient> _acceptedClient = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _acceptTask;

    private TcpClient? _client;

    public FakeTerrariaServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = AcceptAndReadAsync();
    }

    public int Port { get; }

    public async Task WaitForConnectionAsync(TimeSpan timeout)
        => await _acceptedClient.Task.WaitAsync(timeout);

    public async Task<TPacket> WaitForPacketAsync<TPacket>(TimeSpan timeout)
        where TPacket : struct, INetPacket
    {
        using var timeoutCts = new CancellationTokenSource(timeout);

        while (await _receivedPackets.Reader.WaitToReadAsync(timeoutCts.Token))
        {
            while (_receivedPackets.Reader.TryRead(out var packet))
            {
                if (packet is TPacket typedPacket)
                {
                    return typedPacket;
                }
            }
        }

        throw new TimeoutException($"Timed out waiting for packet {typeof(TPacket).Name}.");
    }

    public async Task SendAsync(INetPacket packet, CancellationToken cancellationToken = default)
    {
        var client = await _acceptedClient.Task.WaitAsync(cancellationToken);
        var bytes = _serverSerializer.Serialize(packet);
        await client.GetStream().WriteAsync(bytes, cancellationToken);
    }

    private async Task AcceptAndReadAsync()
    {
        try
        {
            var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            _client = client;
            _acceptedClient.TrySetResult(client);

            while (!_cts.IsCancellationRequested)
            {
                var packet = await ReadPacketAsync(client.GetStream(), _cts.Token);
                if (packet is null)
                {
                    break;
                }

                await _receivedPackets.Writer.WriteAsync(packet, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _acceptedClient.TrySetCanceled(_cts.Token);
        }
        catch (Exception ex)
        {
            _acceptedClient.TrySetException(ex);
            _receivedPackets.Writer.TryComplete(ex);
            return;
        }

        _receivedPackets.Writer.TryComplete();
    }

    private async Task<object?> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        if (!await ReadExactlyOrEndAsync(stream, header, cancellationToken))
        {
            return null;
        }

        var length = BitConverter.ToUInt16(header, 0);
        if (length < 2)
        {
            throw new IOException($"Invalid packet length: {length}");
        }

        var packetBytes = new byte[length];
        header.CopyTo(packetBytes, 0);
        if (!await ReadExactlyOrEndAsync(stream, packetBytes.AsMemory(2, length - 2), cancellationToken))
        {
            return null;
        }

        using var memory = new MemoryStream(packetBytes);
        using var reader = new BinaryReader(memory);
        return _serverDeserializer.Deserialize(reader);
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        try
        {
            _client?.Dispose();
            await _acceptTask.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
