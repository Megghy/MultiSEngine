using System.Collections.Immutable;
using System.Net;
using Microsoft.Xna.Framework;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Transfers;
using TrProtocol;

namespace MultiSEngine.Core.Sessions;

public sealed record SessionSnapshot(
    Guid SessionId,
    string PlayerName,
    string PlayerUuid,
    string RemoteIp,
    int RemotePort,
    int ProtocolVersion,
    SessionPhase Phase,
    string? CurrentServerName,
    string? LastServerName,
    byte ActivePlayerSlot,
    DateTimeOffset ConnectedAtUtc);

public interface ISessionHandle
{
    Guid SessionId { get; }
    SessionSnapshot GetSnapshot();
    SessionState CurrentState { get; }
    IPEndPoint RemoteEndPoint { get; }
    string PlayerName { get; }
    string PlayerUuid { get; }
    int ProtocolVersion { get; }
    ServerDescriptor? CurrentServer { get; }

    ValueTask<TransferResult> TransferToAsync(ServerDescriptor target, CancellationToken cancellationToken = default);
    bool TryRequestTransfer(ServerDescriptor target);
    ValueTask<TransferResult> ReturnBackAsync(string? reason = null, CancellationToken cancellationToken = default);
    bool TryRequestReturnBack(string? reason = null);
    ValueTask<bool> SubmitPasswordAsync(string password, CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default);

    ValueTask SendChatAsync(string text, Color color, bool withPrefix = true, CancellationToken cancellationToken = default);
    ValueTask SendPacketToClientAsync(INetPacket packet, bool fromServer = true, CancellationToken cancellationToken = default);
    ValueTask SendPacketToServerAsync(INetPacket packet, CancellationToken cancellationToken = default);
    ValueTask SendRawToClientAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);
    ValueTask SendRawToServerAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);
}

public interface ISessionRegistry
{
    int Count { get; }
    bool TryRegister(ISessionHandle session);
    bool TryRemove(Guid sessionId, out ISessionHandle? removed);
    bool TryGet(Guid sessionId, out ISessionHandle? session);
    ISessionHandle? FindByName(string playerName);
    ImmutableArray<ISessionHandle> GetSessions();
    ImmutableArray<SessionSnapshot> GetSnapshots();
}
