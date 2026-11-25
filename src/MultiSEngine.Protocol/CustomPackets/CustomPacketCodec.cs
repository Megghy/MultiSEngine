using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MultiSEngine.Protocol.Framing;
using TrProtocol;

namespace MultiSEngine.Protocol.CustomPackets;

public sealed record CustomPacketEnvelope(
    string PacketType,
    long TimestampUnixMs,
    ulong Nonce,
    ReadOnlyMemory<byte> Payload,
    bool IsAuthenticated);

public enum CustomPacketDecodeStatus
{
    Success,
    InvalidFormat,
    UnsupportedVersion,
    AuthenticationRequired,
    InvalidSignature,
    ExpiredOrClockSkew,
    ReplayDetected,
}

public sealed record CustomPacketDecodeResult(
    CustomPacketDecodeStatus Status,
    CustomPacketEnvelope? Envelope = null,
    string? Error = null)
{
    public bool IsSuccess => Status == CustomPacketDecodeStatus.Success;
}

/// <summary>
/// v2 Custom Packet wire format carried inside Terraria MessageID.Unused15 (15):
/// [uint16 totalLen][byte 15][byte 'M','S','E', 2][uint16 typeLen][utf8 type][int64 unixMs][uint64 nonce][int32 payloadLen][payload][byte[32] hmacSha256]
/// HMAC covers bytes from version ('2') through the end of payload.
/// </summary>
public sealed class CustomPacketCodec
{
    private static readonly byte[] MagicPrefix = [(byte)'M', (byte)'S', (byte)'E'];
    public const byte ProtocolVersion = 2;
    public const int HmacSize = 32;

    private readonly ConcurrentDictionary<ulong, long> _seenNonces = new();
    private readonly TimeProvider _timeProvider;
    private long _lastCleanupUnixMs;

    public CustomPacketCodec(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public byte[] Encode(
        string packetType,
        ReadOnlySpan<byte> payload,
        string? sharedSecret,
        ulong? nonceOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packetType);
        var typeBytes = Encoding.UTF8.GetBytes(packetType);
        if (typeBytes.Length > ushort.MaxValue)
            throw new ArgumentException("Packet type name is too long.", nameof(packetType));

        var timestampMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var nonce = nonceOverride ?? GenerateNonce();

        // Header(3) + Magic(3) + Version(1) + TypeLen(2) + Type + Timestamp(8) + Nonce(8) + PayloadLen(4) + Payload + HMAC(32)
        var signedLength = 1 + 2 + typeBytes.Length + 8 + 8 + 4 + payload.Length;
        var totalLength = 3 + 3 + signedLength + HmacSize;
        if (totalLength > PacketFrameCodec.MaxFrameLength)
            throw new InvalidOperationException($"Custom packet exceeds maximum Terraria packet size ({totalLength} > {PacketFrameCodec.MaxFrameLength}).");

        var frame = GC.AllocateUninitializedArray<byte>(totalLength);
        var span = frame.AsSpan();

        BinaryPrimitives.WriteUInt16LittleEndian(span[0..2], (ushort)totalLength);
        span[2] = (byte)MessageID.Unused15;
        MagicPrefix.CopyTo(span[3..6]);

        var signedStart = 6;
        var cursor = signedStart;
        span[cursor++] = ProtocolVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(span[cursor..(cursor + 2)], (ushort)typeBytes.Length);
        cursor += 2;
        typeBytes.CopyTo(span[cursor..(cursor + typeBytes.Length)]);
        cursor += typeBytes.Length;
        BinaryPrimitives.WriteInt64LittleEndian(span[cursor..(cursor + 8)], timestampMs);
        cursor += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(span[cursor..(cursor + 8)], nonce);
        cursor += 8;
        BinaryPrimitives.WriteInt32LittleEndian(span[cursor..(cursor + 4)], payload.Length);
        cursor += 4;
        payload.CopyTo(span[cursor..(cursor + payload.Length)]);
        cursor += payload.Length;

        var signedRegion = span[signedStart..cursor];
        var hmacSlot = span[cursor..(cursor + HmacSize)];
        if (!string.IsNullOrEmpty(sharedSecret))
        {
            ComputeHmac(sharedSecret, signedRegion, hmacSlot);
        }
        else
        {
            hmacSlot.Clear();
        }

        return frame;
    }

    public CustomPacketDecodeResult Decode(
        ReadOnlySpan<byte> frame,
        string? expectedSecret,
        TimeSpan maxClockSkew,
        bool allowUnsigned)
    {
        const int minLength = 3 + 3 + 1 + 2 + 8 + 8 + 4 + HmacSize;
        if (frame.Length < minLength || frame[2] != (byte)MessageID.Unused15)
            return new(CustomPacketDecodeStatus.InvalidFormat, Error: "Frame too short or wrong message ID.");

        if (!frame[3..6].SequenceEqual(MagicPrefix))
            return new(CustomPacketDecodeStatus.InvalidFormat, Error: "Missing MSE2 magic header.");

        var signedStart = 6;
        var cursor = signedStart;
        var version = frame[cursor++];
        if (version != ProtocolVersion)
            return new(CustomPacketDecodeStatus.UnsupportedVersion, Error: $"Unsupported custom packet version: {version}.");

        var typeLen = BinaryPrimitives.ReadUInt16LittleEndian(frame[cursor..(cursor + 2)]);
        cursor += 2;
        if (typeLen == 0 || cursor + typeLen + 8 + 8 + 4 + HmacSize > frame.Length)
            return new(CustomPacketDecodeStatus.InvalidFormat, Error: "Invalid packet type length.");

        var packetType = Encoding.UTF8.GetString(frame[cursor..(cursor + typeLen)]);
        cursor += typeLen;

        var timestampMs = BinaryPrimitives.ReadInt64LittleEndian(frame[cursor..(cursor + 8)]);
        cursor += 8;
        var nonce = BinaryPrimitives.ReadUInt64LittleEndian(frame[cursor..(cursor + 8)]);
        cursor += 8;
        var payloadLen = BinaryPrimitives.ReadInt32LittleEndian(frame[cursor..(cursor + 4)]);
        cursor += 4;

        if (payloadLen < 0 || cursor + payloadLen + HmacSize != frame.Length)
            return new(CustomPacketDecodeStatus.InvalidFormat, Error: "Payload length mismatch.");

        var payloadSpan = frame[cursor..(cursor + payloadLen)];
        cursor += payloadLen;

        var signedRegion = frame[signedStart..cursor];
        var receivedHmac = frame[cursor..(cursor + HmacSize)];

        var isAuthenticated = false;
        if (!string.IsNullOrEmpty(expectedSecret))
        {
            Span<byte> expectedHmac = stackalloc byte[HmacSize];
            ComputeHmac(expectedSecret, signedRegion, expectedHmac);
            if (!CryptographicOperations.FixedTimeEquals(receivedHmac, expectedHmac))
                return new(CustomPacketDecodeStatus.InvalidSignature, Error: "HMAC signature verification failed.");

            var nowMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var maxSkewMs = (long)Math.Max(1000, maxClockSkew.TotalMilliseconds);
            if (Math.Abs(nowMs - timestampMs) > maxSkewMs)
                return new(CustomPacketDecodeStatus.ExpiredOrClockSkew, Error: "Custom packet timestamp outside allowed clock skew window.");

            if (!TryRecordNonce(nonce, nowMs, maxSkewMs))
                return new(CustomPacketDecodeStatus.ReplayDetected, Error: "Duplicate custom packet nonce detected.");

            isAuthenticated = true;
        }
        else if (!allowUnsigned)
        {
            return new(CustomPacketDecodeStatus.AuthenticationRequired, Error: "Custom packet rejected because no shared secret is configured.");
        }

        return new(
            CustomPacketDecodeStatus.Success,
            new CustomPacketEnvelope(packetType, timestampMs, nonce, payloadSpan.ToArray(), isAuthenticated));
    }

    private bool TryRecordNonce(ulong nonce, long nowMs, long windowMs)
    {
        if (!_seenNonces.TryAdd(nonce, nowMs))
            return false;

        var lastCleanup = Volatile.Read(ref _lastCleanupUnixMs);
        if (nowMs - lastCleanup > windowMs && Interlocked.CompareExchange(ref _lastCleanupUnixMs, nowMs, lastCleanup) == lastCleanup)
        {
            var cutoff = nowMs - windowMs * 2;
            foreach (var (key, seenAt) in _seenNonces)
            {
                if (seenAt < cutoff)
                    _seenNonces.TryRemove(key, out _);
            }
        }

        return true;
    }

    private static void ComputeHmac(string secret, ReadOnlySpan<byte> data, Span<byte> destination)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        HMACSHA256.HashData(keyBytes, data, destination);
    }

    private static ulong GenerateNonce()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }
}

public static class BuiltInCustomPacketTypes
{
    public const string SyncIp = "mse.sync_ip";
    public const string ExecuteCommand = "mse.execute_command";

    public static byte[] EncodeSyncIpPayload(string playerName, string ipAddress)
    {
        using var ms = new MemoryStream(64);
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        bw.Write(playerName ?? string.Empty);
        bw.Write(ipAddress ?? string.Empty);
        return ms.ToArray();
    }

    public static (string PlayerName, string IpAddress) DecodeSyncIpPayload(ReadOnlyMemory<byte> payload)
    {
        using var ms = new MemoryStream(payload.ToArray(), writable: false);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        return (br.ReadString(), br.ReadString());
    }

    public static byte[] EncodeExecuteCommandPayload(string targetPlayerName, string commandText)
    {
        using var ms = new MemoryStream(64);
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        bw.Write(targetPlayerName ?? string.Empty);
        bw.Write(commandText ?? string.Empty);
        return ms.ToArray();
    }

    public static (string TargetPlayerName, string CommandText) DecodeExecuteCommandPayload(ReadOnlyMemory<byte> payload)
    {
        using var ms = new MemoryStream(payload.ToArray(), writable: false);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        return (br.ReadString(), br.ReadString());
    }
}
