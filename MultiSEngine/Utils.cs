using System.Buffers;
using System.Net;
using System.Net.Sockets;
using MultiSEngine.DataStruct;

namespace MultiSEngine
{
    public static class Utils
    {
        private const int DefaultPacketBufferSize = 1024 * 16;

        /// <summary>
        /// 封装 NetPacket 序列化后的缓冲租借，确保使用完及时归还内存池。
        /// </summary>
        public sealed class PacketMemoryRental : IDisposable
        {
            private IMemoryOwner<byte>? _owner;

            public PacketMemoryRental(IMemoryOwner<byte> owner, int length)
            {
                _owner = owner;
                Memory = owner.Memory.Slice(0, length);
            }

            public ReadOnlyMemory<byte> Memory { get; }

            public void Dispose()
            {
                _owner?.Dispose();
                _owner = null;
            }
        }
        private static PacketSerializer? _s2cSerializer; // server -> client
        private static PacketSerializer? _c2sSerializer; // client -> server

        private static PacketSerializer GetS2CSerializer()
        {
            return _s2cSerializer ??= new PacketSerializer(true, $"Terraria{Config.Instance.ServerVersion}");
        }
        private static PacketSerializer GetC2SSerializer()
        {
            return _c2sSerializer ??= new PacketSerializer(false, $"Terraria{Config.Instance.ServerVersion}");
        }

        private sealed class ReadOnlyMemoryStream : Stream
        {
            private readonly ReadOnlyMemory<byte> _buffer;
            private int _position;
            public ReadOnlyMemoryStream(ReadOnlyMemory<byte> buffer) => _buffer = buffer;
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _buffer.Length;
            public override long Position { get => _position; set => _position = (int)value; }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                var remaining = _buffer.Length - _position;
                if (remaining <= 0) return 0;
                var n = Math.Min(remaining, count);
                _buffer.Span.Slice(_position, n).CopyTo(buffer.AsSpan(offset, n));
                _position += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin)
            {
                long target = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End => Length + offset,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };
                if (target < 0 || target > Length) throw new IOException("Attempted to seek outside the buffer");
                _position = (int)target; return _position;
            }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        public static Packet? AsPacket(this ReadOnlySpan<byte> buf, bool fromServer = true)
            => AsPacket((ReadOnlyMemory<byte>)buf.ToArray(), fromServer);

        public static Packet? AsPacket(this ReadOnlyMemory<byte> buf, bool fromServer = true)
        {
            using var ms = new ReadOnlyMemoryStream(buf);
            using var br = new BinaryReader(ms);
            return (fromServer ? GetS2CSerializer() : GetC2SSerializer()).Deserialize(br);
        }
        public static T? AsPacket<T>(this ReadOnlyMemory<byte> buf, bool fromServer = true) where T : Packet
            => AsPacket(buf, fromServer) as T;
        public static T? AsPacket<T>(this ReadOnlySpan<byte> buf, bool fromServer = true) where T : Packet
            => AsPacket(buf, fromServer) as T;

        public static PacketMemoryRental AsPacketRental(this Packet packet, int bufferSizeHint = DefaultPacketBufferSize)
        {
            var bytes = GetC2SSerializer().Serialize(packet); // serialization identical for length header
            var owner = MemoryPool<byte>.Shared.Rent(Math.Max(bytes.Length, bufferSizeHint));
            var memory = owner.Memory;
            bytes.CopyTo(memory);
            return new PacketMemoryRental(owner, bytes.Length);
        }
        public static ReadOnlyMemory<byte> AsReadOnlyMemory(this Packet packet, out PacketMemoryRental rental, int bufferSizeHint = DefaultPacketBufferSize)
        {
            rental = packet.AsPacketRental(bufferSizeHint);
            return rental.Memory;
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

        /// <summary>
        /// 异步解析地址, 优先返回字面 IP, 否则进行 DNS 查询。
        /// </summary>
        public static async ValueTask<IPAddress?> ResolveAddressAsync(string address, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;

            if (IPAddress.TryParse(address, out var ip))
            {
                return ip;
            }

            try
            {
                var hostInfo = await Dns.GetHostEntryAsync(address).WaitAsync(cancellationToken).ConfigureAwait(false);
                return hostInfo.AddressList.FirstOrDefault();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
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
        public static PacketMemoryRental GetTileSection(int x, int y, short width, short height, int type = 541)
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
            return new TileSection { Data = new SectionData { StartX = x, StartY = y, Width = width, Height = height, Tiles = list, ChestCount = 0, Chests = [], SignCount = 0, Signs = [], TileEntityCount = 0, TileEntities = [] } }.AsPacketRental();
        }
        public static string GetText(this NetworkText text)
        {
            //return text._mode == NetworkText.Mode.LocalizationKey ? Language.GetTextValue(text._text) : text._text;
            return text._text;
        }
    }
}
