using System.Buffers;
using MultiSEngine.Protocol.Framing;
using MultiSEngine.Protocol.Terraria;
using Terraria;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.Tests;

public sealed class ProtocolAndSyncTests
{
    [Fact]
    public void FastPathClassifier_FiltersOtherPlayerBroadcastsInO1WithoutDeserialization()
    {
        var mySlot = (byte)5;
        var otherControls = PacketFrameCodec.SerializeToArray(new PlayerControls { PlayerSlot = 9 }, fromServer: true);
        var myControls = PacketFrameCodec.SerializeToArray(new PlayerControls { PlayerSlot = mySlot }, fromServer: true);

        Assert.Equal(
            InboundRouteDecision.FastPassThrough,
            FastPathClassifier.ClassifyServerFrame(new ReadOnlySequence<byte>(otherControls), mySlot, hasMiddlewareForMessage: false));

        Assert.Equal(
            InboundRouteDecision.Inspect,
            FastPathClassifier.ClassifyServerFrame(new ReadOnlySequence<byte>(myControls), mySlot, hasMiddlewareForMessage: false));

        // When middleware subscribes to PlayerControls, it must inspect even other players.
        Assert.Equal(
            InboundRouteDecision.Inspect,
            FastPathClassifier.ClassifyServerFrame(new ReadOnlySequence<byte>(otherControls), mySlot, hasMiddlewareForMessage: true));
    }

    [Fact]
    public void PlayerStateTracker_IgnoresOtherPlayerServerPacketsAndLocksIdentity()
    {
        var tracker = new PlayerStateTracker { ActiveSlot = 3 };

        Assert.Equal(IdentityValidationResult.Accepted, tracker.ApplyClientPacket(new SyncPlayer { PlayerSlot = 0, Name = "Alice" }, inTargetServer: false));
        Assert.Equal(IdentityValidationResult.Accepted, tracker.ApplyClientPacket(new ClientUUID { UUID = "uuid-alice" }, inTargetServer: false));
        tracker.LockIdentity();

        // Name / UUID tampering must always be rejected once identity is locked (even in Lobby!).
        Assert.Equal(IdentityValidationResult.NameMismatch, tracker.ApplyClientPacket(new SyncPlayer { PlayerSlot = 0, Name = "Evil" }, inTargetServer: false));
        Assert.Equal(IdentityValidationResult.UuidMismatch, tracker.ApplyClientPacket(new ClientUUID { UUID = "uuid-evil" }, inTargetServer: false));

        // Server broadcasts for player slot 9 MUST NOT overwrite Alice's (slot 3) health or buffs.
        Assert.True(tracker.ApplyServerPacket(new PlayerHealth { PlayerSlot = 3, StatLife = 400, StatLifeMax = 500 }, expectedPlayerSlot: 3));
        Assert.False(tracker.ApplyServerPacket(new PlayerHealth { PlayerSlot = 9, StatLife = 10, StatLifeMax = 100 }, expectedPlayerSlot: 3));
        Assert.Equal(400, tracker.ServerCharacter!.Health);

        // Buffs tracking works for own slot.
        var buffs = new PlayerBuffs { PlayerSlot = 3, BuffTypes = new ushort[44] };
        buffs.BuffTypes[0] = 26;
        Assert.True(tracker.ApplyServerPacket(buffs, expectedPlayerSlot: 3));
        Assert.NotNull(tracker.ServerCharacter.Buffs);
        Assert.Equal((ushort)26, tracker.ServerCharacter.Buffs!.Value.BuffTypes[0]);
    }

    [Theory]
    [InlineData(139)]
    [InlineData(298)]
    [InlineData(339)]
    [InlineData(498)]
    [InlineData(540)]
    [InlineData(699)]
    [InlineData(740)]
    [InlineData(899)]
    [InlineData(990)]
    public void CharacterSnapshot_IgnoresReservedNetworkPaddingSlots(short reservedSlot)
    {
        var snapshot = new CharacterSnapshot();
        var stored = snapshot.TryStoreEquipment(new SyncEquipment
        {
            ItemSlot = reservedSlot,
            ItemType = 1,
            Stack = 1,
        });
        Assert.False(stored);
    }

    [Fact]
    public void LobbyProtocol_WriteDeactivateOtherPlayers_ExcludesSelfAndHasExactCount()
    {
        using var writer = new PooledBufferWriter(2048);
        const byte mySlot = 7;
        LobbyProtocol.WriteDeactivateOtherPlayers(writer, excludeSlot: mySlot);

        var seq = new ReadOnlySequence<byte>(writer.WrittenMemory);
        var deactivatedSlots = new HashSet<byte>();
        while (PacketFrameCodec.TryReadFrame(ref seq, out var frame))
        {
            var packet = Assert.IsType<PlayerActive>(PacketFrameCodec.Deserialize(frame, fromServer: true));
            Assert.False(packet.Active);
            Assert.True(deactivatedSlots.Add(packet.PlayerSlot));
        }

        Assert.Equal(254, deactivatedSlots.Count);
        Assert.DoesNotContain(mySlot, deactivatedSlots);
        Assert.Contains((byte)0, deactivatedSlots);
        Assert.Contains((byte)254, deactivatedSlots);
    }

    [Fact]
    public void TerrariaSyncEngine_EmitsMinimalWorldDataAndRewritesPlayerSlot()
    {
        var tracker = new PlayerStateTracker();
        tracker.ApplyClientPacket(new SyncPlayer { PlayerSlot = 0, Name = "Bob" }, inTargetServer: false);
        tracker.ApplyClientPacket(new PlayerHealth { PlayerSlot = 0, StatLife = 300, StatLifeMax = 400 }, inTargetServer: false);
        tracker.ApplyClientPacket(new PlayerMana { PlayerSlot = 0, StatMana = 120, StatManaMax = 200 }, inTargetServer: false);

        // Case 1: SSC target server -> WorldData emitted EXACTLY ONCE.
        var sscBits = new BitsByte();
        sscBits[6] = true;
        var sscWorld = new WorldData { WorldName = "SSCWorld", MaxTileX = 4200, MaxTileY = 1200, SpawnX = 2100, SpawnY = 600, EventInfo1 = sscBits, WorldUniqueID = Guid.NewGuid(), ExtraSpawnPoints = [] };

        using (var writer = new PooledBufferWriter(8192))
        {
            TerrariaSyncEngine.BuildTargetSwitchBatch(writer, tracker, sscWorld, targetSlot: 11, spawnX: 2100, spawnY: 600, ReadOnlyMemory<byte>.Empty, restoreDataWhenJoinNonSsc: true);
            var packets = DecodeAll(writer.WrittenMemory);
            Assert.Equal(1, packets.Count(p => p is WorldData));
            Assert.Contains(packets, p => p is LoadPlayer { PlayerSlot: 11 });
            Assert.IsType<Teleport>(packets[^1]);
            Assert.Equal((byte)11, ((Teleport)packets[^1]).PlayerSlot);
        }

        // Case 2: Non-SSC target server -> WorldData toggled ON then OFF (exactly 2), and SyncPlayer PlayerSlot rewritten to 11.
        var nonSscWorld = new WorldData { WorldName = "FreeWorld", MaxTileX = 4200, MaxTileY = 1200, SpawnX = 2100, SpawnY = 600, EventInfo1 = new BitsByte(), WorldUniqueID = Guid.NewGuid(), ExtraSpawnPoints = [] };

        using (var writer = new PooledBufferWriter(16384))
        {
            TerrariaSyncEngine.BuildTargetSwitchBatch(writer, tracker, nonSscWorld, targetSlot: 11, spawnX: 2100, spawnY: 600, ReadOnlyMemory<byte>.Empty, restoreDataWhenJoinNonSsc: true);
            var packets = DecodeAll(writer.WrittenMemory);
            var worldPackets = packets.OfType<WorldData>().ToArray();
            Assert.Equal(2, worldPackets.Length);
            Assert.True(worldPackets[0].EventInfo1[6]);
            Assert.False(worldPackets[1].EventInfo1[6]);

            var syncPlayer = packets.OfType<SyncPlayer>().Single();
            Assert.Equal((byte)11, syncPlayer.PlayerSlot);
            Assert.Equal("Bob", syncPlayer.Name);
        }
    }

    [Fact]
    public void Terraria1461Protocol_RoundTripsVersionAndNewPackets()
    {
        Assert.Equal("v1.4.6.1", TerrariaVersions.ToVersionString(329));
        Assert.Equal(329, Constants.ProtocolVersion);
        Assert.Equal("Terraria329", Constants.VersionString);
        Assert.True(Constants.tileFrameImportant[753]);

        // WorldData trailing DungeonX / DungeonY round-trip
        var world = new WorldData
        {
            WorldName = "World1461",
            MaxTileX = 4200,
            MaxTileY = 1200,
            SpawnX = 2100,
            SpawnY = 600,
            WorldUniqueID = Guid.NewGuid(),
            ExtraSpawnPoints = [],
            DungeonX = 412,
            DungeonY = 237,
        };
        var worldBytes = PacketFrameCodec.SerializeToArray(world, fromServer: true);
        var decodedWorld = Assert.IsType<WorldData>(PacketFrameCodec.Deserialize(worldBytes, fromServer: true));
        Assert.Equal((short)412, decodedWorld.DungeonX);
        Assert.Equal((short)237, decodedWorld.DungeonY);

        // ServerInfo (163): C2S empty request + S2C response with and without PortMapped
        var c2sReq = PacketFrameCodec.SerializeToArray(new ServerInfo(), fromServer: false);
        Assert.Equal(3, c2sReq.Length);
        Assert.Equal((byte)MessageID.ServerInfo, c2sReq[2]);
        _ = Assert.IsType<ServerInfo>(PacketFrameCodec.Deserialize(c2sReq, fromServer: false));

        var s2cMapped = new ServerInfo
        {
            ListenPort = 7777,
            WorldName = "CrossplayWorld",
            HostName = "terraria-host",
            MaxTilesX = 8400,
            HasCrimson = true,
            GameMode = 2,
            MaxNetPlayers = 16,
            ActivePlayers = 4,
            Reserved = string.Empty,
            PortMapped = true,
            ExternalAddress = "203.0.113.10",
            ExternalPort = 17777,
        };
        var s2cMappedBytes = PacketFrameCodec.SerializeToArray(s2cMapped, fromServer: true);
        var decodedMapped = Assert.IsType<ServerInfo>(PacketFrameCodec.Deserialize(s2cMappedBytes, fromServer: true));
        Assert.Equal(7777, decodedMapped.ListenPort);
        Assert.Equal("CrossplayWorld", decodedMapped.WorldName);
        Assert.Equal((ushort)8400, decodedMapped.MaxTilesX);
        Assert.True(decodedMapped.HasCrimson);
        Assert.Equal(2, decodedMapped.GameMode);
        Assert.True(decodedMapped.PortMapped);
        Assert.Equal("203.0.113.10", decodedMapped.ExternalAddress);
        Assert.Equal(17777, decodedMapped.ExternalPort);

        var s2cUnmappedBytes = PacketFrameCodec.SerializeToArray(new ServerInfo
        {
            ListenPort = 7778,
            WorldName = "LocalWorld",
            HostName = "localhost",
            MaxTilesX = 4200,
            MaxNetPlayers = 8,
            ActivePlayers = 1,
            Reserved = string.Empty,
            PortMapped = false,
        }, fromServer: true);
        var decodedUnmapped = Assert.IsType<ServerInfo>(PacketFrameCodec.Deserialize(s2cUnmappedBytes, fromServer: true));
        Assert.False(decodedUnmapped.PortMapped);
        Assert.Null(decodedUnmapped.ExternalAddress);

        // PlayerPlatformInfo (164): Steam (no PlatformName) and Nintendo (includes PlatformName)
        var steamBytes = PacketFrameCodec.SerializeToArray(new PlayerPlatformInfo
        {
            PlayerSlot = 3,
            PlatformId = TrProtocol.NetPackets.Mobile.Platform.Steam,
            PlayerId = "76561198000000001",
            CountsAsHost = false,
        }, fromServer: true);
        var decodedSteam = Assert.IsType<PlayerPlatformInfo>(PacketFrameCodec.Deserialize(steamBytes, fromServer: true));
        Assert.Equal((byte)3, decodedSteam.PlayerSlot);
        Assert.Equal(TrProtocol.NetPackets.Mobile.Platform.Steam, decodedSteam.PlatformId);
        Assert.Equal("76561198000000001", decodedSteam.PlayerId);
        Assert.Null(decodedSteam.PlatformName);
        Assert.False(decodedSteam.CountsAsHost);

        var nintendoBytes = PacketFrameCodec.SerializeToArray(new PlayerPlatformInfo
        {
            PlayerSlot = 4,
            PlatformId = TrProtocol.NetPackets.Mobile.Platform.Nintendo,
            PlayerId = "nso-123",
            PlatformName = "SwitchPlayer",
            CountsAsHost = true,
        }, fromServer: true);
        var decodedNintendo = Assert.IsType<PlayerPlatformInfo>(PacketFrameCodec.Deserialize(nintendoBytes, fromServer: true));
        Assert.Equal("SwitchPlayer", decodedNintendo.PlatformName);
        Assert.True(decodedNintendo.CountsAsHost);

        // CustomServerAck (165): 7-bit encoded int
        var ackBytes = PacketFrameCodec.SerializeToArray(new CustomServerAck { AckId = 300 }, fromServer: true);
        var decodedAck = Assert.IsType<CustomServerAck>(PacketFrameCodec.Deserialize(ackBytes, fromServer: true));
        Assert.Equal(300, decodedAck.AckId);
    }

    private static List<object> DecodeAll(ReadOnlyMemory<byte> memory)
    {
        var list = new List<object>();
        var seq = new ReadOnlySequence<byte>(memory);
        while (PacketFrameCodec.TryReadFrame(ref seq, out var frame))
            list.Add(PacketFrameCodec.Deserialize(frame, fromServer: true));
        return list;
    }
}
