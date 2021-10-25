using System;
using System.Linq;
using TrProtocol;
using MultiSEngine.Modules.CustomData;
using System.Collections.Generic;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Core
{
    public class DataBridge
    {
        internal static readonly Dictionary<string, Type> CustomPackets = new();
        public static string Token => Config.Instance.Token;
        internal static void Init()
        {
            Net.Instance.ServerSerializer.RegisterPacket<CustomPacketStuff.CustomDataPacket>();
            Net.Instance.ClientSerializer.RegisterPacket<CustomPacketStuff.CustomDataPacket>();
            AppDomain.CurrentDomain.GetAssemblies().ForEach(assembly =>
            {
                try
                {
                    assembly
                        .GetTypes()
                        .Where(t => t.BaseType == typeof(CustomData))
                        .ForEach(t => RegisterCustomPacket(t));
                }
                catch (Exception ex){ Logs.Error(ex); }
            });
        }
        public static void RegisterCustomPacket<T>() where T : Packet
        {
            RegisterCustomPacket(typeof(T));
        }
        private static void RegisterCustomPacket(Type type)
        {
            var name = (Activator.CreateInstance(type) as CustomData).Name;
            if (CustomPackets.ContainsKey(name))
            {
                Logs.Warn($"CustomPacket: {name} already exist.");
                return;
            }
            CustomPackets.Add(name, type);
        }
    }
}

