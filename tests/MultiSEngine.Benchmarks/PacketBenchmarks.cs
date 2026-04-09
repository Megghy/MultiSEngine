using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using MultiSEngine.Protocol;
using TrProtocol;
using TrProtocol.NetPackets;

namespace MultiSEngine.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class PacketSerializationBenchmarks
{
    private readonly SyncPlayer _packet = new()
    {
        PlayerSlot = 7,
        SkinVariant = 1,
        Hair = 12,
        Name = "BenchmarkPlayer",
        HairDye = 2,
        HairColor = Utils.Rgb(10, 20, 30),
        SkinColor = Utils.Rgb(40, 50, 60),
        EyeColor = Utils.Rgb(70, 80, 90),
        ShirtColor = Utils.Rgb(100, 110, 120),
        UnderShirtColor = Utils.Rgb(130, 140, 150),
        PantsColor = Utils.Rgb(160, 170, 180),
        ShoeColor = Utils.Rgb(190, 200, 210),
    };

    private byte[] _scratchBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _scratchBuffer = ArrayPool<byte>.Shared.Rent(PacketCodec.MaxPacketSize);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        ArrayPool<byte>.Shared.Return(_scratchBuffer, clearArray: true);
    }

    [Benchmark(Baseline = true)]
    public byte[] LegacySerializeToArray()
        => LegacyPacketSerializer.Serialize(_packet);

    [Benchmark]
    public int SerializeViaPacketCodec()
        => PacketCodec.Serialize(_packet, _scratchBuffer);

    [Benchmark]
    public int SerializeAsPacketRental()
    {
        using var rental = _packet.AsPacketRental(true);
        return rental.Memory.Length;
    }

    [Benchmark]
    public int SerializeViaPacketCodecRented()
    {
        using var rental = PacketCodec.SerializeRented(_packet);
        return rental.Memory.Length;
    }
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class PacketDeserializationBenchmarks
{
    private byte[] _packetBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        var packet = new LoadPlayer
        {
            PlayerSlot = 9,
        };

        _packetBytes = LegacyPacketSerializer.Serialize(packet);
    }

    [Benchmark(Baseline = true)]
    public object LegacyDeserializeWithBinaryReader()
        => LegacyPacketSerializer.Deserialize(_packetBytes, client: true);

    [Benchmark]
    public object DeserializeViaPacketCodec()
        => PacketCodec.Deserialize(_packetBytes.AsSpan(), client: true);

    [Benchmark]
    public object DeserializeViaUtils()
        => Utils.AsPacket(_packetBytes.AsSpan(), fromServer: true);
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class HandlerPacketContextBenchmarks
{
    private byte[] _packetBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _packetBytes = LegacyPacketSerializer.Serialize(new LoadPlayer
        {
            PlayerSlot = 3,
        });
    }

    [Benchmark(Baseline = true)]
    public object LegacyLazyPacketMaterialization()
        => new LegacyHandlerPacketContext(MessageID.LoadPlayer, _packetBytes, fromServer: true).Packet;

    [Benchmark]
    public object CurrentPacketMaterialization()
        => new HandlerPacketContext(MessageID.LoadPlayer, _packetBytes, fromServer: true).Packet;

    private sealed class LegacyHandlerPacketContext(MessageID messageId, ReadOnlyMemory<byte> data, bool fromServer)
    {
        private readonly Lazy<object> _packet = new(() => Utils.AsPacket(data.Span, fromServer), LazyThreadSafetyMode.None);

        public MessageID MessageId { get; } = messageId;

        public object Packet => _packet.Value;
    }
}

internal static class LegacyPacketSerializer
{
    private const int MaxPacketSize = ushort.MaxValue;

    public static byte[] Serialize(INetPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);

        var tempBuffer = ArrayPool<byte>.Shared.Rent(MaxPacketSize);
        try
        {
            int contentLength;
            unsafe
            {
                fixed (byte* pTemp = tempBuffer)
                {
                    void* ptr = pTemp;
                    packet.WriteContent(ref ptr);
                    contentLength = (int)((byte*)ptr - pTemp);
                }
            }

            writer.BaseStream.Position = 0;
            writer.Write((ushort)(contentLength + 2));
            writer.Write(tempBuffer, 0, contentLength);
            return stream.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer, clearArray: true);
        }
    }

    public static object Deserialize(byte[] packetBytes, bool client)
    {
        using var stream = new MemoryStream(packetBytes, writable: false);
        using var reader = new BinaryReader(stream);

        var totalLength = reader.ReadUInt16();
        var payloadLength = totalLength - 2;
        var payload = reader.ReadBytes(payloadLength);

        unsafe
        {
            fixed (byte* pPayload = payload)
            {
                void* ptr = pPayload;
                byte* end = pPayload + payloadLength;
                return INetPacket.ReadINetPacket(ref ptr, end, !client);
            }
        }
    }
}
