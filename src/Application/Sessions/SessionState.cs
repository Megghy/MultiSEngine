namespace MultiSEngine.Application.Sessions;

public enum SessionState
{
    Accepted,
    HandshakingFakeWorld,
    IdleInFakeWorld,
    PreparingTransfer,
    ConnectingTarget,
    AttachingTarget,
    SyncingClient,
    InGameTarget,
    ReturningToFakeWorld,
    Disconnecting,
    Disconnected,
}
