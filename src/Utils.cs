using System.Buffers;
using System.Net;
using System.Net.Sockets;
using TrProtocol.Interfaces;

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
            private IDisposable? _owner;

            public PacketMemoryRental(IMemoryOwner<byte> owner, int length)
                : this((IDisposable)owner, owner.Memory.Slice(0, length))
            {
            }

            public PacketMemoryRental(PacketCodecRental rental)
                : this(rental, rental.Memory)
            {
            }

            private PacketMemoryRental(IDisposable owner, ReadOnlyMemory<byte> memory)
            {
                _owner = owner;
                Memory = memory;
            }

            public ReadOnlyMemory<byte> Memory { get; }

            public void Dispose()
            {
                _owner?.Dispose();
                _owner = null;
            }
        }

        public sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
        {
            private byte[] _buffer;

            public PooledBufferWriter(int initialCapacity = PacketCodec.MaxPacketSize)
            {
                _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            }

            public int WrittenCount { get; private set; }

            public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, WrittenCount);

            public void Advance(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));

                var next = WrittenCount + count;
                if (next > _buffer.Length)
                    throw new InvalidOperationException("Advanced beyond rented buffer length.");

                WrittenCount = next;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return _buffer.AsMemory(WrittenCount);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                EnsureCapacity(sizeHint);
                return _buffer.AsSpan(WrittenCount);
            }

            public void Dispose()
            {
                ArrayPool<byte>.Shared.Return(_buffer, clearArray: false);
                _buffer = [];
                WrittenCount = 0;
            }

            private void EnsureCapacity(int sizeHint)
            {
                if (sizeHint < 1)
                    sizeHint = 1;

                var required = WrittenCount + sizeHint;
                if (required <= _buffer.Length)
                    return;

                var newSize = Math.Max(required, _buffer.Length * 2);
                var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
                _buffer.AsSpan(0, WrittenCount).CopyTo(newBuffer);
                ArrayPool<byte>.Shared.Return(_buffer, clearArray: false);
                _buffer = newBuffer;
            }
        }

        public static INetPacket AsPacket(this ReadOnlySpan<byte> buf, bool fromServer = true)
            => PacketCodec.Deserialize(buf, client: fromServer);

        public static INetPacket AsPacket(this ReadOnlyMemory<byte> buf, bool fromServer = true)
            => PacketCodec.Deserialize(buf.Span, client: fromServer);

        public static PacketMemoryRental AsPacketRental(this INetPacket packet, bool fromServer = true, int bufferSizeHint = DefaultPacketBufferSize)
        {
            if (packet is ISideSpecific sideSpecific)
                sideSpecific.IsServerSide = fromServer;

            var rentedPacket = PacketCodec.SerializeRented(packet);
            return new PacketMemoryRental(rentedPacket);
        }
        public static PacketMemoryRental AsPacketRental(this ReadOnlyMemory<byte> buffer, int bufferSizeHint = DefaultPacketBufferSize)
        {
            var owner = MemoryPool<byte>.Shared.Rent(Math.Max(buffer.Length, bufferSizeHint));
            buffer.Span.CopyTo(owner.Memory.Span);
            return new PacketMemoryRental(owner, buffer.Length);
        }
        public static ReadOnlyMemory<byte> AsReadOnlyMemory(this INetPacket packet, out PacketMemoryRental rental, bool fromServer = true, int bufferSizeHint = DefaultPacketBufferSize)
        {
            rental = packet.AsPacketRental(fromServer, bufferSizeHint);
            return rental.Memory;
        }

        public static int WritePacket(this IBufferWriter<byte> writer, INetPacket packet, bool fromServer = true)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(packet);

            if (packet is ISideSpecific sideSpecific)
                sideSpecific.IsServerSide = fromServer;

            return PacketCodec.SerializeDirect(packet, writer);
        }

        public static Color Rgb(byte r, byte g, byte b, byte a = byte.MaxValue)
            => new()
            {
                R = r,
                G = g,
                B = b,
                A = a,
            };
        public static NetworkText LiteralText(string text)
            => new(text, NetworkText.Mode.Literal);
        public static ClientData[] Online(this ServerInfo server) => Runtime.RuntimeState.ClientRegistry.Where(c => c.CurrentServer == server);
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
            return new TileSection { Data = new SectionData { StartX = x, StartY = y, Width = width, Height = height, Tiles = list, ChestCount = 0, Chests = [], SignCount = 0, Signs = [], TileEntityCount = 0, TileEntities = [] } }.AsPacketRental(true);
        }
        public static string GetText(this NetworkText text)
        {
            return text.ToString();
        }
    }
}


