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
        public override bool RecieveServerData(MessageID msgType, byte[] data)
        {
            if (msgType is MessageID.Unused15)
            {
                var custom = data.AsPacket<CustomPacketStuff.CustomDataPacket>();
                custom?.Data.RecievedData(Client);
                return true;
            }
            return false;
        }
    }
}
