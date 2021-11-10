using System;
using TrProtocol;

namespace MultiSEngine.DataStruct
{
    internal class CustomPacketStuff
    {
        public class CustomDataPacket : Packet
        {
            public override MessageID Type => MessageID.Unused15;
            public CustomData.CustomData Data { get; set; }
        }
        [AttributeUsage(AttributeTargets.Class)]
        public class TokenCheckAttribute : Attribute
        {

        }
    }
}
