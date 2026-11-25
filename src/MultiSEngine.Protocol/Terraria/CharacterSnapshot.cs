using TrProtocol.NetPackets;

namespace MultiSEngine.Protocol.Terraria;

public sealed class CharacterLoadoutSnapshot
{
    public SyncEquipment?[] Armor { get; } = new SyncEquipment?[CharacterSnapshot.LoadoutArmorSlotCount];
    public SyncEquipment?[] Dye { get; } = new SyncEquipment?[CharacterSnapshot.LoadoutDyeSlotCount];
}

public sealed class CharacterSnapshot
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

    public short Health { get; set; }
    public short HealthMax { get; set; }
    public short Mana { get; set; }
    public short ManaMax { get; set; }

    public SyncPlayer? Info { get; set; }
    public WorldData? WorldData { get; set; }
    public SyncLoadout? Loadout { get; set; }
    public PlayerBuffs? Buffs { get; set; }
    public PlayerTeam? Team { get; set; }
    public PlayerPvP? Pvp { get; set; }
    public PlayerZone? Zone { get; set; }
    public PlayerStealth? Stealth { get; set; }
    public UpdatePlayerLuckFactors? Luck { get; set; }

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

    public CharacterLoadoutSnapshot[] Loadouts { get; } =
    [
        new CharacterLoadoutSnapshot(),
        new CharacterLoadoutSnapshot(),
        new CharacterLoadoutSnapshot(),
    ];

    public byte CurrentLoadoutIndex => Loadout?.LoadOutSlot ?? 0;

    public bool TryStoreEquipment(in SyncEquipment packet)
    {
        if (!TryResolveSlot(packet.ItemSlot, out var segment, out var slotIndex))
            return false;

        switch (segment)
        {
            case EquipmentSegment.Inventory: Inventory[slotIndex] = packet; return true;
            case EquipmentSegment.Armor: Armor[slotIndex] = packet; return true;
            case EquipmentSegment.Dye: Dye[slotIndex] = packet; return true;
            case EquipmentSegment.MiscEquip: MiscEquip[slotIndex] = packet; return true;
            case EquipmentSegment.MiscDye: MiscDye[slotIndex] = packet; return true;
            case EquipmentSegment.PiggyBank: PiggyBank[slotIndex] = packet; return true;
            case EquipmentSegment.Safe: Safe[slotIndex] = packet; return true;
            case EquipmentSegment.Trash: Trash = packet; return true;
            case EquipmentSegment.Forge: Forge[slotIndex] = packet; return true;
            case EquipmentSegment.VoidVault: VoidVault[slotIndex] = packet; return true;
            case EquipmentSegment.Loadout1Armor: Loadouts[0].Armor[slotIndex] = packet; return true;
            case EquipmentSegment.Loadout1Dye: Loadouts[0].Dye[slotIndex] = packet; return true;
            case EquipmentSegment.Loadout2Armor: Loadouts[1].Armor[slotIndex] = packet; return true;
            case EquipmentSegment.Loadout2Dye: Loadouts[1].Dye[slotIndex] = packet; return true;
            case EquipmentSegment.Loadout3Armor: Loadouts[2].Armor[slotIndex] = packet; return true;
            case EquipmentSegment.Loadout3Dye: Loadouts[2].Dye[slotIndex] = packet; return true;
            default: return false;
        }
    }

    public IEnumerable<SyncEquipment> EnumerateSyncEquipment(byte playerSlot)
    {
        foreach (var p in EnumerateSegment(Inventory, playerSlot, InventorySlotStart)) yield return p;
        foreach (var p in EnumerateSegment(Armor, playerSlot, ArmorSlotStart)) yield return p;
        foreach (var p in EnumerateSegment(Dye, playerSlot, DyeSlotStart)) yield return p;
        foreach (var p in EnumerateSegment(MiscEquip, playerSlot, MiscEquipSlotStart)) yield return p;
        foreach (var p in EnumerateSegment(MiscDye, playerSlot, MiscDyeSlotStart)) yield return p;
        foreach (var p in EnumerateSegment(PiggyBank, playerSlot, Bank1SlotStart)) yield return p;
        foreach (var p in EnumerateSegment(Safe, playerSlot, Bank2SlotStart)) yield return p;
        yield return RewriteSlot(Trash, playerSlot, TrashSlot);
        foreach (var p in EnumerateSegment(Forge, playerSlot, Bank3SlotStart)) yield return p;
        foreach (var p in EnumerateSegment(VoidVault, playerSlot, Bank4SlotStart)) yield return p;
        foreach (var p in EnumerateSegment(Loadouts[0].Armor, playerSlot, Loadout1ArmorSlotStart)) yield return p;
        foreach (var p in EnumerateSegment(Loadouts[0].Dye, playerSlot, Loadout1DyeSlotStart)) yield return p;
        foreach (var p in EnumerateSegment(Loadouts[1].Armor, playerSlot, Loadout2ArmorSlotStart)) yield return p;
        foreach (var p in EnumerateSegment(Loadouts[1].Dye, playerSlot, Loadout2DyeSlotStart)) yield return p;
        foreach (var p in EnumerateSegment(Loadouts[2].Armor, playerSlot, Loadout3ArmorSlotStart)) yield return p;
        foreach (var p in EnumerateSegment(Loadouts[2].Dye, playerSlot, Loadout3DyeSlotStart)) yield return p;
    }

    public SyncLoadout CreateSyncLoadoutPacket(byte playerSlot)
    {
        var packet = Loadout ?? new SyncLoadout { LoadOutSlot = 0, AccessoryVisibility = 0 };
        packet.PlayerSlot = playerSlot;
        return packet;
    }

    public SyncPlayer CreateSyncPlayerPacket(byte playerSlot)
    {
        var info = Info ?? throw new InvalidOperationException("Character SyncPlayer info is not available.");
        info.PlayerSlot = playerSlot;
        return info;
    }

    internal static bool TryResolveSlot(int networkSlot, out EquipmentSegment segment, out int slotIndex)
    {
        if (TryRange(networkSlot, InventorySlotStart, InventorySlotCount, EquipmentSegment.Inventory, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, ArmorSlotStart, ArmorSlotCount, EquipmentSegment.Armor, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, DyeSlotStart, DyeSlotCount, EquipmentSegment.Dye, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, MiscEquipSlotStart, MiscEquipSlotCount, EquipmentSegment.MiscEquip, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, MiscDyeSlotStart, MiscDyeSlotCount, EquipmentSegment.MiscDye, out segment, out slotIndex)) return true;
        if (TryBank(networkSlot, Bank1SlotStart, EquipmentSegment.PiggyBank, out segment, out slotIndex)) return true;
        if (TryBank(networkSlot, Bank2SlotStart, EquipmentSegment.Safe, out segment, out slotIndex)) return true;
        if (networkSlot == TrashSlot)
        {
            segment = EquipmentSegment.Trash;
            slotIndex = 0;
            return true;
        }
        if (TryBank(networkSlot, Bank3SlotStart, EquipmentSegment.Forge, out segment, out slotIndex)) return true;
        if (TryBank(networkSlot, Bank4SlotStart, EquipmentSegment.VoidVault, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, Loadout1ArmorSlotStart, LoadoutArmorSlotCount, EquipmentSegment.Loadout1Armor, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, Loadout1DyeSlotStart, LoadoutDyeSlotCount, EquipmentSegment.Loadout1Dye, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, Loadout2ArmorSlotStart, LoadoutArmorSlotCount, EquipmentSegment.Loadout2Armor, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, Loadout2DyeSlotStart, LoadoutDyeSlotCount, EquipmentSegment.Loadout2Dye, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, Loadout3ArmorSlotStart, LoadoutArmorSlotCount, EquipmentSegment.Loadout3Armor, out segment, out slotIndex)) return true;
        if (TryRange(networkSlot, Loadout3DyeSlotStart, LoadoutDyeSlotCount, EquipmentSegment.Loadout3Dye, out segment, out slotIndex)) return true;

        segment = default;
        slotIndex = -1;
        return false;
    }

    private static IEnumerable<SyncEquipment> EnumerateSegment(SyncEquipment?[] array, byte playerSlot, int start)
    {
        for (var i = 0; i < array.Length; i++)
            yield return RewriteSlot(array[i], playerSlot, start + i);
    }

    private static SyncEquipment RewriteSlot(SyncEquipment? existing, byte playerSlot, int itemSlot)
    {
        var packet = existing ?? new SyncEquipment
        {
            ItemSlot = (short)itemSlot,
            Stack = 0,
            Prefix = 0,
            ItemType = 0,
            Details = default,
        };
        packet.PlayerSlot = playerSlot;
        packet.ItemSlot = (short)itemSlot;
        return packet;
    }

    private static bool TryRange(int networkSlot, int start, int length, EquipmentSegment segment, out EquipmentSegment resolved, out int index)
    {
        if (networkSlot >= start && networkSlot < start + length)
        {
            resolved = segment;
            index = networkSlot - start;
            return true;
        }
        resolved = default;
        index = -1;
        return false;
    }

    private static bool TryBank(int networkSlot, int start, EquipmentSegment segment, out EquipmentSegment resolved, out int index)
    {
        if (networkSlot >= start && networkSlot < start + BankSlotCount)
        {
            resolved = segment;
            index = networkSlot - start;
            return true;
        }
        resolved = default;
        index = -1;
        return false;
    }

    internal enum EquipmentSegment
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
