using System;
using System.Linq;
using Delphinus;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Core
{
    public class DataBridge
    {
        internal static void Init()
        {
            AppDomain.CurrentDomain.GetAssemblies().ForEach(assembly =>
            {
                try
                {
                    assembly
                        .GetTypes()
                        .Where(t => t.BaseType == typeof(CustomPacketBase))
                        .ForEach(t => PacketSerializer.RegistePacket((Packet)Activator.CreateInstance(t)));
                }
                catch { }
            });
        }
    }
}

