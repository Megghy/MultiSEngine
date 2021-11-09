namespace MultiSEngine.Modules.DataStruct
{
    public class ServerInfo
    {
        public bool Visible { get; set; } = true;
        public string Name { get; set; }
        public string IP { get; set; }
        public int Port { get; set; }
        public short SpawnX { get; set; } = -1;
        public short SpawnY { get; set; } = -1;
        public int VersionNum { get; set; } = -1;
    }
}
