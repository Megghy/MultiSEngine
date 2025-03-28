using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MultiSEngine.DataStruct;
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
        public unsafe static NetPacket AsPacket(this ref Span<byte> buf, bool asServer = true)
        {
            fixed (void* ptr = buf)
            {
                var ptrBegin = Unsafe.Add<byte>(ptr, 2); // 从type开始读
                return NetPacket.ReadNetPacket(ref ptrBegin, ptr_end: Unsafe.Add<byte>(ptrBegin, buf.Length), asServer);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="buf"></param>
        /// <param name="asServer">Read as server or not</param>
        /// <returns></returns>
        public unsafe static T AsPacket<T>(this ref Span<byte> buf, bool asServer = true) where T : NetPacket
        {
            return buf.AsPacket(asServer) as T;
        }
        public unsafe static T AsPacket<T>(this byte[] buf, bool asServer = true) where T : NetPacket
        {
            var b = buf.AsSpan();
            return AsPacket<T>(ref b, asServer);
        }
        public unsafe static Span<byte> AsBytes(this NetPacket packet)
        {
            var ptr_begin = (void*)Marshal.AllocHGlobal(1024 * 16);

            var ptr = Unsafe.Add<byte>(ptr_begin, 2);
            packet.WriteContent(ref ptr);
            var size = (short)((long)ptr - (long)ptr_begin);
            Unsafe.Write(ptr_begin, size);
            return new Span<byte>(ptr_begin, size);
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
        public static Span<byte> GetTileSection(int x, int y, short width, short height, int type = 541)
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
                Flags1 = bb.value,
                Flags2 = 0,
                Flags3 = 0,
            };
            var list = new ComplexTileData[width * height];
            for (int i = 0; i < width * height; i++)
            {
                list[i] = tile;
            }
            return new TileSection(new(x, y, width, height, list, 0, [], 0, [], 0, [])).AsBytes();
        }
        public static string GetText(this NetworkTextModel text)
        {
            //return text._mode == NetworkText.Mode.LocalizationKey ? Language.GetTextValue(text._text) : text._text;
            return text._text;
        }
    }
}
