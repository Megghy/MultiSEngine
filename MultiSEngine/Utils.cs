using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Delphinus;
using Delphinus.Packets;
using Terraria;
using Terraria.Localization;

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
            catch {  }
            return false;
        }
        public static byte[] Serilize<T>(this T packet, bool client = true) where T : Packet => client ? Core.Net.Instance.ClientSerializer?.Serialize(packet) : Core.Net.Instance.ServerSerializer?.Serialize(packet);
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
        public static byte[] GetTileSquare(int x, int y, int width, int heigh, int type = 541)
        {
            using (var writer = new BinaryWriter(new MemoryStream()))
            {
                writer.BaseStream.Position += 2L;
                writer.Write((byte)20);
                writer.Write((short)x);
                writer.Write((short)y);
                writer.Write((byte)width);
                writer.Write((byte)heigh);
                writer.Write((byte)0);
                for (int tempX = x; tempX < x + width; tempX++)
                {
                    for (int tempY = y; tempY < y + heigh; tempY++)
                    {
                        BitsByte bb18 = 0;
                        BitsByte bb19 = 0;
                        byte b = 0;
                        byte b2 = 0;
                        bb18[0] = true; //active
                        bb18[2] = false; //wall
                        bb18[3] = false; //liquid
                        bb18[4] = false; //wire
                        bb18[5] = false; //half
                        bb18[6] = false;
                        bb18[7] = false;
                        bb19[0] = false;
                        bb19[1] = false;
                        //bb19 += (byte)(tile.slope() << 4);
                        bb19[7] = false;
                        writer.Write(bb18);
                        writer.Write(bb19);
                        if (b > 0)
                        {
                            writer.Write(b);
                        }
                        if (b2 > 0)
                        {
                            writer.Write(b2);
                        }
                        writer.Write(type);
                    }
                }
                writer.BaseStream.Position = 0L;
                writer.Write((short)writer.BaseStream.Length);
                writer.BaseStream.Position = 0L;
                byte[] bytes = new byte[writer.BaseStream.Length];
                writer.BaseStream.Read(bytes, 0, bytes.Length);
                writer.BaseStream.Seek(0, SeekOrigin.Begin);
                return bytes;
            }
        }
        public static string GetText(this NetworkText text)
        {
            return text._mode == NetworkText.Mode.LocalizationKey ? Language.GetTextValue(text._text) : text._text;
        }
    }
}
