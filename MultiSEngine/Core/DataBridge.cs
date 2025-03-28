using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.CustomData;

namespace MultiSEngine.Core
{
    public class DataBridge
    {
        internal static readonly Dictionary<string, Type> CustomPackets = [];
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
            if (CustomPackets.ContainsKey(name))
            {
                Logs.Warn($"CustomPacket: [{name}] already exist.");
                return;
            }
            CustomPackets.Add(name, type);
        }
    }
}

