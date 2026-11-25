using System.Net;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.Net;
using Terraria.Net.Sockets;
using TShockAPI;
using TShockAPI.DB;

namespace MultiSEngine.TShock;

/// <summary>
/// Applies the verified real client IP to both Terraria's RemoteClient TcpSocket and TShock's cached TSPlayer.IP
/// without reflection, and enforces TShock IP bans against the real address.
/// </summary>
internal static class RealIpApplicator
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "CacheIP")]
    private static extern ref string? GetCacheIpRef(TSPlayer player);

    public static string? GetSocketIp(int playerIndex)
    {
        if ((uint)playerIndex >= (uint)Netplay.Clients.Length)
            return null;

        var socket = Netplay.Clients[playerIndex]?.Socket;
        if (socket is null)
            return null;

        var raw = socket.GetRemoteAddress()?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return TShockAPI.TShock.Utils.GetRealIP(raw);
    }

    public static bool TryApply(int playerIndex, string rawIp, out string normalizedIp)
    {
        normalizedIp = string.Empty;
        if ((uint)playerIndex >= (uint)Netplay.Clients.Length || !IPAddress.TryParse(rawIp, out var parsed))
            return false;

        var ip = parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4() : parsed;
        normalizedIp = ip.ToString();

        var client = Netplay.Clients[playerIndex];
        if (client?.Socket is TcpSocket tcpSocket)
        {
            var port = tcpSocket._remoteAddress is TcpAddress existing ? existing.Port : 0;
            tcpSocket._remoteAddress = new TcpAddress(ip, port);
        }

        if ((uint)playerIndex < (uint)TShockAPI.TShock.Players.Length &&
            TShockAPI.TShock.Players[playerIndex] is { } player)
        {
            GetCacheIpRef(player) = normalizedIp;
        }

        return true;
    }

    public static void ReapplyToPlayer(TSPlayer player, string normalizedIp)
    {
        GetCacheIpRef(player) = normalizedIp;
    }

    public static bool TryFindActiveIpBan(TSPlayer player, string normalizedIp, out string? reason)
    {
        reason = null;
        var identifier = $"{Identifier.IP}{normalizedIp}";
        foreach (var ban in TShockAPI.TShock.Bans.RetrieveBansByIdentifier(identifier, currentOnly: true))
        {
            if (TShockAPI.TShock.Bans.IsValidBan(ban, player))
            {
                reason = string.IsNullOrWhiteSpace(ban.Reason) ? "Banned." : ban.Reason;
                return true;
            }
        }

        return false;
    }
}
