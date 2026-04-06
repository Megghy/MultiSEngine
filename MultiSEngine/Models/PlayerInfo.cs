namespace MultiSEngine.Models
{
    public class PlayerInfo(bool isOriginCharacter = false)
    {
        private const int CompactInventorySlotCount = 350;
        private const int Bank1NetworkSlotStart = 99;
        private const int Bank2NetworkSlotStart = 299;
        private const int BankSlotsPerNetworkBlock = 200;
        private const int BankContentSlots = 40;
        private const int TrashNetworkSlot = 499;
        private const int Bank3NetworkSlotStart = 500;
        private const int Bank4NetworkSlotStart = 700;
        private const int Loadout1ArmorNetworkSlotStart = 900;
        private const int Loadout1DyeNetworkSlotStart = 920;
        private const int LoadoutArmorSlots = 20;
        private const int LoadoutDyeSlots = 10;
        private const int PiggyInternalSlotStart = 99;
        private const int SafeInternalSlotStart = 139;
        private const int TrashInternalSlot = 179;
        private const int ForgeInternalSlotStart = 180;
        private const int VoidInternalSlotStart = 220;
        private const int Loadout1ArmorInternalSlotStart = 260;
        private const int Loadout1DyeInternalSlotStart = 280;
        private const int Loadout2ArmorNetworkSlotStart = 930;
        private const int Loadout2DyeNetworkSlotStart = 950;
        private const int Loadout3ArmorNetworkSlotStart = 960;
        private const int Loadout3DyeNetworkSlotStart = 980;
        private const int Loadout2ArmorInternalSlotStart = 290;
        private const int Loadout2DyeInternalSlotStart = 310;
        private const int Loadout3ArmorInternalSlotStart = 320;
        private const int Loadout3DyeInternalSlotStart = 340;

        public bool IsOriginCharacter => isOriginCharacter;
        public bool SSC => ServerCharacter?.WorldData?.EventInfo1[6] ?? false;
        public int VersionNum { get; set; } = -1;
        public byte Index { get; set; } = 0;
        public string Name => (ServerCharacter?.Info ?? OriginCharacter.Info)?.Name ?? string.Empty;
        public string UUID { get; set; } = "";
        public int SpawnX { get; set; } = -1;
        public int SpawnY { get; set; } = -1;
        public short WorldSpawnX => (ServerCharacter?.WorldData ?? OriginCharacter.WorldData)?.SpawnX ?? 0;
        public short WorldSpawnY => (ServerCharacter?.WorldData ?? OriginCharacter.WorldData)?.SpawnY ?? 0;
        public float X { get; set; } = -1;
        public float Y { get; set; } = -1;
        public int TileX => (int)(X / 16);
        public int TileY => (int)(Y / 16);


        #region 首次登录时提供的信息
        public int Timer = 0;

        public short DeathsPVE = 0;

        public short DeathsPVP = 0;

        public PlayerSpawnContext Context = PlayerSpawnContext.SpawningIntoWorld;
        #endregion

        public void UpdateData(INetPacket packet, bool fromClient)
        {
            if (packet is null)
                return;
            var data = SSC ? ServerCharacter : OriginCharacter;
            switch (packet)
            {
                case SyncEquipment item:
                    if (!TryGetCompactInventorySlot(item.ItemSlot, out var compactSlot))
                        break;
                    if (!SSC)
                        OriginCharacter.Inventory[compactSlot] = item;
                    else
                        ServerCharacter.Inventory[compactSlot] = item;
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
                    world.WorldName = string.IsNullOrEmpty(Config.Instance.ServerName) ? world.WorldName : Config.Instance.ServerName; //设置了服务器名称的话则替换
                    ServerCharacter.WorldData = world;
                    break;
                case PlayerControls control:
                    X = control.Position.X;
                    Y = control.Position.Y;
                    break;
            }
        }

        internal static bool TryGetCompactInventorySlot(int networkSlot, out int compactSlot)
        {
            if (networkSlot < 0)
            {
                compactSlot = -1;
                return false;
            }

            if (networkSlot < Bank1NetworkSlotStart)
            {
                compactSlot = networkSlot;
                return compactSlot < CompactInventorySlotCount;
            }

            if (networkSlot < Bank1NetworkSlotStart + BankContentSlots)
            {
                compactSlot = PiggyInternalSlotStart + (networkSlot - Bank1NetworkSlotStart);
                return true;
            }

            if (networkSlot < Bank1NetworkSlotStart + BankSlotsPerNetworkBlock)
            {
                compactSlot = -1;
                return false;
            }

            if (networkSlot < Bank2NetworkSlotStart + BankContentSlots)
            {
                compactSlot = SafeInternalSlotStart + (networkSlot - Bank2NetworkSlotStart);
                return true;
            }

            if (networkSlot < TrashNetworkSlot)
            {
                compactSlot = -1;
                return false;
            }

            if (networkSlot == TrashNetworkSlot)
            {
                compactSlot = TrashInternalSlot;
                return true;
            }

            if (networkSlot < Bank3NetworkSlotStart + BankContentSlots)
            {
                compactSlot = ForgeInternalSlotStart + (networkSlot - Bank3NetworkSlotStart);
                return true;
            }

            if (networkSlot < Bank4NetworkSlotStart)
            {
                compactSlot = -1;
                return false;
            }

            if (networkSlot < Bank4NetworkSlotStart + BankContentSlots)
            {
                compactSlot = VoidInternalSlotStart + (networkSlot - Bank4NetworkSlotStart);
                return true;
            }

            if (networkSlot < Loadout1ArmorNetworkSlotStart)
            {
                compactSlot = -1;
                return false;
            }

            if (networkSlot < Loadout1ArmorNetworkSlotStart + LoadoutArmorSlots)
            {
                compactSlot = Loadout1ArmorInternalSlotStart + (networkSlot - Loadout1ArmorNetworkSlotStart);
                return true;
            }

            if (networkSlot < Loadout1DyeNetworkSlotStart + LoadoutDyeSlots)
            {
                compactSlot = Loadout1DyeInternalSlotStart + (networkSlot - Loadout1DyeNetworkSlotStart);
                return true;
            }

            if (networkSlot < Loadout2ArmorNetworkSlotStart + LoadoutArmorSlots)
            {
                compactSlot = Loadout2ArmorInternalSlotStart + (networkSlot - Loadout2ArmorNetworkSlotStart);
                return true;
            }

            if (networkSlot < Loadout2DyeNetworkSlotStart + LoadoutDyeSlots)
            {
                compactSlot = Loadout2DyeInternalSlotStart + (networkSlot - Loadout2DyeNetworkSlotStart);
                return true;
            }

            if (networkSlot < Loadout3ArmorNetworkSlotStart + LoadoutArmorSlots)
            {
                compactSlot = Loadout3ArmorInternalSlotStart + (networkSlot - Loadout3ArmorNetworkSlotStart);
                return true;
            }

            if (networkSlot < Loadout3DyeNetworkSlotStart + LoadoutDyeSlots)
            {
                compactSlot = Loadout3DyeInternalSlotStart + (networkSlot - Loadout3DyeNetworkSlotStart);
                return true;
            }

            compactSlot = -1;
            return false;
        }

        #region 用于临时储存玩家的原始信息
        public CharacterData OriginCharacter { get; set; } = new();
        public CharacterData ServerCharacter { get; set; } = new();
        #endregion
    }
}


