using System.Buffers;
using TrProtocol;

namespace MultiSEngine.Protocol.Terraria;

public enum InboundRouteDecision
{
    /// <summary>Zero-copy direct pass-through to the opposite peer.</summary>
    FastPassThrough,
    /// <summary>Must be inspected or handled by the session/protocol layer.</summary>
    Inspect,
}

/// <summary>
/// O(1) raw-byte classifier that avoids deserializing high-frequency broadcast packets for other players.
/// </summary>
public static class FastPathClassifier
{
    public static MessageID PeekMessageId(in ReadOnlySequence<byte> frame)
    {
        if (frame.FirstSpan.Length >= 3)
            return (MessageID)frame.FirstSpan[2];

        Span<byte> header = stackalloc byte[3];
        frame.Slice(0, 3).CopyTo(header);
        return (MessageID)header[2];
    }

    public static bool TryPeekPlayerSlot(in ReadOnlySequence<byte> frame, out byte playerSlot)
    {
        if (frame.Length < 4)
        {
            playerSlot = 0;
            return false;
        }

        if (frame.FirstSpan.Length >= 4)
        {
            playerSlot = frame.FirstSpan[3];
            return true;
        }

        Span<byte> header = stackalloc byte[4];
        frame.Slice(0, 4).CopyTo(header);
        playerSlot = header[3];
        return true;
    }

    public static InboundRouteDecision ClassifyClientFrame(
        in ReadOnlySequence<byte> frame,
        bool hasMiddlewareForMessage)
    {
        if (hasMiddlewareForMessage)
            return InboundRouteDecision.Inspect;

        var msgId = PeekMessageId(frame);
        return msgId switch
        {
            MessageID.ClientHello
            or MessageID.SyncPlayer
            or MessageID.SyncEquipment
            or MessageID.RequestWorldInfo
            or MessageID.RequestTileData
            or MessageID.SpawnPlayer
            or MessageID.PlayerControls
            or MessageID.PlayerHealth
            or MessageID.PlayerPvP
            or MessageID.PlayerZone
            or MessageID.SendPassword
            or MessageID.PlayerMana
            or MessageID.PlayerTeam
            or MessageID.PlayerBuffs
            or MessageID.ClientUUID
            or MessageID.NetModules
            or MessageID.PlayerStealth
            or MessageID.UpdatePlayerLuckFactors
            or MessageID.SyncLoadout
            or MessageID.PlayerPlatformInfo
            or MessageID.Unused15 => InboundRouteDecision.Inspect,
            _ => InboundRouteDecision.FastPassThrough,
        };
    }

    public static InboundRouteDecision ClassifyServerFrame(
        in ReadOnlySequence<byte> frame,
        byte activePlayerSlot,
        bool hasMiddlewareForMessage)
    {
        if (hasMiddlewareForMessage)
            return InboundRouteDecision.Inspect;

        var msgId = PeekMessageId(frame);
        return msgId switch
        {
            MessageID.Kick
            or MessageID.LoadPlayer
            or MessageID.WorldData
            or MessageID.Unused15 => InboundRouteDecision.Inspect,

            // High-frequency player state packets: inspect ONLY when targeting this session's own player slot.
            MessageID.SyncPlayer
            or MessageID.SyncEquipment
            or MessageID.SpawnPlayer
            or MessageID.PlayerControls
            or MessageID.PlayerHealth
            or MessageID.PlayerPvP
            or MessageID.PlayerZone
            or MessageID.PlayerMana
            or MessageID.PlayerTeam
            or MessageID.PlayerBuffs
            or MessageID.PlayerStealth
            or MessageID.UpdatePlayerLuckFactors
            or MessageID.SyncLoadout =>
                TryPeekPlayerSlot(frame, out var slot) && slot == activePlayerSlot
                    ? InboundRouteDecision.Inspect
                    : InboundRouteDecision.FastPassThrough,

            _ => InboundRouteDecision.FastPassThrough,
        };
    }
}
