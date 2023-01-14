using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using TrProtocol;

namespace MultiSEngine.Core.Handler
{
    public class CustomPacketHandler : BaseHandler
    {
        public CustomPacketHandler(BaseAdapter parent) : base(parent)
        {
        }
        public override bool RecieveServerData(MessageID msgType, ref Span<byte> data)
        {
            if(msgType is MessageID.Unused15)
            {
                var custom = data.AsPacket<CustomPacketStuff.CustomDataPacket>();
                custom?.Data.RecievedData(Client);
                return true;
            }
            return false;
        }
    }
}
