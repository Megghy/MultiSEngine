namespace MultiSEngine.Application.Sessions;

public sealed class SessionStateMachine(SessionState initialState = SessionState.Accepted)
{
    public SessionState State { get; private set; } = initialState;

    public SessionState StableState { get; private set; } = initialState;

    public void TransitionTo(SessionState nextState)
    {
        if (!CanTransition(State, nextState))
            throw new InvalidOperationException($"Illegal session transition: {State} -> {nextState}");

        State = nextState;
        if (IsStableState(nextState))
            StableState = nextState;
    }

    public void RollbackToStableState()
    {
        if (!CanRollback(State))
            throw new InvalidOperationException($"Cannot rollback session state: {State}");

        State = StableState;
    }

    private static bool CanTransition(SessionState current, SessionState next)
    {
        if (current == next)
            return true;

        if (next == SessionState.Disconnecting)
            return current != SessionState.Disconnected;

        return (current, next) switch
        {
            (SessionState.Accepted, SessionState.HandshakingFakeWorld) => true,
            (SessionState.HandshakingFakeWorld, SessionState.IdleInFakeWorld) => true,
            (SessionState.IdleInFakeWorld, SessionState.PreparingTransfer) => true,
            (SessionState.PreparingTransfer, SessionState.ConnectingTarget) => true,
            (SessionState.ConnectingTarget, SessionState.AttachingTarget) => true,
            (SessionState.AttachingTarget, SessionState.SyncingClient) => true,
            (SessionState.SyncingClient, SessionState.InGameTarget) => true,
            (SessionState.InGameTarget, SessionState.PreparingTransfer) => true,
            (SessionState.InGameTarget, SessionState.ReturningToFakeWorld) => true,
            (SessionState.ReturningToFakeWorld, SessionState.IdleInFakeWorld) => true,
            (SessionState.Disconnecting, SessionState.Disconnected) => true,
            _ => false,
        };
    }

    private static bool CanRollback(SessionState current)
    {
        return current is
            SessionState.PreparingTransfer or
            SessionState.ConnectingTarget or
            SessionState.AttachingTarget or
            SessionState.SyncingClient or
            SessionState.ReturningToFakeWorld;
    }

    private static bool IsStableState(SessionState state)
        => state is SessionState.Accepted or SessionState.IdleInFakeWorld or SessionState.InGameTarget;
}
