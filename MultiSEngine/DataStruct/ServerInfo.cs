﻿namespace MultiSEngine.DataStruct
{
    public class ServerInfo
    {
        public bool Visible { get; set; } = true;
        public string Name { get; set; }
        public string ShortName { get; set; } = "";
        public string IP { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 7777;
        public short SpawnX { get; set; } = -1;
        public short SpawnY { get; set; } = -1;
        public int VersionNum { get; set; } = -1;
        public override bool Equals(object obj)
        {
            return (obj as ServerInfo)?.Name == Name;
        }
        public static bool operator ==(ServerInfo serverInfo1, ServerInfo serverInfo2)
        {
            return serverInfo1?.Name == serverInfo2?.Name;
        }
        public static bool operator !=(ServerInfo serverInfo1, ServerInfo serverInfo2)
        {
            return serverInfo1?.Name != serverInfo2?.Name;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}
