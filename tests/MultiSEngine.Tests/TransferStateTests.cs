using MultiSEngine.Application.Transfers;
using MultiSEngine.Models;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.Tests;

public sealed class TransferStateTests
{
    [Fact]
    public async Task PreConnectSession_TracksBufferedPacketTypes_AndCompletion()
    {
        var session = new PreConnectSession(new ServerInfo { Name = "alpha" });
        var packet = new byte[] { 5, 0, (byte)MessageID.StartPlaying, 1, 2 };

        session.BufferPacket(packet);
        session.MarkFailed("boom");

        Assert.True(session.HasBufferedPacket(MessageID.StartPlaying));
        Assert.False(session.IsConnecting);
        Assert.Equal("boom", session.FailureReason);
        Assert.False(await session.CompletionTask);
    }

    [Fact]
    public void PreConnectSession_UpdateWorldData_SetsWorldAndSpawn()
    {
        var session = new PreConnectSession(new ServerInfo { Name = "alpha" });
        var world = new WorldData
        {
            WorldName = "demo",
            SpawnX = 100,
            SpawnY = 200,
        };

        session.UpdateWorldData(world, 123, 456);

        Assert.Equal("demo", session.World?.WorldName);
        Assert.Equal((short)123, session.SpawnX);
        Assert.Equal((short)456, session.SpawnY);
    }

    [Fact]
    public void PlayerStateStore_CreateRemoteSyncPlayer_OverridesPlayerSlot()
    {
        var player = CreatePlayer("Alice");

        var remote = PlayerStateStore.CreateRemoteSyncPlayer(player, 7);

        Assert.Equal("Alice", remote.Name);
        Assert.Equal((byte)7, remote.PlayerSlot);
        Assert.Equal("Alice", player.OriginCharacter.Info?.Name);
        Assert.Equal((byte)3, player.OriginCharacter.Info?.PlayerSlot);
    }

    [Fact]
    public void PlayerStateStore_ApplyTargetSession_AndResetTargetCharacter_Work()
    {
        var player = CreatePlayer("Alice");
        var world = new WorldData
        {
            WorldName = "target",
            SpawnX = 90,
            SpawnY = 120,
        };
        var session = new PreConnectSession(new ServerInfo { Name = "target" });
        session.UpdateWorldData(world, 321, 654);

        PlayerStateStore.ApplyTargetSession(player, session);

        Assert.Equal("target", player.ServerCharacter.WorldData?.WorldName);
        Assert.Equal(321, player.SpawnX);
        Assert.Equal(654, player.SpawnY);

        PlayerStateStore.ResetTargetCharacter(player);

        Assert.Null(player.ServerCharacter.WorldData);
        Assert.Equal(-1, player.SpawnX);
        Assert.Equal(-1, player.SpawnY);
    }

    private static PlayerInfo CreatePlayer(string name)
    {
        return new PlayerInfo
        {
            OriginCharacter =
            {
                Info = new SyncPlayer
                {
                    Name = name,
                    PlayerSlot = 3,
                }
            }
        };
    }
}
