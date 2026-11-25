using MultiSEngine.Core.Configuration;

namespace MultiSEngine.Core.Transfers;

public enum TransferStatus
{
    Succeeded,
    Started,
    AlreadyInTarget,
    InvalidState,
    CancelledByHook,
    InvalidAddress,
    ConnectionFailed,
    KickedByTarget,
    TimedOut,
    PasswordTimedOut,
    SyncFailed,
    SessionClosed,
}

public sealed record TransferResult(
    TransferStatus Status,
    ServerDescriptor? TargetServer,
    string? Message = null)
{
    public bool IsSuccess => Status == TransferStatus.Succeeded;
}

public sealed record ProbeResult(
    ServerDescriptor Server,
    bool IsAvailable,
    TimeSpan Latency,
    string? FailureReason = null);

public interface IServerProbeService
{
    Task<ProbeResult> ProbeAsync(ServerDescriptor server, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProbeResult>> ProbeAllAsync(IEnumerable<ServerDescriptor> servers, TimeSpan timeout, CancellationToken cancellationToken = default);
}
