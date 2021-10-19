using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Modules.DataStruct
{
    public class MSEPlayer
    {
        public class PlayerData
        {
            public int Health;
            public int Mana;
            public int HealthMax;
            public int ManaMax;
            public SyncPlayer Info { get; set; }
            public WorldData WorldData { get; set; }
            public SyncEquipment[] Inventory { get; set; } = new SyncEquipment[260];
        }
        public bool SSC { get; set; } = false;
        public int VersionNum { get; set; }
        public byte Index { get; set; } = 0;
        public string Name => (ServerData.Info ?? OriginData.Info)?.Name;
        public string UUID { get; set; } = "";
        public int SpawnX { get; set; }
        public int SpawnY { get; set; }
        public int WorldSpawnX => (ServerData.WorldData ?? OriginData.WorldData).SpawnX;
        public int WorldSpawnY => (ServerData.WorldData ?? OriginData.WorldData).SpawnY;
        public float X { get; set; }
        public float Y { get; set; }
        public int TileX => (int)(X / 16);
        public int TileY => (int)(Y / 16);

        public void UpdateData(Packet packet)
        {
            switch (packet)
            {
                case SyncEquipment item:
                    (SSC ? ServerData : OriginData).Inventory[item.ItemSlot] = item;
                    break;
                case PlayerHealth health:
                    (SSC ? ServerData : OriginData).Health = health.StatLife;
                    (SSC ? ServerData : OriginData).HealthMax = health.StatLifeMax;
                    break;
                case PlayerMana mana:
                    (SSC ? ServerData : OriginData).Mana = mana.StatMana;
                    (SSC ? ServerData : OriginData).ManaMax = mana.StatManaMax;
                    break;
                case SyncPlayer playerInfo:
                    (SSC ? ServerData : OriginData).Info = playerInfo;
                    break;
                case WorldData world:
                    ServerData.WorldData = world;
                    SSC = world.EventInfo1[6];
                    break;
                case PlayerControls control:
                    X = control.Position.X;
                    Y = control.Position.Y;
                    break;
            }
        }

        #region 用于临时储存玩家的原始信息
        public PlayerData OriginData { get; set; } = new();
        public PlayerData ServerData { get; set; } = new();
        #endregion
    }
}
