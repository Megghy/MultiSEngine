using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Sessions;

namespace MultiSEngine.Core.Security;

[Flags]
public enum CommandSourceKind
{
    None = 0,
    Console = 1 << 0,
    Player = 1 << 1,
    BackendServer = 1 << 2,
    All = Console | Player | BackendServer,
}

public sealed record CommandContext(
    CommandSourceKind SourceKind,
    string CommandName,
    IReadOnlyList<string> Arguments,
    string RawText,
    ISessionHandle? PlayerSession = null,
    ServerDescriptor? BackendServer = null,
    bool HasClusterPermission = false);

public sealed record CommandExecutionResult(
    bool Handled,
    bool ForwardToServer = false,
    bool RequestShutdown = false,
    string? ErrorMessage = null)
{
    public static CommandExecutionResult Intercepted(bool requestShutdown = false)
        => new(Handled: true, ForwardToServer: false, RequestShutdown: requestShutdown);

    public static CommandExecutionResult PassThrough()
        => new(Handled: false, ForwardToServer: true);

    public static CommandExecutionResult HandledAndForward()
        => new(Handled: true, ForwardToServer: true);
}
