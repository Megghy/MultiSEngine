using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;

namespace MultiSEngine.Core.Handler
{
    public class CustomPacketHandler(BaseAdapter parent) : BaseHandler(parent)
    {
        public override bool RecieveServerData(MessageID msgType, Span<byte> data)
        {
            if (msgType is MessageID.Unused15)
            {
                using var ms = new MemoryStream(data.ToArray());
                using var br = new BinaryReader(ms);
                br.BaseStream.Position = 2;
                var name = br.ReadString();
                if (DataBridge.CustomPackets.TryGetValue(name, out var type))
                {
                    var token = br.ReadString();
                    var packet = Activator.CreateInstance(type) as DataStruct.CustomData.BaseCustomData;
                    packet.InternalRead(br);
                    packet.OnRecievedData(Client);
                }
                else
                {
                    Logs.Error($"Packet [{name}] not defined, ignore.");
                }
                return true;
            }
            return false;
        }
    }
}
