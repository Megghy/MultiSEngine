using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.DataStruct
{
    public class PlayerInfo
    {
        public class PlayerData
        {
            public short Health;
            public short Mana;
            public short HealthMax;
            public short ManaMax;
            public SyncPlayer Info { get; set; }
            public WorldData WorldData { get; set; }
            public SyncEquipment[] Inventory { get; set; } = new SyncEquipment[350];
        }
        public bool SSC => ServerData?.WorldData?.EventInfo1[6] ?? false;
        public int VersionNum { get; set; } = -1;
        public byte Index { get; set; } = 0;
        public string Name => (ServerData?.Info ?? OriginData.Info)?.Name;
        public string UUID { get; set; } = "";
        public int SpawnX { get; set; } = -1;
        public int SpawnY { get; set; } = -1;
        public short WorldSpawnX => (ServerData?.WorldData ?? OriginData.WorldData).SpawnX;
        public short WorldSpawnY => (ServerData?.WorldData ?? OriginData.WorldData).SpawnY;
        public float X { get; set; } = -1;
        public float Y { get; set; } = -1;
        public int TileX => (int)(X / 16);
        public int TileY => (int)(Y / 16);

        public void UpdateData(Packet packet, bool fromClient)
        {
            if (packet is null)
                return;
            var data = SSC ? ServerData : OriginData;
            switch (packet)
            {
                case SyncEquipment item:
                    if (!SSC)
                        OriginData.Inventory[item.ItemSlot] = item;
                    else
                        ServerData.Inventory[item.ItemSlot] = item;
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
                    ServerData.WorldData = world;
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
