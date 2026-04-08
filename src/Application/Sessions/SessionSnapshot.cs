namespace MultiSEngine.Application.Sessions;

public sealed record SessionSnapshot(
    long SessionId,
    string Name,
    string Address,
    string? CurrentServerName,
    ClientState State);
