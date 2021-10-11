using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using TrProtocol;
using TrProtocol.Models;

namespace MultiSEngine
{
    public static class Utils
    {
        /*public static T Deserilize<T>(this byte[] buffer) where T : IPacket
        {
            using (var reader = new BinaryReader(new MemoryStream(buffer)))
                return reader.Deserialize<T>();
        }*/
        public static bool TryParseAddress(string address, out string ip)
        {
            ip = "";
            try
            {
                if (IPAddress.TryParse(address, out _))
                {
                    ip = address;
                    return true;
                }
                else
                {
                    IPHostEntry hostinfo = Dns.GetHostEntry(address);
                    if (hostinfo.AddressList.FirstOrDefault() is { } _ip)
                    {
                        ip = _ip.ToString();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
        public static ShortPosition Point(int x, int y) => new() { X = (short)x, Y = (short)y };
        public static byte[] Serilize<T>(this T packet) where T: Packet => Core.Net.Instance.Serializer?.Serialize(packet);
        public static List<Modules.DataStruct.ServerInfo> GetServerInfoByName(string name)
        {
            return Config.Instance.Servers.Where(s => s.Name.ToLower().StartsWith(name.ToLower()) || s.Name.ToLower().Contains(name.ToLower())).ToList();
        }
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }
            foreach (T obj in source)
            {
                action(obj);
            }
        }
    }
}
