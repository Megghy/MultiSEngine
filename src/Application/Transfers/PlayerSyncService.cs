namespace MultiSEngine.Application.Transfers;

public static class PlayerSyncService
{
    public static async ValueTask SyncClientAsync(ClientData client, CancellationToken cancellationToken = default)
    {
        Logs.Text($"Syncing player: [{client.Name}]");
        client.Syncing = true;

        var rentals = new List<Utils.PacketMemoryRental>();

        void EnqueuePacket(INetPacket packet)
        {
            var rental = packet.AsPacketRental(true);
            rentals.Add(rental);
        }

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
            await DispatchBatchToClientAsync(client, rentals, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            client.Syncing = false;
        }
    }

    private static async ValueTask DispatchBatchToClientAsync(ClientData client, List<Utils.PacketMemoryRental> rentals, CancellationToken cancellationToken)
    {
        if (rentals.Count == 0)
        {
            foreach (var rental in rentals)
                rental.Dispose();
            return;
        }

        var adapter = client.Adapter;
        if (adapter is null)
        {
            foreach (var rental in rentals)
                rental.Dispose();
            return;
        }

        var bufferArray = new ReadOnlyMemory<byte>[rentals.Count];
        var rentalArray = new Utils.PacketMemoryRental[rentals.Count];
        for (var index = 0; index < rentals.Count; index++)
        {
            rentalArray[index] = rentals[index];
            bufferArray[index] = rentals[index].Memory;
        }

        rentals.Clear();

        try
        {
            await adapter.SendToClientBatchAsync(bufferArray, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logs.Warn($"Failed to send batch data to {client.Name}{Environment.NewLine}{ex}");
        }
        finally
        {
            foreach (var rental in rentalArray)
                rental.Dispose();
        }
    }
}
