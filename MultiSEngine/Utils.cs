using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using TrProtocol;
using MultiSEngine.Modules.DataStruct;
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
        public static int Online(this ServerInfo server) => Modules.Data.Clients.Where(c => c.Server == server).Count();
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
        public static byte[] Serialize<T>(this T packet, bool client = true) where T : Packet => client ? Core.Net.Instance.ClientSerializer?.Serialize(packet) : Core.Net.Instance.ServerSerializer?.Serialize(packet);
        public static List<ServerInfo> GetServerInfoByName(string name)
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
        public static Position Point(int x, int y) => new() { X = (short)x, Y = (short)y };
        public static ShortPosition ShortPoint(int x, int y) => new() { X = (short)x, Y = (short)y };
        public static Vector2 Vector2(int x, int y) => new() { X = (short)x, Y = (short)y };
        public static byte[] GetTileSquare(int x, int y, int width, int heigh, int type = 541)
        {
            var bb = new BitsByte();
            bb[1] = true;
            bb[5] = true;
            var list = new ComplexTileData[width * heigh];
            for (int i = 0; i < width * heigh; i++)
            {
                list[i] = new()
                {
                    TileType = (ushort)type,
                    Liquid = 0,
                    WallColor = 0,
                    WallType = 0,
                    TileColor = 0,
                    Flags1 = bb,
                    Flags2 = 0,
                    Flags3 = 0,
                };
            }
            return Core.Net.Instance.ServerSerializer.Serialize(new TrProtocol.Packets.TileSection()
            {
                Data = new()
                {
                    StartX = x,
                    StartY = y,
                    Width = (short)width,
                    Height = (short)heigh,
                    ChestCount = 0,
                    Chests = Array.Empty<ChestData>(),
                    SignCount = 0,
                    Signs = Array.Empty<SignData>(),
                    TileEntityCount = 0,
                    TileEntities = Array.Empty<TileEntity>(),
                    Tiles = list
                }
            });
        }
        public static string GetText(this NetworkText text)
        {
            //return text._mode == NetworkText.Mode.LocalizationKey ? Language.GetTextValue(text._text) : text._text;
            return text._text;
        }
        public static MessageID GetMessageID(this byte[] buffer, int start = 0) => buffer.Length >= 3 ? (MessageID)buffer[start + 2] : MessageID.NeverCalled;
    }
}
