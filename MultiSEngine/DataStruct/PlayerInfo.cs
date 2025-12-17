namespace MultiSEngine.DataStruct
{
    public class PlayerInfo(bool isOriginCharacter = false)
    {
        public bool IsOriginCharacter => isOriginCharacter;
        public bool SSC => ServerCharacter?.WorldData?.EventInfo1[6] ?? false;
        public int VersionNum { get; set; } = -1;
        public byte Index { get; set; } = 0;
        public string Name => (ServerCharacter?.Info ?? OriginCharacter.Info)?.Name;
        public string UUID { get; set; } = "";
        public int SpawnX { get; set; } = -1;
        public int SpawnY { get; set; } = -1;
        public short WorldSpawnX => (ServerCharacter?.WorldData ?? OriginCharacter.WorldData).SpawnX;
        public short WorldSpawnY => (ServerCharacter?.WorldData ?? OriginCharacter.WorldData).SpawnY;
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

        public void UpdateData(Packet packet, bool fromClient)
        {
            if (packet is null)
                return;
            var data = SSC ? ServerCharacter : OriginCharacter;
            switch (packet)
            {
                case SyncEquipment item:
                    if (!SSC)
                        OriginCharacter.Inventory[item.ItemSlot] = item;
                    else
                        ServerCharacter.Inventory[item.ItemSlot] = item;
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

        #region 用于临时储存玩家的原始信息
        public CharacterData OriginCharacter { get; set; } = new();
        public CharacterData ServerCharacter { get; set; } = new();
        #endregion
    }
}
