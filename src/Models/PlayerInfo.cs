namespace MultiSEngine.Models;

public sealed class PlayerInfo(bool isOriginCharacter = false)
{
    public bool IsOriginCharacter => isOriginCharacter;

    public bool SSC => ServerCharacter.WorldData?.EventInfo1[6] ?? false;

    public int VersionNum { get; set; } = -1;

    public byte Index { get; set; }

    public string Name => (ServerCharacter.Info ?? OriginCharacter.Info)?.Name ?? string.Empty;

    public string UUID { get; set; } = string.Empty;

    public int SpawnX { get; set; } = -1;

    public int SpawnY { get; set; } = -1;

    public short WorldSpawnX => (ServerCharacter.WorldData ?? OriginCharacter.WorldData)?.SpawnX ?? 0;

    public short WorldSpawnY => (ServerCharacter.WorldData ?? OriginCharacter.WorldData)?.SpawnY ?? 0;

    public float X { get; set; } = -1;

    public float Y { get; set; } = -1;

    public int TileX => (int)(X / 16);

    public int TileY => (int)(Y / 16);

    public int Timer;

    public short DeathsPVE;

    public short DeathsPVP;

    public PlayerSpawnContext Context = PlayerSpawnContext.SpawningIntoWorld;

    public CharacterData OriginCharacter { get; set; } = new();

    public CharacterData ServerCharacter { get; set; } = new();

    public void UpdateData(INetPacket packet, bool fromClient)
    {
        if (packet is null)
            return;

        var data = ResolveTargetData(fromClient);
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
                ServerCharacter.WorldData = world;
                break;
            case PlayerControls control:
                X = control.Position.X;
                Y = control.Position.Y;
                break;
        }
    }

    private CharacterData ResolveTargetData(bool fromClient)
    {
        if (!fromClient)
            return ServerCharacter;

        return SSC ? ServerCharacter : OriginCharacter;
    }
}
