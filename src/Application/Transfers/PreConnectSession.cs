using MultiSEngine.Protocol.Adapters;

namespace MultiSEngine.Application.Transfers;

public sealed class PreConnectSession(ServerInfo targetServer) : IDisposable
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Utils.PacketMemoryRental> _bufferedPackets = [];
    private readonly HashSet<MessageID> _bufferedPacketTypes = [];

    public ServerInfo TargetServer { get; } = targetServer;

    public bool IsConnecting { get; private set; } = true;

    public byte RemotePlayerIndex { get; private set; }

    public short SpawnX { get; private set; } = -1;

    public short SpawnY { get; private set; } = -1;

    public WorldData? World { get; private set; }

    public string? FailureReason { get; private set; }

    public Task<bool> CompletionTask => _completion.Task;

    public void SetRemotePlayerIndex(byte playerIndex)
        => RemotePlayerIndex = playerIndex;

    public void UpdateWorldData(WorldData worldData, short spawnX, short spawnY)
    {
        World ??= worldData;
        SpawnX = spawnX;
        SpawnY = spawnY;
    }

    public void BufferPacket(ReadOnlyMemory<byte> packet)
    {
        if (packet.Length < 3)
            return;

        _bufferedPackets.Add(packet.AsPacketRental());
        _bufferedPacketTypes.Add((MessageID)packet.Span[2]);
    }

    public bool HasBufferedPacket(MessageID messageId)
        => _bufferedPacketTypes.Contains(messageId);

    public async ValueTask FlushBufferedPacketsToClientAsync(BaseAdapter adapter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        if (_bufferedPackets.Count == 0)
            return;

        var buffers = new ReadOnlyMemory<byte>[_bufferedPackets.Count];
        for (var i = 0; i < _bufferedPackets.Count; i++)
        {
            buffers[i] = _bufferedPackets[i].Memory;
        }
        try
        {
            await adapter.SendToClientBatchAsync(buffers, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DisposeBufferedPackets();
        }
    }

    public void MarkSucceeded()
    {
        IsConnecting = false;
        _completion.TrySetResult(true);
    }

    public void MarkFailed(string? reason = null)
    {
        FailureReason = reason;
        IsConnecting = false;
        _completion.TrySetResult(false);
    }

    public void Dispose()
        => DisposeBufferedPackets();

    private void DisposeBufferedPackets()
    {
        foreach (var rental in _bufferedPackets)
            rental.Dispose();
        _bufferedPackets.Clear();
        _bufferedPacketTypes.Clear();
    }
}
