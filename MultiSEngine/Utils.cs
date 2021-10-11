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
    }
}
