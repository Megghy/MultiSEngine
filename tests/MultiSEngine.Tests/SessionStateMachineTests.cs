using MultiSEngine.Application.Sessions;

namespace MultiSEngine.Tests;

public sealed class SessionStateMachineTests
{
    [Fact]
    public void FakeWorldHandshakeFlow_ReachesIdleStableState()
    {
        var session = new SessionStateMachine();

        session.TransitionTo(SessionState.HandshakingFakeWorld);
        session.TransitionTo(SessionState.IdleInFakeWorld);

        Assert.Equal(SessionState.IdleInFakeWorld, session.State);
        Assert.Equal(SessionState.IdleInFakeWorld, session.StableState);
    }

    [Fact]
    public void TransferFlow_ReachesTargetStableState()
    {
        var session = CreateInGameTargetSession();

        Assert.Equal(SessionState.InGameTarget, session.State);
        Assert.Equal(SessionState.InGameTarget, session.StableState);
    }

    [Fact]
    public void IllegalTransition_Throws()
    {
        var session = new SessionStateMachine();

        var exception = Assert.Throws<InvalidOperationException>(() => session.TransitionTo(SessionState.InGameTarget));

        Assert.Contains("Illegal session transition", exception.Message);
    }

    [Fact]
    public void Rollback_RestoresLatestStableState()
    {
        var session = CreateInGameTargetSession();

        session.TransitionTo(SessionState.PreparingTransfer);
        session.TransitionTo(SessionState.ConnectingTarget);
        session.RollbackToStableState();

        Assert.Equal(SessionState.InGameTarget, session.State);
        Assert.Equal(SessionState.InGameTarget, session.StableState);
    }

    [Fact]
    public void Rollback_FromStableState_Throws()
    {
        var session = new SessionStateMachine();
        session.TransitionTo(SessionState.HandshakingFakeWorld);
        session.TransitionTo(SessionState.IdleInFakeWorld);

        var exception = Assert.Throws<InvalidOperationException>(session.RollbackToStableState);

        Assert.Contains("Cannot rollback", exception.Message);
    }

    [Fact]
    public void DisconnectFlow_CompletesFromNonDisconnectedState()
    {
        var session = new SessionStateMachine();

        session.TransitionTo(SessionState.Disconnecting);
        session.TransitionTo(SessionState.Disconnected);

        Assert.Equal(SessionState.Disconnected, session.State);
    }

    private static SessionStateMachine CreateInGameTargetSession()
    {
        var session = new SessionStateMachine();
        session.TransitionTo(SessionState.HandshakingFakeWorld);
        session.TransitionTo(SessionState.IdleInFakeWorld);
        session.TransitionTo(SessionState.PreparingTransfer);
        session.TransitionTo(SessionState.ConnectingTarget);
        session.TransitionTo(SessionState.AttachingTarget);
        session.TransitionTo(SessionState.SyncingClient);
        session.TransitionTo(SessionState.InGameTarget);
        return session;
    }
}
