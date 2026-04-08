using System.Collections.Frozen;
using MultiSEngine.Protocol.CustomData;
using MultiSEngine.Runtime;

namespace MultiSEngine.Protocol
{
    public class DataBridge
    {
        internal static FrozenDictionary<string, Type> CustomPackets => RuntimeState.CustomPackets.Snapshot();
        public static void RegisterCustomPacket<T>() where T : BaseCustomData
        {
            RuntimeState.CustomPackets.Register<T>();
        }
        private static void RegisterCustomPacket(Type type)
        {
            RuntimeState.CustomPackets.Register(type);
        }

        public static void RebuildCustomPacketIndex()
            => _ = RuntimeState.CustomPackets.Snapshot();
    }
}

