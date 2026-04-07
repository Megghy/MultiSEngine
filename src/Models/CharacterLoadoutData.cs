namespace MultiSEngine.Models;

public sealed class CharacterLoadoutData
{
    public SyncEquipment?[] Armor { get; } = new SyncEquipment?[CharacterData.LoadoutArmorSlotCount];

    public SyncEquipment?[] Dye { get; } = new SyncEquipment?[CharacterData.LoadoutDyeSlotCount];
}
