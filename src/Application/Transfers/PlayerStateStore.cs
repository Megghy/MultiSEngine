namespace MultiSEngine.Application.Transfers;

public static class PlayerStateStore
{
    public static void ApplyPacket(PlayerInfo player, INetPacket packet, bool fromClient)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(packet);

        var data = ResolveTargetData(player, fromClient);
        switch (packet)
        {
            case SyncEquipment item:
                data.TryStoreEquipment(item);
                break;
            case SyncLoadout loadout:
                data.Loadout = loadout;
                break;
            case PlayerHealth health:
                data.Health = health.StatLife;
                data.HealthMax = health.StatLifeMax;
                break;
            case PlayerMana mana:
                data.Mana = mana.StatMana;
                data.ManaMax = mana.StatManaMax;
                break;
            case SyncPlayer playerInfo:
                data.Info = playerInfo;
                break;
            case WorldData world:
                world.WorldName = string.IsNullOrEmpty(Config.Instance.ServerName) ? world.WorldName : Config.Instance.ServerName;
                player.ServerCharacter.WorldData = world;
                break;
            case PlayerControls control:
                player.X = control.Position.X;
                player.Y = control.Position.Y;
                break;
        }
    }

    public static SyncPlayer CreateRemoteSyncPlayer(PlayerInfo player, byte remotePlayerIndex)
    {
        ArgumentNullException.ThrowIfNull(player);

        var playerInfo = player.OriginCharacter.Info ?? throw new Exception("[PlayerStateStore] Origin player info not found");
        playerInfo.PlayerSlot = remotePlayerIndex;
        return playerInfo;
    }

    public static void ApplySpawn(PlayerInfo player, SpawnPlayer spawn)
    {
        ArgumentNullException.ThrowIfNull(player);

        player.SpawnX = spawn.Position.X;
        player.SpawnY = spawn.Position.Y;
        player.DeathsPVE = spawn.DeathsPVE;
        player.DeathsPVP = spawn.DeathsPVP;
        player.Timer = spawn.Timer;
        player.Context = spawn.Context;
    }

    public static void ApplyTargetSession(PlayerInfo player, PreConnectSession session)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(session);

        player.ServerCharacter.WorldData = session.World ?? throw new Exception("[PlayerStateStore] Target world data not available");
        player.SpawnX = session.SpawnX;
        player.SpawnY = session.SpawnY;
    }

    public static void ResetTargetCharacter(PlayerInfo player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.ServerCharacter = new();
        player.SpawnX = -1;
        player.SpawnY = -1;
    }

    private static CharacterData ResolveTargetData(PlayerInfo player, bool fromClient)
    {
        if (!fromClient)
            return player.ServerCharacter;

        return player.SSC ? player.ServerCharacter : player.OriginCharacter;
    }
}
