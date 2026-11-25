using MultiSEngine.Core.Configuration;

namespace MultiSEngine.Core.Sessions;

public enum SessionPhase
{
    Handshaking,
    Lobby,
    TransferConnecting,
    TransferAwaitingPassword,
    TransferSyncing,
    InServer,
    ReturningToLobby,
    Disconnecting,
    Disconnected,
}

public enum HandshakeStage
{
    AwaitingHello,
    AwaitingWorldInfo,
    AwaitingTileData,
    AwaitingSpawn,
}

/// <summary>
/// Single source of truth for a session's lifecycle state.
/// </summary>
public abstract record SessionState
{
    public abstract SessionPhase Phase { get; }
    public virtual ServerDescriptor? CurrentServer => null;
    public virtual ServerDescriptor? LastServer => null;
    public virtual byte ActivePlayerSlot => 0;

    public sealed record Handshaking(HandshakeStage Stage) : SessionState
    {
        public override SessionPhase Phase => SessionPhase.Handshaking;
    }

    public sealed record Lobby(ServerDescriptor? LastServerInfo) : SessionState
    {
        public override SessionPhase Phase => SessionPhase.Lobby;
        public override ServerDescriptor? LastServer => LastServerInfo;
    }

    public sealed record TransferConnecting(
        ServerDescriptor TargetServer,
        ServerDescriptor? PreviousServer,
        ServerDescriptor? LastServerInfo,
        byte CurrentSlot,
        long TransferId) : SessionState
    {
        public override SessionPhase Phase => SessionPhase.TransferConnecting;
        public override ServerDescriptor? CurrentServer => PreviousServer;
        public override ServerDescriptor? LastServer => LastServerInfo;
        public override byte ActivePlayerSlot => CurrentSlot;
    }

    public sealed record TransferAwaitingPassword(
        ServerDescriptor TargetServer,
        ServerDescriptor? PreviousServer,
        ServerDescriptor? LastServerInfo,
        byte CurrentSlot,
        long TransferId,
        bool RetryPrompted) : SessionState
    {
        public override SessionPhase Phase => SessionPhase.TransferAwaitingPassword;
        public override ServerDescriptor? CurrentServer => PreviousServer;
        public override ServerDescriptor? LastServer => LastServerInfo;
        public override byte ActivePlayerSlot => CurrentSlot;
    }

    public sealed record TransferSyncing(
        ServerDescriptor TargetServer,
        ServerDescriptor? PreviousServer,
        byte TargetSlot,
        long TransferId) : SessionState
    {
        public override SessionPhase Phase => SessionPhase.TransferSyncing;
        public override ServerDescriptor? CurrentServer => PreviousServer;
        public override ServerDescriptor? LastServer => PreviousServer;
        public override byte ActivePlayerSlot => TargetSlot;
    }

    public sealed record InServer(
        ServerDescriptor Server,
        ServerDescriptor? PreviousServer,
        byte RemotePlayerSlot,
        long UpstreamLinkId) : SessionState
    {
        public override SessionPhase Phase => SessionPhase.InServer;
        public override ServerDescriptor? CurrentServer => Server;
        public override ServerDescriptor? LastServer => PreviousServer;
        public override byte ActivePlayerSlot => RemotePlayerSlot;
    }

    public sealed record ReturningToLobby(
        ServerDescriptor FromServer,
        ServerDescriptor? PreviousServer) : SessionState
    {
        public override SessionPhase Phase => SessionPhase.ReturningToLobby;
        public override ServerDescriptor? CurrentServer => FromServer;
        public override ServerDescriptor? LastServer => PreviousServer;
    }

    public sealed record Disconnecting(string? Reason) : SessionState
    {
        public override SessionPhase Phase => SessionPhase.Disconnecting;
    }

    public sealed record Disconnected(string? Reason) : SessionState
    {
        public override SessionPhase Phase => SessionPhase.Disconnected;
    }
}
