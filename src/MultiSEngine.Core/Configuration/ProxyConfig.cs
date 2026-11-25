using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace MultiSEngine.Core.Configuration;

public sealed record ProxyConfig
{
    public string ListenIP { get; init; } = "0.0.0.0";
    public int ListenPort { get; init; } = 7778;
    public string ServerName { get; init; } = "MultiSEngine";
    public int ServerVersion { get; init; } = 329;
    public int SwitchTimeoutMs { get; init; } = 10_000;
    public int PasswordTimeoutMs { get; init; } = 60_000;
    public int HandshakeTimeoutMs { get; init; } = 15_000;
    public bool EnableCrossplay { get; init; } = true;
    public bool EnableChatForward { get; init; } = true;
    public string ChatFormat { get; init; } = "[{servername}] {username}: {message}";
    public bool SwitchToDefaultServerOnJoin { get; init; }
    public bool RestoreDataWhenJoinNonSsc { get; init; } = true;
    public bool DisableTcpDelay { get; init; } = true;
    public bool UseCrowdControlled { get; init; }
    public string DefaultServer { get; init; } = string.Empty;
    public ProxyLimitsConfig Limits { get; init; } = new();
    public CustomPacketSecurityConfig CustomPackets { get; init; } = new();
    public ImmutableArray<ServerDescriptor> Servers { get; init; } = [];

    [JsonIgnore]
    public ServerDescriptor? DefaultServerDescriptor =>
        string.IsNullOrWhiteSpace(DefaultServer)
            ? null
            : Servers.FirstOrDefault(s => string.Equals(s.Name, DefaultServer, StringComparison.OrdinalIgnoreCase));

    public ServerDescriptor? FindServer(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var trimmed = query.Trim();

        // 1. Exact name match
        foreach (var server in Servers)
        {
            if (string.Equals(server.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                return server;
        }

        // 2. Exact short name match
        foreach (var server in Servers)
        {
            if (!string.IsNullOrEmpty(server.ShortName) &&
                string.Equals(server.ShortName, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return server;
            }
        }

        // 3. Prefix match
        foreach (var server in Servers)
        {
            if (server.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                return server;
        }

        // 4. Substring match
        foreach (var server in Servers)
        {
            if (server.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                return server;
        }

        return null;
    }

    public static ProxyConfig CreateDefault() => new()
    {
        SwitchToDefaultServerOnJoin = false,
        DefaultServer = "lobby",
        Servers =
        [
            new ServerDescriptor
            {
                Name = "lobby",
                ShortName = "l",
                Host = "127.0.0.1",
                Port = 7777,
                Visible = true,
                VersionNum = -1,
            },
        ],
    };
}

public sealed record ServerDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 7777;
    public int VersionNum { get; init; } = -1;
    public bool Visible { get; init; } = true;
    public short SpawnX { get; init; } = -1;
    public short SpawnY { get; init; } = -1;
    public string? SharedSecret { get; init; }
    public bool AllowClusterCommands { get; init; }
}

public sealed record ProxyLimitsConfig
{
    public int MaxConnections { get; init; } = 256;
    public int SendPipePauseThresholdBytes { get; init; } = 512 * 1024;
    public int SendPipeResumeThresholdBytes { get; init; } = 256 * 1024;
    public int SendFlushTimeoutMs { get; init; } = 10_000;
    public int MaxPreConnectBufferedBytes { get; init; } = 8 * 1024 * 1024;
    public int MaxHandshakePacketsPerSecond { get; init; } = 256;
    public int SessionMailboxCapacity { get; init; } = 256;
}

public sealed record CustomPacketSecurityConfig
{
    public string GlobalSharedSecret { get; init; } = string.Empty;
    public int MaxClockSkewSeconds { get; init; } = 30;
    public bool AllowUnsignedInDevelopment { get; init; }
}
