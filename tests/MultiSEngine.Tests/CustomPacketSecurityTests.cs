using MultiSEngine.Protocol.CustomPackets;

namespace MultiSEngine.Tests;

internal sealed class AdjustableTimeProvider(DateTimeOffset startUtc) : TimeProvider
{
    private DateTimeOffset _now = startUtc;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now += delta;
}

public sealed class CustomPacketSecurityTests
{
    [Fact]
    public void EncodeAndDecode_ValidSecret_RoundTripsAndRejectsReplay()
    {
        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        var codec = new CustomPacketCodec(clock);
        var payload = BuiltInCustomPacketTypes.EncodeSyncIpPayload("Steve", "203.0.113.42");

        var frame = codec.Encode(BuiltInCustomPacketTypes.SyncIp, payload, "super-secret-key", nonceOverride: 424242UL);

        var first = codec.Decode(frame, "super-secret-key", TimeSpan.FromSeconds(30), allowUnsigned: false);
        Assert.True(first.IsSuccess);
        Assert.True(first.Envelope!.IsAuthenticated);
        Assert.Equal(BuiltInCustomPacketTypes.SyncIp, first.Envelope.PacketType);

        var (player, ip) = BuiltInCustomPacketTypes.DecodeSyncIpPayload(first.Envelope.Payload);
        Assert.Equal("Steve", player);
        Assert.Equal("203.0.113.42", ip);

        // Replaying the exact same frame must be rejected.
        var replay = codec.Decode(frame, "super-secret-key", TimeSpan.FromSeconds(30), allowUnsigned: false);
        Assert.Equal(CustomPacketDecodeStatus.ReplayDetected, replay.Status);
    }

    [Fact]
    public void Decode_TamperedPayloadOrWrongSecret_RejectedWithInvalidSignature()
    {
        var codec = new CustomPacketCodec();
        var payload = BuiltInCustomPacketTypes.EncodeExecuteCommandPayload("Alice", "/mse tp pvp");
        var frame = codec.Encode(BuiltInCustomPacketTypes.ExecuteCommand, payload, "secret-A");

        var wrongSecret = codec.Decode(frame, "secret-B", TimeSpan.FromSeconds(30), allowUnsigned: false);
        Assert.Equal(CustomPacketDecodeStatus.InvalidSignature, wrongSecret.Status);

        // Flip one bit inside payload.
        frame[frame.Length - CustomPacketCodec.HmacSize - 1] ^= 0x01;
        var tampered = codec.Decode(frame, "secret-A", TimeSpan.FromSeconds(30), allowUnsigned: false);
        Assert.Equal(CustomPacketDecodeStatus.InvalidSignature, tampered.Status);
    }

    [Fact]
    public void Decode_ExpiredTimestampOrMissingSecret_Rejected()
    {
        var clock = new AdjustableTimeProvider(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        var codec = new CustomPacketCodec(clock);
        var payload = BuiltInCustomPacketTypes.EncodeSyncIpPayload("Alice", "127.0.0.1");
        var frame = codec.Encode(BuiltInCustomPacketTypes.SyncIp, payload, "secret");

        clock.Advance(TimeSpan.FromSeconds(60));
        var expired = codec.Decode(frame, "secret", TimeSpan.FromSeconds(30), allowUnsigned: false);
        Assert.Equal(CustomPacketDecodeStatus.ExpiredOrClockSkew, expired.Status);

        var noSecret = codec.Decode(frame, expectedSecret: null, TimeSpan.FromSeconds(30), allowUnsigned: false);
        Assert.Equal(CustomPacketDecodeStatus.AuthenticationRequired, noSecret.Status);
    }
}
