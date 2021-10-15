using System.IO;

namespace MultiSEngine.Modules.CustomDataPacket
{
    public class SyncIPPacket : DataStruct.CustomPacketBase
    {
        public override byte PacketID { get; } = 0;
        public byte PlayerIndex { get; set; }
        public string IP { get; set; }

        public override void Deserialize(BinaryReader reader, bool fromClient)
        {
            PlayerIndex = reader.ReadByte();
            IP = reader.ReadString();
        }

        public override void Serialize(BinaryWriter reader, bool fromClient)
        {
            reader.Write(PlayerIndex);
            reader.Write(IP);
        }
    }
}
