namespace MultiSEngine.Modules.DataStruct
{
    public class ServerInfo
    {
        public bool Visible { get; set; } = true;
        public string Name { get; set; }
        public string IP { get; set; }
        public int Port { get; set; }
        public int SpawnX { get; set; } = -1;
        public int SpawnY { get; set; } = -1;
        public bool RememberHostInventory { get; set; } = true;
        public int VersionNum { get; set; } = 238;
    }
}
