using System.Collections.Frozen;
using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.CustomData;

namespace MultiSEngine.Core
{
    public class DataBridge
    {
        private static readonly Dictionary<string, Type> CustomPacketsMutable = [];
        internal static FrozenDictionary<string, Type> CustomPackets { get; private set; } = FrozenDictionary<string, Type>.Empty;
        [AutoInit]
        private static void Init()
        {
            AppDomain.CurrentDomain.GetAssemblies().ForEach(assembly =>
            {
                try
                {
                    assembly.GetTypes()
                        .Where(t => t.BaseType == typeof(BaseCustomData))
                        .ForEach(t => RegisterCustomPacket(t));
                }
                catch (Exception ex) { Logs.Error(ex); }
            });
        }
        public static void RegisterCustomPacket<T>() where T : BaseCustomData
        {
            RegisterCustomPacket(typeof(T));
        }
        private static void RegisterCustomPacket(Type type)
        {
            var name = (Activator.CreateInstance(type) as BaseCustomData)!.Name;
            if (!CustomPacketsMutable.TryAdd(name, type))
            {
                Logs.Warn($"CustomPacket: [{name}] already exist.");
                return;
            }
        }

        public static void RebuildCustomPacketIndex()
        {
            CustomPackets = CustomPacketsMutable.ToFrozenDictionary();
        }

        [AutoInit(order: 10000)]
        private static void BuildIndex()
            => RebuildCustomPacketIndex();
    }
}

