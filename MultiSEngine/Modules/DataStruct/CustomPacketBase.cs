using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
