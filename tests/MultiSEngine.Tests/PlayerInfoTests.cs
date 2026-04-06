using MultiSEngine.Models;
using TrProtocol.NetPackets;

namespace MultiSEngine.Tests;

public sealed class PlayerInfoTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(58, 58)]
    [InlineData(99, 99)]
    [InlineData(138, 138)]
    [InlineData(299, 139)]
    [InlineData(338, 178)]
    [InlineData(499, 179)]
    [InlineData(500, 180)]
    [InlineData(539, 219)]
    [InlineData(700, 220)]
    [InlineData(739, 259)]
    [InlineData(900, 260)]
    [InlineData(919, 279)]
    [InlineData(920, 280)]
    [InlineData(929, 289)]
    [InlineData(930, 290)]
    [InlineData(949, 309)]
    [InlineData(950, 310)]
    [InlineData(959, 319)]
    [InlineData(960, 320)]
    [InlineData(979, 339)]
    [InlineData(980, 340)]
    [InlineData(989, 349)]
    public void UpdateData_MapsRepresentativeNetworkEquipmentSlots_ToCompactInventoryIndex(int networkSlot, int compactSlot)
    {
        var player = new PlayerInfo();
        var packet = new SyncEquipment
        {
            PlayerSlot = 1,
            ItemSlot = (short)networkSlot,
            Stack = 1,
            Prefix = 0,
            ItemType = 757,
        };

        player.UpdateData(packet, fromClient: true);

        Assert.True(player.OriginCharacter.Inventory[compactSlot].HasValue);
        Assert.Equal(networkSlot, player.OriginCharacter.Inventory[compactSlot]!.Value.ItemSlot);
        Assert.Equal(1, player.OriginCharacter.Inventory.Count(item => item.HasValue));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(139)]
    [InlineData(298)]
    [InlineData(339)]
    [InlineData(498)]
    [InlineData(540)]
    [InlineData(699)]
    [InlineData(740)]
    [InlineData(899)]
    [InlineData(990)]
    public void UpdateData_IgnoresReservedNetworkEquipmentSlot(int networkSlot)
    {
        var player = new PlayerInfo();
        var packet = new SyncEquipment
        {
            PlayerSlot = 1,
            ItemSlot = (short)networkSlot,
            Stack = 1,
            Prefix = 0,
            ItemType = 757,
        };

        player.UpdateData(packet, fromClient: true);

        Assert.DoesNotContain(player.OriginCharacter.Inventory, item => item.HasValue);
    }
}
