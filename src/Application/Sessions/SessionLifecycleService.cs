namespace MultiSEngine.Application.Sessions;

public static class SessionLifecycleService
{
    public static void MarkFakeWorldHandshakeStarted(ClientData client)
        => client.Session.TransitionTo(SessionState.HandshakingFakeWorld);

    public static void MarkFakeWorldEntered(ClientData client)
        => client.Session.TransitionTo(SessionState.IdleInFakeWorld);

    public static void BeginTransfer(ClientData client)
        => client.Session.TransitionTo(SessionState.PreparingTransfer);

    public static void MarkTargetConnecting(ClientData client)
        => client.Session.TransitionTo(SessionState.ConnectingTarget);

    public static void MarkTargetAttaching(ClientData client)
        => client.Session.TransitionTo(SessionState.AttachingTarget);

    public static void MarkClientSyncing(ClientData client)
        => client.Session.TransitionTo(SessionState.SyncingClient);

    public static void MarkTargetEntered(ClientData client)
        => client.Session.TransitionTo(SessionState.InGameTarget);

    public static void BeginReturnToFakeWorld(ClientData client)
        => client.Session.TransitionTo(SessionState.ReturningToFakeWorld);

    public static void CompleteReturnToFakeWorld(ClientData client)
        => client.Session.TransitionTo(SessionState.IdleInFakeWorld);

    public static void RollbackToStableState(ClientData client)
        => client.Session.RollbackToStableState();

    public static void BeginDisconnect(ClientData client)
    {
        if (client.Session.State != SessionState.Disconnected)
            client.Session.TransitionTo(SessionState.Disconnecting);
    }

    public static void CompleteDisconnect(ClientData client)
    {
        if (client.Session.State != SessionState.Disconnected)
            client.Session.TransitionTo(SessionState.Disconnected);
    }
}
