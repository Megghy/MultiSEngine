using MultiSEngine.Protocol.CustomPackets;
using TShockAPI;

namespace MultiSEngine.TShock;

public sealed class CustomPacketReceivedEventArgs(
    int playerIndex,
    TSPlayer? player,
    string packetType,
    ReadOnlyMemory<byte> payload,
    bool isAuthenticated) : EventArgs
{
    public int PlayerIndex { get; } = playerIndex;
    public TSPlayer? Player { get; } = player;
    public string PacketType { get; } = packetType;
    public ReadOnlyMemory<byte> Payload { get; } = payload;
    public bool IsAuthenticated { get; } = isAuthenticated;
    public bool Handled { get; set; }
}

/// <summary>
/// Public static bridge API for TShock 6 plugins to inspect MultiSEngine proxy state
/// and send signed MSE2 custom packets back to MultiSEngine.
/// </summary>
public static class MultiSEngineBridge
{
    private static MultiSEngineTShockPlugin? _plugin;

    /// <summary>
    /// Raised whenever a verified MSE2 custom packet arrives from MultiSEngine.
    /// </summary>
    public static event EventHandler<CustomPacketReceivedEventArgs>? CustomPacketReceived;

    internal static void Attach(MultiSEngineTShockPlugin plugin) => _plugin = plugin;

    internal static void Detach(MultiSEngineTShockPlugin plugin)
    {
        if (ReferenceEquals(_plugin, plugin))
            _plugin = null;
    }

    internal static void RaiseCustomPacketReceived(CustomPacketReceivedEventArgs args)
    {
        var handlers = CustomPacketReceived;
        if (handlers is null)
            return;

        foreach (var single in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<CustomPacketReceivedEventArgs>)single).Invoke(null, args);
            }
            catch (Exception ex)
            {
                TShockAPI.TShock.Log.ConsoleError($"[MultiSEngine.TShock] CustomPacketReceived subscriber error: {ex}");
            }
        }
    }

    public static bool IsProxied(int playerIndex)
        => _plugin?.IsProxied(playerIndex) ?? false;

    public static string? GetSyncedIp(int playerIndex)
        => _plugin?.GetSyncedIp(playerIndex);

    /// <summary>
    /// Requests MultiSEngine to transfer the specified player to another configured backend server.
    /// </summary>
    public static bool SendSwitchServer(TSPlayer player, string targetServerName)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetServerName);
        var payload = BuiltInCustomPacketTypes.EncodeSwitchServerPayload(targetServerName);
        return SendCustomPacket(player, BuiltInCustomPacketTypes.SwitchServer, payload);
    }

    /// <summary>
    /// Requests MultiSEngine to execute a proxy command for the specified player (or another player if AllowClusterCommands is enabled on the proxy).
    /// </summary>
    public static bool SendExecuteCommand(TSPlayer player, string commandText, string? targetPlayerName = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        var payload = BuiltInCustomPacketTypes.EncodeExecuteCommandPayload(targetPlayerName ?? player.Name, commandText);
        return SendCustomPacket(player, BuiltInCustomPacketTypes.ExecuteCommand, payload);
    }

    /// <summary>
    /// Encodes, HMAC-signs, and sends an MSE2 custom packet over Terraria MessageID.Unused15 (15) to MultiSEngine.
    /// </summary>
    public static bool SendCustomPacket(TSPlayer player, string packetType, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(player);
        var plugin = _plugin;
        if (plugin is null || !player.RealPlayer || !player.ConnectionAlive)
            return false;

        return plugin.SendSignedCustomPacket(player, packetType, payload);
    }
}
