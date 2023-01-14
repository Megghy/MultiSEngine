using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using MultiSEngine.Core;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
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
        public static Packet AsPacket(this Span<byte> buf)
        {
            using var reader = new BinaryReader(new MemoryStream(buf.ToArray()));
            return Net.DefaultServerSerializer.Deserialize(reader);
        }
        public static T AsPacket<T>(this Span<byte> buf) where T : Packet
        {
            using var reader = new BinaryReader(new MemoryStream(buf.ToArray()));
            return Net.DefaultServerSerializer.Deserialize(reader) as T;
        }
        public static byte[] AsBytes(this Packet packet)
        {
            return Net.DefaultServerSerializer.Serialize(packet);
        }
        public static ClientData[] Online(this ServerInfo server) => Modules.Data.Clients.Where(c => c.CurrentServer == server).ToArray();
        public static bool TryParseAddress(string address, out IPAddress ip)
        {
            ip = default;
            try
            {
                if (IPAddress.TryParse(address, out ip))
                {
                    return true;
                }
                else
                {
                    IPHostEntry hostinfo = Dns.GetHostEntry(address);
                    if (hostinfo.AddressList.FirstOrDefault() is { } _ip)
                    {
                        ip = _ip;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
        public static ServerInfo[] GetServersInfoByName(string name)
        {
            return Config.Instance.Servers.Where(s => s.Name.ToLower().StartsWith(name.ToLower()) || s.Name.ToLower().Contains(name.ToLower()) || s.ShortName == name).ToArray();
        }
        public static bool IsOnline(this TcpClient c)
        {
            return !((c.Client.Poll(1000, SelectMode.SelectRead) && (c.Client.Available == 0)) || !c.Client.Connected);
        }
        public static ServerInfo GetSingleServerInfoByName(string name)
        {
            if (GetServersInfoByName(name) is { } temp && temp.Any())
                return temp.First();
            return null;
        }
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            foreach (T obj in source)
            {
                action(obj);
            }
        }
        public static byte[] GetTileSection(int x, int y, int width, int heigh, int type = 541)
        {
            var bb = new BitsByte();
            bb[1] = true;
            bb[5] = true;
            var tile = new ComplexTileData()
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
            var list = new ComplexTileData[width * heigh];
            for (int i = 0; i < width * heigh; i++)
            {
                list[i] = tile;
            }
            return Core.Net.DefaultServerSerializer.Serialize(new TrProtocol.Packets.TileSection()
            {
                Data = new()
                {
                    //IsCompressed = true,
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
    }
}
