using System;
using System.IO;
using TrProtocol;

namespace MultiSEngine.Modules.DataStruct
{
    public abstract class CustomPacketBase : Packet
    {
        public abstract byte PacketID { get; }
        public override MessageID Type => MessageID.Unused15;
    }
}
