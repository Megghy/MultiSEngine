namespace MultiSEngine.Application.Transfers;

public static class PlayerSyncService
{
    public static async ValueTask SyncClientAsync(ClientData client, CancellationToken cancellationToken = default)
    {
        Logs.Text($"Syncing player: [{client.Name}]");
        client.Syncing = true;

        using var batchWriter = new Utils.PooledBufferWriter();

        void EnqueuePacket(INetPacket packet)
            => batchWriter.WritePacket(packet, fromServer: true);

        var data = client.Player.ServerCharacter?.WorldData ?? client.Player.OriginCharacter.WorldData ?? throw new Exception("[PlayerSyncService] World data not available for sync");
        if (!client.Player.SSC && Config.Instance.RestoreDataWhenJoinNonSSC)
        {
            var bb = data.EventInfo1;
            bb[6] = true;
            data.EventInfo1 = bb;
            EnqueuePacket(data);
            EnqueuePacket(client.Player.OriginCharacter.Info ?? throw new Exception("[PlayerSyncService] Origin player info not available for sync"));
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
            client.Player.OriginCharacter
                .EnumerateSyncEquipment(client.Player.Index)
                .ForEach(packet => EnqueuePacket(packet));
            bb[6] = false;
            data.EventInfo1 = bb;
            EnqueuePacket(data);
        }
        else
        {
            EnqueuePacket(data);
        }

        try
        {
            await DispatchBatchToClientAsync(client, batchWriter.WrittenMemory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            client.Syncing = false;
        }
    }

    private static async ValueTask DispatchBatchToClientAsync(ClientData client, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.IsEmpty)
            return;

        var adapter = client.Adapter;
        if (adapter is null)
            return;

        try
        {
            await adapter.SendToClientDirectAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logs.Warn($"Failed to send batch data to {client.Name}{Environment.NewLine}{ex}");
        }
    }
}
