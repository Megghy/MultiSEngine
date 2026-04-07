using MultiSEngine.Models;
using TrProtocol.NetPackets;

namespace MultiSEngine.Tests;

public sealed class PlayerInfoTests
{
    [Fact]
    public void UpdateData_StoresStructuredEquipmentSlots_InMatchingSegments()
    {
        var player = new PlayerInfo();

        player.UpdateData(CreateEquipmentPacket(58, 1001), fromClient: true);
        player.UpdateData(CreateEquipmentPacket(89, 1002), fromClient: true);
        player.UpdateData(CreateEquipmentPacket(94, 1003), fromClient: true);
        player.UpdateData(CreateEquipmentPacket(99, 1004), fromClient: true);
        player.UpdateData(CreateEquipmentPacket(299, 1005), fromClient: true);
        player.UpdateData(CreateEquipmentPacket(499, 1006), fromClient: true);
        player.UpdateData(CreateEquipmentPacket(500, 1007), fromClient: true);
        player.UpdateData(CreateEquipmentPacket(700, 1008), fromClient: true);
        player.UpdateData(CreateEquipmentPacket(930, 1009), fromClient: true);
        player.UpdateData(CreateEquipmentPacket(980, 1010), fromClient: true);

        Assert.Equal(58, player.OriginCharacter.Inventory[58]!.Value.ItemSlot);
        Assert.Equal(1001, player.OriginCharacter.Inventory[58]!.Value.ItemType);
        Assert.Equal(89, player.OriginCharacter.MiscEquip[0]!.Value.ItemSlot);
        Assert.Equal(94, player.OriginCharacter.MiscDye[0]!.Value.ItemSlot);
        Assert.Equal(99, player.OriginCharacter.PiggyBank[0]!.Value.ItemSlot);
        Assert.Equal(299, player.OriginCharacter.Safe[0]!.Value.ItemSlot);
        Assert.Equal(499, player.OriginCharacter.Trash!.Value.ItemSlot);
        Assert.Equal(500, player.OriginCharacter.Forge[0]!.Value.ItemSlot);
        Assert.Equal(700, player.OriginCharacter.VoidVault[0]!.Value.ItemSlot);
        Assert.Equal(930, player.OriginCharacter.Loadouts[1].Armor[0]!.Value.ItemSlot);
        Assert.Equal(980, player.OriginCharacter.Loadouts[2].Dye[0]!.Value.ItemSlot);
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
    public void UpdateData_IgnoresReservedNetworkPaddingSlots(int networkSlot)
    {
        var player = new PlayerInfo();

        player.UpdateData(CreateEquipmentPacket(networkSlot, 2001), fromClient: true);

        Assert.Equal(0, CountStoredEquipment(player.OriginCharacter));
    }

    [Fact]
    public void UpdateData_TracksCurrentLoadoutPacket()
    {
        var player = new PlayerInfo();
        var packet = new SyncLoadout
        {
            PlayerSlot = 3,
            LoadOutSlot = 2,
            AccessoryVisibility = 1234,
        };

        player.UpdateData(packet, fromClient: true);

        Assert.NotNull(player.OriginCharacter.Loadout);
        Assert.Equal(2, player.OriginCharacter.CurrentLoadoutIndex);
        Assert.Equal((ushort)1234, player.OriginCharacter.Loadout!.Value.AccessoryVisibility);
    }

    [Fact]
    public void EnumerateSyncEquipment_ReturnsAllStructuredSlots_InLatestOrder()
    {
        var data = new CharacterData();

        var packets = data.EnumerateSyncEquipment(playerSlot: 7).ToArray();

        Assert.Equal(350, packets.Length);
        Assert.Equal(0, packets[0].ItemSlot);
        Assert.Equal(58, packets[58].ItemSlot);
        Assert.Equal(59, packets[59].ItemSlot);
        Assert.Equal(94, packets[94].ItemSlot);
        Assert.Equal(99, packets[99].ItemSlot);
        Assert.Equal(299, packets[139].ItemSlot);
        Assert.Equal(499, packets[179].ItemSlot);
        Assert.Equal(500, packets[180].ItemSlot);
        Assert.Equal(700, packets[220].ItemSlot);
        Assert.Equal(900, packets[260].ItemSlot);
        Assert.Equal(980, packets[340].ItemSlot);
        Assert.Equal(989, packets[^1].ItemSlot);
        Assert.All(packets, packet => Assert.Equal(7, packet.PlayerSlot));
    }

    [Fact]
    public void CreateSyncLoadoutPacket_RewritesPlayerSlot_WithoutDroppingPayload()
    {
        var data = new CharacterData
        {
            Loadout = new SyncLoadout
            {
                PlayerSlot = 1,
                LoadOutSlot = 2,
                AccessoryVisibility = 4321,
            }
        };

        var packet = data.CreateSyncLoadoutPacket(playerSlot: 9);

        Assert.Equal(9, packet.PlayerSlot);
        Assert.Equal(2, packet.LoadOutSlot);
        Assert.Equal((ushort)4321, packet.AccessoryVisibility);
    }

    private static SyncEquipment CreateEquipmentPacket(int slot, short itemType)
    {
        return new SyncEquipment
        {
            PlayerSlot = 1,
            ItemSlot = (short)slot,
            Stack = 1,
            Prefix = 0,
            ItemType = itemType,
        };
    }

    private static int CountStoredEquipment(CharacterData data)
    {
        return CountStoredEquipment(data.Inventory)
            + CountStoredEquipment(data.Armor)
            + CountStoredEquipment(data.Dye)
            + CountStoredEquipment(data.MiscEquip)
            + CountStoredEquipment(data.MiscDye)
            + CountStoredEquipment(data.PiggyBank)
            + CountStoredEquipment(data.Safe)
            + CountStoredEquipment(data.Forge)
            + CountStoredEquipment(data.VoidVault)
            + data.Loadouts.Sum(loadout => CountStoredEquipment(loadout.Armor) + CountStoredEquipment(loadout.Dye))
            + (data.Trash.HasValue ? 1 : 0);
    }

    private static int CountStoredEquipment(SyncEquipment?[] packets) => packets.Count(packet => packet.HasValue);
}
