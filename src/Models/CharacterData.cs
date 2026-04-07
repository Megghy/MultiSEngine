namespace MultiSEngine.Models;

public sealed class CharacterData
{
    public const int InventorySlotCount = 59;
    public const int ArmorSlotCount = 20;
    public const int DyeSlotCount = 10;
    public const int MiscEquipSlotCount = 5;
    public const int MiscDyeSlotCount = 5;
    public const int BankSlotCount = 40;
    public const int LoadoutArmorSlotCount = 20;
    public const int LoadoutDyeSlotCount = 10;

    public const int InventorySlotStart = 0;
    public const int ArmorSlotStart = 59;
    public const int DyeSlotStart = 79;
    public const int MiscEquipSlotStart = 89;
    public const int MiscDyeSlotStart = 94;
    public const int Bank1SlotStart = 99;
    public const int BankStorageNetworkBlockSize = 200;
    public const int Bank2SlotStart = 299;
    public const int TrashSlot = 499;
    public const int Bank3SlotStart = 500;
    public const int Bank4SlotStart = 700;
    public const int Loadout1ArmorSlotStart = 900;
    public const int Loadout1DyeSlotStart = 920;
    public const int Loadout2ArmorSlotStart = 930;
    public const int Loadout2DyeSlotStart = 950;
    public const int Loadout3ArmorSlotStart = 960;
    public const int Loadout3DyeSlotStart = 980;

    public short Health;

    public short Mana;

    public short HealthMax;

    public short ManaMax;

    public SyncPlayer? Info { get; set; }

    public WorldData? WorldData { get; set; }

    public SyncLoadout? Loadout { get; set; }

    public SyncEquipment?[] Inventory { get; } = new SyncEquipment?[InventorySlotCount];

    public SyncEquipment?[] Armor { get; } = new SyncEquipment?[ArmorSlotCount];

    public SyncEquipment?[] Dye { get; } = new SyncEquipment?[DyeSlotCount];

    public SyncEquipment?[] MiscEquip { get; } = new SyncEquipment?[MiscEquipSlotCount];

    public SyncEquipment?[] MiscDye { get; } = new SyncEquipment?[MiscDyeSlotCount];

    public SyncEquipment?[] PiggyBank { get; } = new SyncEquipment?[BankSlotCount];

    public SyncEquipment?[] Safe { get; } = new SyncEquipment?[BankSlotCount];

    public SyncEquipment?[] Forge { get; } = new SyncEquipment?[BankSlotCount];

    public SyncEquipment?[] VoidVault { get; } = new SyncEquipment?[BankSlotCount];

    public SyncEquipment? Trash { get; set; }

    public CharacterLoadoutData[] Loadouts { get; } =
    [
        new CharacterLoadoutData(),
        new CharacterLoadoutData(),
        new CharacterLoadoutData(),
    ];

    public byte CurrentLoadoutIndex => Loadout?.LoadOutSlot ?? 0;

    public bool TryStoreEquipment(in SyncEquipment packet)
    {
        if (!TryResolveSlot(packet.ItemSlot, out var segment, out var slotIndex))
            return false;

        switch (segment)
        {
            case CharacterEquipmentSegment.Inventory:
                Inventory[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Armor:
                Armor[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Dye:
                Dye[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.MiscEquip:
                MiscEquip[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.MiscDye:
                MiscDye[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.PiggyBank:
                PiggyBank[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Safe:
                Safe[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Trash:
                Trash = packet;
                return true;
            case CharacterEquipmentSegment.Forge:
                Forge[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.VoidVault:
                VoidVault[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Loadout1Armor:
                Loadouts[0].Armor[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Loadout1Dye:
                Loadouts[0].Dye[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Loadout2Armor:
                Loadouts[1].Armor[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Loadout2Dye:
                Loadouts[1].Dye[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Loadout3Armor:
                Loadouts[2].Armor[slotIndex] = packet;
                return true;
            case CharacterEquipmentSegment.Loadout3Dye:
                Loadouts[2].Dye[slotIndex] = packet;
                return true;
            default:
                return false;
        }
    }

    public IEnumerable<SyncEquipment> EnumerateSyncEquipment(byte playerSlot)
    {
        foreach (var packet in EnumeratePackets(Inventory, playerSlot, InventorySlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(Armor, playerSlot, ArmorSlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(Dye, playerSlot, DyeSlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(MiscEquip, playerSlot, MiscEquipSlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(MiscDye, playerSlot, MiscDyeSlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(PiggyBank, playerSlot, Bank1SlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(Safe, playerSlot, Bank2SlotStart))
            yield return packet;
        yield return RewritePacket(Trash, playerSlot, TrashSlot);
        foreach (var packet in EnumeratePackets(Forge, playerSlot, Bank3SlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(VoidVault, playerSlot, Bank4SlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(Loadouts[0].Armor, playerSlot, Loadout1ArmorSlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(Loadouts[0].Dye, playerSlot, Loadout1DyeSlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(Loadouts[1].Armor, playerSlot, Loadout2ArmorSlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(Loadouts[1].Dye, playerSlot, Loadout2DyeSlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(Loadouts[2].Armor, playerSlot, Loadout3ArmorSlotStart))
            yield return packet;
        foreach (var packet in EnumeratePackets(Loadouts[2].Dye, playerSlot, Loadout3DyeSlotStart))
            yield return packet;
    }

    public SyncLoadout CreateSyncLoadoutPacket(byte playerSlot)
    {
        var packet = Loadout ?? new SyncLoadout
        {
            LoadOutSlot = 0,
            AccessoryVisibility = 0,
        };
        packet.PlayerSlot = playerSlot;
        return packet;
    }

    internal static bool TryResolveSlot(int networkSlot, out CharacterEquipmentSegment segment, out int slotIndex)
    {
        if (TryResolveSimpleRange(networkSlot, InventorySlotStart, InventorySlotCount, CharacterEquipmentSegment.Inventory, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, ArmorSlotStart, ArmorSlotCount, CharacterEquipmentSegment.Armor, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, DyeSlotStart, DyeSlotCount, CharacterEquipmentSegment.Dye, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, MiscEquipSlotStart, MiscEquipSlotCount, CharacterEquipmentSegment.MiscEquip, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, MiscDyeSlotStart, MiscDyeSlotCount, CharacterEquipmentSegment.MiscDye, out segment, out slotIndex))
            return true;
        if (TryResolveBankRange(networkSlot, Bank1SlotStart, CharacterEquipmentSegment.PiggyBank, out segment, out slotIndex))
            return true;
        if (TryResolveBankRange(networkSlot, Bank2SlotStart, CharacterEquipmentSegment.Safe, out segment, out slotIndex))
            return true;
        if (networkSlot == TrashSlot)
        {
            segment = CharacterEquipmentSegment.Trash;
            slotIndex = 0;
            return true;
        }
        if (TryResolveBankRange(networkSlot, Bank3SlotStart, CharacterEquipmentSegment.Forge, out segment, out slotIndex))
            return true;
        if (TryResolveBankRange(networkSlot, Bank4SlotStart, CharacterEquipmentSegment.VoidVault, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, Loadout1ArmorSlotStart, LoadoutArmorSlotCount, CharacterEquipmentSegment.Loadout1Armor, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, Loadout1DyeSlotStart, LoadoutDyeSlotCount, CharacterEquipmentSegment.Loadout1Dye, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, Loadout2ArmorSlotStart, LoadoutArmorSlotCount, CharacterEquipmentSegment.Loadout2Armor, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, Loadout2DyeSlotStart, LoadoutDyeSlotCount, CharacterEquipmentSegment.Loadout2Dye, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, Loadout3ArmorSlotStart, LoadoutArmorSlotCount, CharacterEquipmentSegment.Loadout3Armor, out segment, out slotIndex))
            return true;
        if (TryResolveSimpleRange(networkSlot, Loadout3DyeSlotStart, LoadoutDyeSlotCount, CharacterEquipmentSegment.Loadout3Dye, out segment, out slotIndex))
            return true;

        segment = default;
        slotIndex = -1;
        return false;
    }

    private static IEnumerable<SyncEquipment> EnumeratePackets(SyncEquipment?[] packets, byte playerSlot, int slotStart)
    {
        for (var i = 0; i < packets.Length; i++)
            yield return RewritePacket(packets[i], playerSlot, slotStart + i);
    }

    private static SyncEquipment RewritePacket(SyncEquipment? packet, byte playerSlot, int itemSlot)
    {
        var value = packet ?? CreateAirPacket(itemSlot);
        value.PlayerSlot = playerSlot;
        value.ItemSlot = (short)itemSlot;
        return value;
    }

    private static SyncEquipment CreateAirPacket(int itemSlot)
    {
        return new SyncEquipment
        {
            ItemSlot = (short)itemSlot,
            Stack = 0,
            Prefix = 0,
            ItemType = 0,
            Details = default,
        };
    }

    private static bool TryResolveSimpleRange(int networkSlot, int start, int length, CharacterEquipmentSegment segment, out CharacterEquipmentSegment resolvedSegment, out int slotIndex)
    {
        if (networkSlot >= start && networkSlot < start + length)
        {
            resolvedSegment = segment;
            slotIndex = networkSlot - start;
            return true;
        }

        resolvedSegment = default;
        slotIndex = -1;
        return false;
    }

    private static bool TryResolveBankRange(int networkSlot, int start, CharacterEquipmentSegment segment, out CharacterEquipmentSegment resolvedSegment, out int slotIndex)
    {
        if (networkSlot < start || networkSlot >= start + BankStorageNetworkBlockSize)
        {
            resolvedSegment = default;
            slotIndex = -1;
            return false;
        }

        if (networkSlot >= start + BankSlotCount)
        {
            resolvedSegment = default;
            slotIndex = -1;
            return false;
        }

        resolvedSegment = segment;
        slotIndex = networkSlot - start;
        return true;
    }

    internal enum CharacterEquipmentSegment
    {
        Inventory,
        Armor,
        Dye,
        MiscEquip,
        MiscDye,
        PiggyBank,
        Safe,
        Trash,
        Forge,
        VoidVault,
        Loadout1Armor,
        Loadout1Dye,
        Loadout2Armor,
        Loadout2Dye,
        Loadout3Armor,
        Loadout3Dye,
    }
}
