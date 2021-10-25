using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrProtocol;

namespace MultiSEngine.Modules.DataStruct
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
