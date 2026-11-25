using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Sessions;

namespace MultiSEngine.Core.Extensions;

public sealed class PlayerJoiningEvent(ISessionHandle session, string remoteIp, int remotePort, string rawVersion)
{
    public ISessionHandle Session { get; } = session;
    public string RemoteIp { get; } = remoteIp;
    public int RemotePort { get; } = remotePort;
    public string RawVersion { get; set; } = rawVersion;
    public bool Cancel { get; set; }
    public string? CancelReason { get; set; }
}

public sealed class PlayerJoinedEvent(ISessionHandle session)
{
    public ISessionHandle Session { get; } = session;
}

public sealed class PlayerLeftEvent(ISessionHandle session, string? reason)
{
    public ISessionHandle Session { get; } = session;
    public string? Reason { get; } = reason;
}

public sealed class PreSwitchEvent(ISessionHandle session, ServerDescriptor targetServer)
{
    public ISessionHandle Session { get; } = session;
    public ServerDescriptor TargetServer { get; set; } = targetServer;
    public bool Cancel { get; set; }
    public string? CancelReason { get; set; }
}

public sealed class PostSwitchEvent(ISessionHandle session, ServerDescriptor currentServer, ServerDescriptor? previousServer)
{
    public ISessionHandle Session { get; } = session;
    public ServerDescriptor CurrentServer { get; } = currentServer;
    public ServerDescriptor? PreviousServer { get; } = previousServer;
}

public sealed class PlayerChatEvent(ISessionHandle session, string command, string message)
{
    public ISessionHandle Session { get; } = session;
    public string Command { get; } = command;
    public string Message { get; set; } = message;
    public bool Handled { get; set; }
}

public sealed class CustomPacketReceivedEvent(CustomPacketContext packet)
{
    public CustomPacketContext Packet { get; } = packet;
    public bool Handled { get; set; }
}
