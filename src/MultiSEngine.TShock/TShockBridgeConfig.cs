using System.Net;
using System.Text.Json;

namespace MultiSEngine.TShock;

public sealed class TShockBridgeConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Shared HMAC-SHA256 secret matching MultiSEngine's CustomPackets.GlobalSharedSecret or per-server SharedSecret.
    /// </summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>
    /// Allowed clock skew in seconds for incoming MSE2 packets.
    /// </summary>
    public int MaxClockSkewSeconds { get; set; } = 30;

    /// <summary>
    /// Permits unsigned MSE2 packets for local development only. Keep false in production.
    /// </summary>
    public bool AllowUnsignedInDevelopment { get; set; }

    /// <summary>
    /// When false, connections that do not originate from a trusted proxy IP or never authenticate via mse.sync_ip are disconnected.
    /// </summary>
    public bool AllowDirectJoin { get; set; } = true;

    /// <summary>
    /// Proxy source IP addresses allowed to supply mse.sync_ip real-IP overrides.
    /// </summary>
    public string[] TrustedProxyIps { get; set; } = ["127.0.0.1", "::1"];

    /// <summary>
    /// Immediately re-evaluates TShock IP bans against the synced real client IP.
    /// </summary>
    public bool EnforceIpBansAfterSync { get; set; } = true;

    /// <summary>
    /// Rejection message shown when AllowDirectJoin is false and a client bypasses the proxy.
    /// </summary>
    public string DirectJoinKickMessage { get; set; } = "Please connect through the MultiSEngine proxy.";

    public bool IsTrustedProxyIp(string? remoteIp)
    {
        if (string.IsNullOrWhiteSpace(remoteIp))
            return false;

        if (!IPAddress.TryParse(remoteIp, out var incoming))
            return false;

        var normalizedIncoming = NormalizeIp(incoming);
        foreach (var entry in TrustedProxyIps)
        {
            if (string.Equals(entry, "*", StringComparison.Ordinal))
                return true;

            if (IPAddress.TryParse(entry, out var allowed) &&
                NormalizeIp(allowed).Equals(normalizedIncoming))
            {
                return true;
            }
        }

        return false;
    }

    public static TShockBridgeConfig LoadOrCreate(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(path))
        {
            var created = new TShockBridgeConfig();
            SaveAtomic(path, created);
            return created;
        }

        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<TShockBridgeConfig>(json, JsonOptions) ?? new TShockBridgeConfig();
        loaded.MaxClockSkewSeconds = Math.Clamp(loaded.MaxClockSkewSeconds, 5, 300);
        loaded.TrustedProxyIps ??= ["127.0.0.1", "::1"];
        return loaded;
    }

    public static void SaveAtomic(string path, TShockBridgeConfig config)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    private static IPAddress NormalizeIp(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
