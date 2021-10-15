using System;
using System.IO;
using Delphinus;

namespace MultiSEngine.Modules.DataStruct
{
    public abstract class CustomPacketBase : Packet
    {
        public abstract byte PacketID { get; }
        public override MessageID MessageID => MessageID.Unused15;
        public override void Deserialize(BinaryReader reader, bool fromClient)
        {
            throw new NotImplementedException();
        }
    }
}
