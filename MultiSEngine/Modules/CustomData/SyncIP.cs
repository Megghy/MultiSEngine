using System.IO;

namespace MultiSEngine.Modules.CustomData
{
    public class SyncIP : CustomData
    {
        public override string Name => "MultiSEngine.SyncIP";
        public string PlayerName { get; set; }
        public string IP { get; set; }

        public override void InternalRead(BinaryReader reader)
        {
            PlayerName = reader.ReadString();
            IP = reader.ReadString();
        }

        public override void InternalWrite(BinaryWriter writer)
        {
            writer.Write(PlayerName);
            writer.Write(IP);
        }
    }
}
