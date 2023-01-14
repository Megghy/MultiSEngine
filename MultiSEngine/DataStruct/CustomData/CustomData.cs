using System;
using System.IO;
using System.Linq;
using TrProtocol;

namespace MultiSEngine.DataStruct.CustomData
{
    [Serializer(typeof(CustomDataSerializer))]
    public abstract class CustomData
    {
        public abstract string Name { get; }
        public virtual string Token => Config.Instance.Token;
        public abstract void InternalWrite(BinaryWriter writer);
        public abstract void InternalRead(BinaryReader reader);
        public virtual void RecievedData(ClientData client)
        {

        }
        private class CustomDataSerializer : FieldSerializer<CustomData>
        {
            protected override CustomData ReadOverride(BinaryReader br)
            {
                var name = br.ReadString();
                if (Core.DataBridge.CustomPackets.TryGetValue(name, out var type))
                {
                    var token = br.ReadString();
                    if (type.GetCustomAttributes(true).Any(a => a is CustomPacketStuff.TokenCheckAttribute) && token != Config.Instance.Token)
                    {
                        Logs.Warn($"Recieve custom data [{name}] with invalid token: {token}.");
                        return null;
                    }
                    var packet = Activator.CreateInstance(type) as CustomData;
                    packet?.InternalRead(br);
                    return packet;
                }
                else
                {
                    Logs.Error($"Packet [{name}] not defined, ignore.");
                    return null;
                }
            }

            protected override void WriteOverride(BinaryWriter bw, CustomData t)
            {
                bw.Write(t.Name);
                bw.Write(t.Token);
                t.InternalWrite(bw);
            }
        }
    }
}
