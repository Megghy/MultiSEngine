# Hooks and Custom Packets

## Lifecycle hooks

Plugins subscribe to async hooks via `IPluginContext`. Subscriber exceptions are isolated so one faulty plugin cannot break other subscribers or crash the proxy.

| Registration | Event type | When it fires |
| --- | --- | --- |
| `context.OnPlayerJoin` | `PlayerJoinEvent` | When a client completes initial `ClientHello` validation. Set `Handled = true` to reject the join. |
| `context.OnPlayerLeave` | `PlayerLeaveEvent` | When a session closes and cleans up. |
| `context.OnPreSwitch` | `PreSwitchEvent` | Before an upstream pre-connect starts. Set `Handled = true` to cancel the transfer. |
| `context.OnPostSwitch` | `PostSwitchEvent` | Only **after** the sync batch succeeds and upstream ownership has atomically switched. |
| `context.OnChat` | `ChatEvent` | When player chat arrives. Set `Handled = true` to suppress forwarding. |

## Packet middleware

Use `context.RegisterPacketMiddleware(direction, messageIds, middleware)` to intercept specific `MessageID` frames on `ClientToServer`, `ServerToClient`, or `Both`.

- Subscribing only to the exact `MessageID`s you need keeps all other packets on the zero-copy O(1) fast path.
- Middleware returns:
  - `PacketMiddlewareResult.Continue` — forward unmodified
  - `PacketMiddlewareResult.Drop` — suppress the frame
  - `PacketMiddlewareResult.Replace(ReadOnlyMemory<byte>)` — replace the wire frame

## `MSE2` authenticated custom packets

MultiSEngine v2 replaces the unauthenticated legacy custom-data format with **`MSE2`**, transported inside Terraria `MessageID.Unused15` (`15`).

### Security rules
- **Client -> Server `Unused15` is dropped by default** so clients can never forge cluster packets.
- Every `MSE2` packet carries a **32-byte HMAC-SHA256** tag verified in constant time, a UTC millisecond timestamp checked against `CustomPackets.MaxClockSkewSeconds` (default `30s`), and an 8-byte random nonce tracked in a replay-protection sliding window.
- Secret resolution uses `ServerDescriptor.SharedSecret` if configured, falling back to `CustomPackets.GlobalSharedSecret`.

### Wire layout (`Unused15` payload)

| Field | Size | Description |
| --- | --- | --- |
| Terraria header | 3 bytes | `ushort Length` (LE) + `byte 15` (`MessageID.Unused15`) |
| Magic | 4 bytes | ASCII `"MSE2"` (`0x4D 0x53 0x45 0x32`) |
| Version | 1 byte | `0x02` |
| Type length + UTF-8 Type | 1 + N bytes | Packet type name (e.g. `mse.sync_ip`) |
| TimestampMs | 8 bytes | Int64 little-endian UTC Unix milliseconds |
| Nonce | 8 bytes | Cryptographically random 64-bit nonce |
| Payload length + Payload | 4 + M bytes | Int32 little-endian length + binary payload |
| Signature | 32 bytes | `HMAC-SHA256(secret, Magic..Payload)` |

### Built-in `MSE2` packet types

| Type | Direction | Purpose |
| --- | --- | --- |
| `mse.sync_ip` | Proxy -> Backend | Sends the player's real client IP address (`string playerName`, `string clientIp`) during handshake. |
| `mse.execute_command` | Backend -> Proxy | Requests a proxy command on a session (`string targetPlayerName`, `string commandLine`). Cross-player execution is denied unless the source server has `AllowClusterCommands = true`. |
| `mse.switch_server` | Backend -> Proxy | Requests transferring the player to a named target server (`string targetServerName`). |

### Handling custom packet types in a plugin

```csharp
context.RegisterCustomPacketHandler("myplugin.ping", async (packetCtx, ct) =>
{
    context.Logger.Info(
        $"Verified packet from {packetCtx.SourceServer.Name} for {packetCtx.Session.PlayerName} ({packetCtx.Payload.Length} bytes).",
        "MyPlugin");
    await ValueTask.CompletedTask;
});
```

## Official TShock 6 companion plugin (`MultiSEngine.TShock`)

`src/MultiSEngine.TShock` targets **TShock 6.2.1 (`net9.0`)**, shares the exact `MSE2` codec at compile time, and is published as a single zero-dependency DLL (`MultiSEngine.TShock-v*.dll`) on every GitHub Release.

### Setup on backend TShock 6 servers

1. Copy `MultiSEngine.TShock-v*.dll` into TShock's `ServerPlugins/` directory and start the server once to generate `tshock/MultiSEngine.json`.
2. Set `SharedSecret` in `tshock/MultiSEngine.json` to match `CustomPackets.GlobalSharedSecret` (or the backend's `Servers[].SharedSecret`) in MultiSEngine's `Config.json`.
3. Optionally set `AllowDirectJoin` to `false` and configure `TrustedProxyIps` (defaults to `["127.0.0.1", "::1"]`) so players cannot bypass the proxy.

### Capabilities & public API

- **Real IP restoration (`mse.sync_ip`)**: Verifies the HMAC signature and trusted proxy IP, updates both Terraria's `TcpSocket._remoteAddress` and TShock's cached `TSPlayer.IP` without reflection, and immediately re-checks TShock IP bans (`EnforceIpBansAfterSync = true`).
- **Built-in commands**: `/mseswitch <server>` (`mse.switch`) to request a transfer via `mse.switch_server`, and `/msereload` (`mse.admin`).
- **Plugin bridge API (`MultiSEngine.TShock.MultiSEngineBridge`)**:
  - `MultiSEngineBridge.IsProxied(int playerIndex)` / `GetSyncedIp(int playerIndex)`
  - `MultiSEngineBridge.SendSwitchServer(TSPlayer player, string targetServerName)`
  - `MultiSEngineBridge.SendExecuteCommand(TSPlayer player, string commandText, string? targetPlayerName = null)`
  - `MultiSEngineBridge.SendCustomPacket(TSPlayer player, string packetType, ReadOnlySpan<byte> payload)`
  - `MultiSEngineBridge.CustomPacketReceived` event for verified incoming `MSE2` packets

