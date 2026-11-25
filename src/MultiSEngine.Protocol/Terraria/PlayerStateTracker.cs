using Terraria;
using TrProtocol;
using TrProtocol.Models.Interfaces;
using TrProtocol.NetPackets;

namespace MultiSEngine.Protocol.Terraria;

public enum IdentityValidationResult
{
    Accepted,
    NameMismatch,
    UuidMismatch,
}

/// <summary>
/// Tracks both the client's original local character and the active target server's character state.
/// Enforces player-slot isolation so other players' broadcast packets can never corrupt this session.
/// </summary>
public sealed class PlayerStateTracker
{
    public string Name { get; private set; } = string.Empty;
    public string Uuid { get; private set; } = string.Empty;
    public PlayerPlatformInfo? PlatformInfo { get; private set; }
    public bool IdentityLocked { get; private set; }

    public int ProtocolVersion { get; set; }
    public byte ActiveSlot { get; set; }
    public float PositionX { get; private set; }
    public float PositionY { get; private set; }
    public short SpawnX { get; set; }
    public short SpawnY { get; set; }
    public int Timer { get; set; }
    public short DeathsPve { get; set; }
    public short DeathsPvp { get; set; }
    public byte Team { get; set; }
    public PlayerSpawnContext SpawnContext { get; set; }

    public CharacterSnapshot OriginCharacter { get; } = new();
    public CharacterSnapshot? ServerCharacter { get; private set; }

    public bool IsServerSideCharacter =>
        ServerCharacter?.WorldData is { } world && world.EventInfo1[6];

    public void LockIdentity() => IdentityLocked = true;

    public IdentityValidationResult ApplyClientPacket(INetPacket packet, bool inTargetServer)
    {
        switch (packet)
        {
            case SyncPlayer syncPlayer:
                if (IdentityLocked && !string.Equals(syncPlayer.Name, Name, StringComparison.Ordinal))
                    return IdentityValidationResult.NameMismatch;

                if (string.IsNullOrEmpty(Name))
                    Name = syncPlayer.Name ?? string.Empty;

                ApplyCharacterPacket(SelectTarget(inTargetServer, fromServer: false), syncPlayer);
                if (!inTargetServer)
                    OriginCharacter.Info = syncPlayer;
                return IdentityValidationResult.Accepted;

            case ClientUUID clientUuid:
                if (IdentityLocked && !string.Equals(clientUuid.UUID, Uuid, StringComparison.Ordinal))
                    return IdentityValidationResult.UuidMismatch;

                if (string.IsNullOrEmpty(Uuid))
                    Uuid = clientUuid.UUID ?? string.Empty;
                return IdentityValidationResult.Accepted;

            case PlayerPlatformInfo platformInfo:
                PlatformInfo = platformInfo;
                return IdentityValidationResult.Accepted;

            case SpawnPlayer spawn:
                ApplySpawnMetadata(spawn);
                return IdentityValidationResult.Accepted;

            default:
                ApplyCharacterPacket(SelectTarget(inTargetServer, fromServer: false), packet);
                return IdentityValidationResult.Accepted;
        }
    }

    public bool ApplyServerPacket(INetPacket packet, byte expectedPlayerSlot)
    {
        if (packet is IPlayerSlot slotted && slotted.PlayerSlot != expectedPlayerSlot)
            return false;

        switch (packet)
        {
            case LoadPlayer loadPlayer:
                ActiveSlot = loadPlayer.PlayerSlot;
                return true;

            case WorldData worldData:
                EnsureServerCharacter().WorldData = worldData;
                return true;

            case SpawnPlayer spawn:
                ApplySpawnMetadata(spawn);
                return true;

            default:
                ApplyCharacterPacket(EnsureServerCharacter(), packet);
                return true;
        }
    }

    public void ApplyTargetHandshake(WorldData worldData, byte remoteSlot, short spawnX, short spawnY)
    {
        ActiveSlot = remoteSlot;
        SpawnX = spawnX;
        SpawnY = spawnY;
        EnsureServerCharacter().WorldData = worldData;
    }

    public void ResetToLobby()
    {
        ActiveSlot = 0;
        ServerCharacter = null;
        SpawnX = LobbyProtocol.SpawnX;
        SpawnY = LobbyProtocol.SpawnY;
    }

    public SyncPlayer CreateRemoteSyncPlayer(byte remoteSlot)
    {
        var source = OriginCharacter.Info
            ?? ServerCharacter?.Info
            ?? throw new InvalidOperationException("SyncPlayer information is unavailable.");
        source.PlayerSlot = remoteSlot;
        return source;
    }

    private CharacterSnapshot SelectTarget(bool inTargetServer, bool fromServer)
    {
        if (fromServer)
            return EnsureServerCharacter();

        if (!inTargetServer)
            return OriginCharacter;

        return IsServerSideCharacter ? EnsureServerCharacter() : OriginCharacter;
    }

    private CharacterSnapshot EnsureServerCharacter() => ServerCharacter ??= new CharacterSnapshot();

    private void ApplySpawnMetadata(in SpawnPlayer spawn)
    {
        SpawnX = spawn.Position.X;
        SpawnY = spawn.Position.Y;
        Timer = spawn.Timer;
        DeathsPve = spawn.DeathsPVE;
        DeathsPvp = spawn.DeathsPVP;
        Team = spawn.Team;
        SpawnContext = spawn.Context;
    }

    private void ApplyCharacterPacket(CharacterSnapshot target, INetPacket packet)
    {
        switch (packet)
        {
            case SyncPlayer info:
                target.Info = info;
                break;
            case SyncEquipment equipment:
                target.TryStoreEquipment(equipment);
                break;
            case SyncLoadout loadout:
                target.Loadout = loadout;
                break;
            case PlayerHealth health:
                target.Health = health.StatLife;
                target.HealthMax = health.StatLifeMax;
                break;
            case PlayerMana mana:
                target.Mana = mana.StatMana;
                target.ManaMax = mana.StatManaMax;
                break;
            case PlayerBuffs buffs:
                target.Buffs = buffs;
                break;
            case PlayerTeam team:
                target.Team = team;
                Team = team.Team;
                break;
            case PlayerPvP pvp:
                target.Pvp = pvp;
                break;
            case PlayerZone zone:
                target.Zone = zone;
                break;
            case PlayerStealth stealth:
                target.Stealth = stealth;
                break;
            case UpdatePlayerLuckFactors luck:
                target.Luck = luck;
                break;
            case PlayerControls controls:
                PositionX = controls.Position.X;
                PositionY = controls.Position.Y;
                break;
            case WorldData worldData:
                target.WorldData = worldData;
                break;
        }
    }
}
