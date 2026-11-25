using Microsoft.Xna.Framework;
using MultiSEngine.Protocol.Framing;
using Terraria;
using Terraria.Localization;
using TrProtocol.NetPackets;
using TrProtocol.NetPackets.Modules;

namespace MultiSEngine.Protocol.Terraria;

public static class TerrariaPacketFactory
{
    public const byte ServerChatSlot = 255;

    public static Color Rgb(byte r, byte g, byte b, byte a = byte.MaxValue)
        => new() { R = r, G = g, B = b, A = a };

    public static NetworkText LiteralText(string text)
        => new(text ?? string.Empty, NetworkText.Mode.Literal);

    public static NetTextModule CreateServerChat(string text, Color color, byte playerSlot = ServerChatSlot)
        => new()
        {
            TextS2C = new TextS2C
            {
                PlayerSlot = playerSlot,
                Text = LiteralText(text),
                Color = color,
            },
        };

    public static NetTextModule CreateClientChat(string text, string command = "Say")
        => new()
        {
            TextC2S = new TextC2S
            {
                Command = command,
                Text = text,
            },
        };

    public static Kick CreateKick(string reason)
        => new() { Reason = LiteralText(reason) };

    public static Teleport CreateTeleport(byte playerSlot, short tileX, short tileY, byte style = 0)
        => new()
        {
            Bit1 = new BitsByte(),
            PlayerSlot = playerSlot,
            Position = new Vector2(tileX * 16f, (tileY - 3) * 16f),
            Style = style,
        };

    public static AddPlayerBuff CreateBuff(byte playerSlot, ushort buffType, int durationTicks)
        => new()
        {
            OtherPlayerSlot = playerSlot,
            BuffType = buffType,
            BuffTime = durationTicks,
        };
}

/// <summary>
/// Builds deterministic, slot-safe client synchronization batches for server switches and lobby returns.
/// Guarantees WorldData is emitted the minimal required number of times and PlayerSlot is always rewritten.
/// </summary>
public static class TerrariaSyncEngine
{
    public static void BuildTargetSwitchBatch(
        PooledBufferWriter writer,
        PlayerStateTracker tracker,
        WorldData targetWorldData,
        byte targetSlot,
        short spawnX,
        short spawnY,
        ReadOnlyMemory<byte> bufferedTargetPackets,
        bool restoreDataWhenJoinNonSsc)
    {
        // 1. Deactivate all other players from previous world so no ghosts remain.
        LobbyProtocol.WriteDeactivateOtherPlayers(writer, excludeSlot: targetSlot);

        // 2. Inform client of its new player slot on the target server.
        writer.WritePacket(new LoadPlayer
        {
            PlayerSlot = targetSlot,
            ServerWantsToRunCheckBytesInClientLoopThread = false,
        }, fromServer: true);

        // 3. Synchronize WorldData + optional non-SSC character restoration.
        var targetIsSsc = targetWorldData.EventInfo1[6];
        if (!targetIsSsc && restoreDataWhenJoinNonSsc && tracker.OriginCharacter.Info is not null)
        {
            WriteNonSscRestoreSequence(writer, tracker.OriginCharacter, targetWorldData, targetSlot);
        }
        else
        {
            writer.WritePacket(targetWorldData, fromServer: true);
        }

        // 4. Replay only filtered world/chunk/entity initialization packets captured during pre-connect.
        if (!bufferedTargetPackets.IsEmpty)
            writer.Write(bufferedTargetPackets.Span);

        // 5. Teleport player to validated target spawn.
        writer.WritePacket(TerrariaPacketFactory.CreateTeleport(targetSlot, spawnX, spawnY), fromServer: true);
    }

    public static void BuildReturnToLobbyBatch(
        PooledBufferWriter writer,
        PlayerStateTracker tracker,
        string lobbyWorldName,
        bool restoreOriginCharacter)
    {
        const byte lobbySlot = 0;
        var lobbyWorld = tracker.OriginCharacter.WorldData ?? LobbyProtocol.CreateLobbyWorldData(lobbyWorldName);
        lobbyWorld.WorldName = lobbyWorldName;
        tracker.OriginCharacter.WorldData = lobbyWorld;

        // 1. Reset remote player slot back to 0 and deactivate all other players.
        writer.WritePacket(new LoadPlayer
        {
            PlayerSlot = lobbySlot,
            ServerWantsToRunCheckBytesInClientLoopThread = false,
        }, fromServer: true);
        LobbyProtocol.WriteDeactivateOtherPlayers(writer, excludeSlot: lobbySlot);

        // 2. Restore local character snapshot into lobby world.
        if (restoreOriginCharacter && tracker.OriginCharacter.Info is not null)
        {
            WriteNonSscRestoreSequence(writer, tracker.OriginCharacter, lobbyWorld, lobbySlot);
        }
        else
        {
            writer.WritePacket(lobbyWorld, fromServer: true);
        }

        // 3. Send lobby spawn tiles and teleport to lobby spawn.
        writer.Write(LobbyProtocol.SpawnSquarePacket.Span);
        writer.WritePacket(TerrariaPacketFactory.CreateTeleport(lobbySlot, LobbyProtocol.SpawnX, LobbyProtocol.SpawnY), fromServer: true);
    }

    private static void WriteNonSscRestoreSequence(
        PooledBufferWriter writer,
        CharacterSnapshot origin,
        WorldData baseWorldData,
        byte playerSlot)
    {
        var tempWorld = baseWorldData;
        var sscOn = tempWorld.EventInfo1;
        sscOn[6] = true;
        tempWorld.EventInfo1 = sscOn;
        writer.WritePacket(tempWorld, fromServer: true);

        writer.WritePacket(origin.CreateSyncPlayerPacket(playerSlot), fromServer: true);
        writer.WritePacket(new PlayerHealth
        {
            PlayerSlot = playerSlot,
            StatLife = origin.Health,
            StatLifeMax = origin.HealthMax,
        }, fromServer: true);
        writer.WritePacket(new PlayerMana
        {
            PlayerSlot = playerSlot,
            StatMana = origin.Mana,
            StatManaMax = origin.ManaMax,
        }, fromServer: true);

        if (origin.Buffs is { } buffs)
        {
            buffs.PlayerSlot = playerSlot;
            writer.WritePacket(buffs, fromServer: true);
        }

        if (origin.Team is { } team)
        {
            team.PlayerSlot = playerSlot;
            writer.WritePacket(team, fromServer: true);
        }

        if (origin.Pvp is { } pvp)
        {
            pvp.PlayerSlot = playerSlot;
            writer.WritePacket(pvp, fromServer: true);
        }

        if (origin.Stealth is { } stealth)
        {
            stealth.PlayerSlot = playerSlot;
            writer.WritePacket(stealth, fromServer: true);
        }

        if (origin.Luck is { } luck)
        {
            luck.PlayerSlot = playerSlot;
            writer.WritePacket(luck, fromServer: true);
        }

        writer.WritePacket(origin.CreateSyncLoadoutPacket(playerSlot), fromServer: true);
        foreach (var equip in origin.EnumerateSyncEquipment(playerSlot))
            writer.WritePacket(equip, fromServer: true);

        var finalWorld = baseWorldData;
        var sscOff = finalWorld.EventInfo1;
        sscOff[6] = false;
        finalWorld.EventInfo1 = sscOff;
        writer.WritePacket(finalWorld, fromServer: true);
    }
}
