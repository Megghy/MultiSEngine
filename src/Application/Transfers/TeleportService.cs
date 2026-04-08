using MultiSEngine.Protocol.Adapters;

namespace MultiSEngine.Application.Transfers;

public static class TeleportService
{
    public const int FakeWorldSpawnX = 4200;
    public const int FakeWorldSpawnY = 1200;

    public static async ValueTask SendTeleportAsync(ClientData client, int tileX, int tileY)
    {
        if (client?.Adapter is not { } adapter)
            return;

        await adapter.SendToClientDirectAsync(new Teleport
        {
            Bit1 = new BitsByte(),
            PlayerSlot = client.Player.Index,
            Position = new Vector2(tileX * 16, tileY * 16),
            Style = 0,
            ExtraInfo = 0,
        }).ConfigureAwait(false);
    }

    public static ValueTask TeleportToTargetSpawnAsync(ClientData client, int spawnX, int spawnY)
        => SendTeleportAsync(client, spawnX, spawnY - 3);

    public static async ValueTask CompleteTargetEntryAsync(BaseAdapter adapter, ClientData client, PreConnectSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(session);

        if (!session.HasBufferedPacket(MessageID.WorldData) && session.World is { } world)
            await adapter.SendToClientDirectAsync(world, cancellationToken).ConfigureAwait(false);

        await session.FlushBufferedPacketsToClientAsync(adapter, cancellationToken).ConfigureAwait(false);
        await TeleportToTargetSpawnAsync(client, session.SpawnX, session.SpawnY).ConfigureAwait(false);
    }

    public static async ValueTask EnterFakeWorldAsync(ClientData client, CancellationToken cancellationToken = default)
    {
        if (client?.Adapter is not { } adapter)
            return;

        await adapter.SendToClientDirectAsync(RuntimeState.SpawnSquarePacket, cancellationToken).ConfigureAwait(false);
        await SendTeleportAsync(client, FakeWorldSpawnX, FakeWorldSpawnY).ConfigureAwait(false);
        await adapter.SendToClientDirectAsync(RuntimeState.DeactivateAllPlayerPacket, cancellationToken).ConfigureAwait(false);
    }
}
