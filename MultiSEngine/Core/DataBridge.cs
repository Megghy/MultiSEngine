using System;
using System.Collections.Generic;
using System.Linq;
using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.CustomData;

namespace MultiSEngine.Core
{
    public class DataBridge
    {
        internal static readonly Dictionary<string, Type> CustomPackets = new();
        public static string Token => Config.Instance.Token;
        [AutoInit]
        private static void Init()
        {
            Net.ServerSerializer.ForEach(s => s.Value.RegisterPacket<CustomPacketStuff.CustomDataPacket>());
            Net.ClientSerializer.ForEach(c => c.Value.RegisterPacket<CustomPacketStuff.CustomDataPacket>());
            AppDomain.CurrentDomain.GetAssemblies().ForEach(assembly =>
            {
                try
                {
                    assembly.GetTypes()
                        .Where(t => t.BaseType == typeof(CustomData))
                        .ForEach(t => RegisterCustomPacket(t));
                }
                catch (Exception ex) { Logs.Error(ex); }
            });
        }
        public static void RegisterCustomPacket<T>() where T : CustomData
        {
            RegisterCustomPacket(typeof(T));
        }
        private static void RegisterCustomPacket(Type type)
        {
            var name = (Activator.CreateInstance(type) as CustomData).Name;
            if (CustomPackets.ContainsKey(name))
            {
                Logs.Warn($"CustomPacket: [{name}] already exist.");
                return;
            }
            CustomPackets.Add(name, type);
        }
    }
}

